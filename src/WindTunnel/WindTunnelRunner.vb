' /********************************************************************************/
'
'   WindTunnelRunner.vb
'
'   EulerianFvm 的编排层：加载/生成网格 → 装配算例 → 求解 → 输出动画序列与报告。
'
'   输出（<out> 目录）：
'       frame_*.vtu        非结构多面体网格快照（--format vtu|both）
'       animation.pvd      ParaView 时间集合
'       frames.json        浏览器端轻量帧清单
'       metadata.json      网格与求解配置元数据
'       summary.json       气动力 / 搅拌功率 / 流量最终报告
'   --format vti|both 时另写入 <out>/vti（Eulerian 解重采样到结构笛卡尔格）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports Moira.CFDEngine.Snapshot
Imports Moira.FVMEngine.Eulerian
Imports Moira.ZMesh

Namespace WindTunnel

    ''' <summary>命令行编排。</summary>
    Public Module WindTunnelRunner

        ''' <summary>执行一次完整运行，返回进程退出码（0 = 成功）。</summary>
        Public Function Run(o As CliOptions) As Integer
            If o Is Nothing Then Return 2

            If Not String.IsNullOrEmpty(o.ErrorMessage) Then
                Console.Error.WriteLine($"参数错误：{o.ErrorMessage}")
                Console.Error.WriteLine()
                Console.Error.WriteLine(CliOptions.Usage)
                Return 2
            End If

            If o.Help Then
                Console.WriteLine(CliOptions.Usage)
                Return 0
            End If

            Try
                Select Case o.Command
                    Case CliCommand.WindTunnel
                        Return RunWindTunnel(o)
                    Case CliCommand.Tank
                        Return RunTank(o)
                    Case Else
                        Console.Error.WriteLine("缺少子命令（windtunnel | tank）")
                        Return 2
                End Select
            Catch ex As Exception
                Console.Error.WriteLine($"运行失败：{ex.GetType().Name}: {ex.Message}")
                If o.Verbose Then Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

#Region "GPU 与注册"

        Private Function EnableGpu(o As CliOptions) As Boolean
            If Not o.UseGpu Then
                Console.WriteLine("CUDA float32 后端：已禁用（--no-gpu）")
                Return False
            End If

            Dim ok As Boolean = EulerianFvmCompute.TryRegister()

            If ok Then
                Console.WriteLine($"CUDA float32 后端：已启用（{EulerianFvmCompute.Current.DescribeDevice()}）")
            Else
                Console.WriteLine($"CUDA float32 后端：不可用，回落 CPU double 路径（{EulerianFvmCompute.LastError}）")
            End If

            Return ok
        End Function

#End Region

#Region "风洞"

        Private Function RunWindTunnel(o As CliOptions) As Integer
            If String.IsNullOrEmpty(o.MeshZip) Then
                Console.Error.WriteLine("windtunnel 需要 --mesh <eulerian.zip>（由 src\\EulerianMesh 生成）")
                Return 2
            End If

            Dim timer As Stopwatch = Stopwatch.StartNew()
            Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(o.MeshZip)

            Console.WriteLine(mesh.Diagnostics.TrimEnd())

            Call EnableGpu(o)

            Dim wopts As New EulerianWindTunnelOptions With {
                .InletSpeed = o.InletSpeed,
                .InletDirection = New EulerianVertex(o.InletDirX, o.InletDirY, o.InletDirZ),
                .Density = o.Density,
                .KinematicViscosity = o.Nu,
                .Turbulence = o.TurbulenceKind(),
                .TurbulenceIntensity = o.TurbulenceIntensity,
                .TurbulenceLengthScale = If(o.TurbulenceLengthScale > 0, o.TurbulenceLengthScale,
                                            AutoTurbulenceLength(mesh)),
                .Compressible = o.IsCompressible(),
                .Mach = o.Mach,
                .Temperature = o.Temperature,
                .FluxScheme = o.FluxScheme,
                .Steps = o.Steps,
                .Transient = o.Transient,
                .DeltaT = o.DeltaT,
                .Cfl = o.Cfl,
                .Scheme = o.ConvectionScheme(),
                .RelaxU = o.RelaxU,
                .PressureMaxIterations = o.PressureIterations,
                .UseGpu = o.UseGpu,
                .ForceInterval = o.Interval,
                .ReferenceArea = If(o.ReferenceArea > 0, o.ReferenceArea, AutoReferenceArea(mesh)),
                .ReferenceLength = If(o.ReferenceLength > 0, o.ReferenceLength, AutoReferenceLength(mesh))}

            If o.Probes.Count > 0 Then
                ' 探针只影响日志（求解器本身不需要）
            End If

            Dim testCase As New EulerianWindTunnelCase(mesh, wopts)

            Call testCase.Configure()

            Console.WriteLine($"网格：{mesh.CellCount} 单元 / {mesh.FaceCount} 面 / {mesh.PatchCount} patch（{mesh.PatchNames()}）")
            Console.WriteLine($"来流：{o.InletSpeed:G6} m/s，湍流：{o.Turbulence}，物理：{If(o.IsCompressible(), "compressible/" & o.FluxScheme, "incompressible")}")
            Console.WriteLine($"参考量：A={wopts.ReferenceArea:G6} m², L={wopts.ReferenceLength:G6} m")

            Dim recorders As List(Of IEulerianSnapshotRecorder) = BuildRecorders(o, mesh)

            ' 逐帧快照：采样间隔由记录器内部判定（此处每步都提交，代价极低）
            Call testCase.Run(o.Steps, Sub(stepIndex, forces)
                                           Call CaptureAll(recorders, mesh, testCase.Fields, stepIndex, stepIndex)
                                       End Sub)

            Call FinishAll(recorders, testCase.Fields)

            timer.Stop()

            Console.WriteLine()
            Console.WriteLine(testCase.Report)

            If o.Report Then
                Console.WriteLine($"总耗时：{timer.Elapsed.TotalSeconds:F1} s")
            End If

            Call WriteSummary(o, "windtunnel",
                              If(testCase.LastForces IsNot Nothing, testCase.LastForces.ToString(), "no force data"),
                              testCase)

            Return 0
        End Function

#End Region

#Region "发酵罐"

        Private Function RunTank(o As CliOptions) As Integer
            Dim timer As Stopwatch = Stopwatch.StartNew()

            Dim geometry As FermentationTankGeometry = FermentationTankGeometry.CreateDefault(o.TankDiameter, o.Rpm)

            ' 高度可覆盖；液位未显式给出时始终取 2/3·H（即 2/3 液体积）
            If o.TankHeight > 0 Then geometry.TankHeight = o.TankHeight

            geometry.LiquidLevel = If(o.LiquidLevel > 0, o.LiquidLevel, 2.0 / 3.0 * geometry.TankHeight)

            If geometry.ImpellerHeight > 0.9 * geometry.TankHeight Then
                geometry.ImpellerHeight = geometry.TankHeight * 0.5
            End If

            Dim topts As New FermentationTankOptions With {
                .Resolution = o.Resolution,
                .CutCells = o.CutCells,
                .Transient = o.Transient,
                .Steps = o.Steps,
                .TargetCourant = Math.Max(0.1, o.Cfl),
                .Turbulence = o.TurbulenceKind(),
                .DriveStrengthRatio = o.DriveStrengthRatio,
                .UseGpu = o.UseGpu,
                .SnapshotInterval = o.Interval,
                .GeometryDirectory = If(o.GeometryDir, IO.Path.Combine(FullOut(o), "geometry"))}

            Dim tank As New EulerianFermentationTankCase(geometry, topts)

            Dim mesh As EulerianFvMesh = Nothing

            If Not String.IsNullOrEmpty(o.MeshZip) Then
                Console.WriteLine($"加载网格：{o.MeshZip}")
                mesh = EulerianFvMesh.FromFile(o.MeshZip)
            Else
                Console.WriteLine("生成几何（STL）与内流 Eulerian 网格 ……")
                Console.WriteLine($"  {geometry.ToString()}")

                Dim files = tank.WriteGeometry(topts.GeometryDirectory)
                Dim rules As New List(Of PatchRule)

                For Each region As EulerianRegionBox In tank.PatchRegions()
                    rules.Add(PatchRule.Region(region.Name, EulerianPatchType.Wall,
                                               region.MinX, region.MinY, region.MinZ,
                                               region.MaxX, region.MaxY, region.MaxZ))
                Next

                Dim report As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
                    files.container, o.MeshOutZip, EulerianPipeline.MeshType.Octree,
                    resolution:=o.Resolution,
                    cutCells:=o.CutCells,
                    poissonIterations:=0,
                    smoothLaplacian:=False,
                    rules:=rules,
                    mode:=DomainMode.Internal,
                    internalSolids:=files.secondary)

                mesh = New EulerianFvMesh(report.Mesh)

                Console.WriteLine($"  网格生成：{report.ToString()}")
            End If

            Call EnableGpu(o)
            Call tank.AttachMesh(mesh)
            Console.WriteLine(mesh.Diagnostics.TrimEnd())

            Call tank.Configure(mesh)

            Console.WriteLine($"转速：{geometry.Rpm:G6} rpm（ω={geometry.Omega:G6} rad/s），" &
                              $"液面：{geometry.LiquidLevel:G6} m（{(geometry.LiquidLevel / geometry.TankHeight * 100.0):F1}%）")

            Dim recorders As List(Of IEulerianSnapshotRecorder) = BuildRecorders(o, mesh)

            Dim probeCells As New List(Of Integer)

            For Each p As Double() In o.Probes
                probeCells.Add(EulerianDiagnostics.NearestCell(mesh, p(0), p(1), p(2)))
            Next

            Call tank.Run(o.Steps, Sub(stepIndex, residual)
                                       Call CaptureAll(recorders, mesh, tank.Fields, stepIndex, tank.Solver.Time)

                                       If o.Verbose AndAlso probeCells.Count > 0 Then
                                           Dim sb As New StringBuilder()

                                           For i As Integer = 0 To probeCells.Count - 1
                                               Dim kv As Double = EulerianDiagnostics.Sample(tank.Fields.K, probeCells(i))

                                               sb.Append(" p" & i.ToString(CultureInfo.InvariantCulture) & "=" &
                                                         kv.ToString("E3", CultureInfo.InvariantCulture))
                                           Next

                                           Console.WriteLine($"  step {stepIndex}: k{sb.ToString()}")
                                       End If
                                   End Sub)

            Call FinishAll(recorders, tank.Fields)

            timer.Stop()

            Console.WriteLine()
            Console.WriteLine(tank.Report)

            If o.Verbose Then
                Console.WriteLine(EulerianDiagnostics.ResidualSummary(tank.Solver, 20))
            End If

            Dim pw = tank.Power()

            Call WriteSummary(o, "tank",
                              $"power={pw.power.ToString("G6", CultureInfo.InvariantCulture)} W; " &
                              $"Np={pw.np.ToString("F4", CultureInfo.InvariantCulture)}; " &
                              $"liquidFraction={tank.Vof.LiquidFraction().ToString("F6", CultureInfo.InvariantCulture)}",
                              Nothing)

            Return 0
        End Function

#End Region

#Region "快照装配"

        Private Function FullOut(o As CliOptions) As String
            Dim dir As String = If(String.IsNullOrEmpty(o.OutputDir), ".", o.OutputDir)

            Call Directory.CreateDirectory(dir)

            Return IO.Path.GetFullPath(dir)
        End Function

        Private Function BuildRecorders(o As CliOptions, mesh As EulerianFvMesh) As List(Of IEulerianSnapshotRecorder)
            Dim list As New List(Of IEulerianSnapshotRecorder)()
            Dim outDir As String = FullOut(o)

            If o.Format = OutputFormat.Vtu OrElse o.Format = OutputFormat.Both Then
                Dim dir As String = If(o.Format = OutputFormat.Both, IO.Path.Combine(outDir, "vtu"), outDir)

                Dim metadata As New EulerianSnapshotMetadata With {
                    .Solver = If(o.IsCompressible(), "Eulerian FVM compressible/" & o.FluxScheme, "Eulerian FVM SIMPLE/PIMPLE"),
                    .CaseName = IO.Path.GetFileName(o.MeshZip),
                    .Dt = o.DeltaT}

                metadata.Notes.Add("unstructured polyhedral mesh exported as VTK_TETRA decomposition")
                metadata.Notes.Add($"cells={mesh.CellCount}, faces={mesh.FaceCount}, patches={mesh.PatchNames()}")

                list.Add(New EulerianSnapshotRecorder(dir, Math.Max(1, o.Interval), "frame", "animation.pvd", metadata))
                Console.WriteLine($"快照（VTU）：{dir}")
            End If

            If o.Format = OutputFormat.Vti OrElse o.Format = OutputFormat.Both Then
                Dim dir As String = If(o.Format = OutputFormat.Both, IO.Path.Combine(outDir, "vti"), outDir)
                Dim resampler As New EulerianToCartesianResampler(mesh.Mesh)

                Call resampler.AutoConfigure()

                Dim rec As New EulerianVtiRecorder(resampler, dir, Math.Max(1, o.Interval))

                rec.AddField("velocity", 3, Function() As Double()
                                                Return _currentFields.U.Values
                                            End Function)
                rec.AddField("pressure", 1, Function() As Double()
                                                Return _currentFields.P.Values
                                            End Function)
                rec.AddField("speed", 1, Function() As Double()
                                               Return SpeedValues()
                                           End Function)

                _vtiRecorder = rec

                list.Add(rec)

                Console.WriteLine($"快照（VTI 重采样）：{dir}（{resampler.Nx}×{resampler.Ny}×{resampler.Nz}，" &
                                  $"间距 {resampler.SpacingX:G4} m）")
            End If

            Return list
        End Function

        Private _currentFields As EulerianFieldSet
        Private _vtiRecorder As EulerianVtiRecorder

        Private Function SpeedValues() As Double()
            Dim f As EulerianFieldSet = _currentFields

            If f Is Nothing Then Return Nothing

            Return f.U.Magnitude()
        End Function

        Private Sub CaptureAll(recorders As List(Of IEulerianSnapshotRecorder), mesh As EulerianFvMesh,
                               fields As EulerianFieldSet, stepIndex As Integer, time As Double)

            If recorders Is Nothing OrElse recorders.Count = 0 Then Return

            _currentFields = fields

            Dim vtuFields As List(Of VtiField) = BuildVtuFields(fields)

            For Each rec As IEulerianSnapshotRecorder In recorders
                Call rec.Capture(mesh.Mesh, vtuFields, stepIndex, time)
            Next
        End Sub

        Private Sub FinishAll(recorders As List(Of IEulerianSnapshotRecorder), fields As EulerianFieldSet)
            If recorders Is Nothing Then Return

            _currentFields = fields

            For Each rec As IEulerianSnapshotRecorder In recorders
                Call rec.Finish()
            Next
        End Sub

        ''' <summary>
        ''' 把场集合转换为 VTU 用的逐单元字段（**分量交错**：
        ''' idx = cell * Components + component，与 VTK CellData 的布局一致）。
        ''' </summary>
        Public Function BuildVtuFields(fields As EulerianFieldSet) As List(Of VtiField)
            Dim list As New List(Of VtiField)()

            If fields Is Nothing Then Return list

            Dim n As Integer = fields.Mesh.CellCount

            list.Add(Interleave(fields.U, n, "velocity"))
            list.Add(Interleave(fields.P, n, "pressure"))
            list.Add(Interleave(fields.Rho, n, "density"))
            list.Add(Interleave(fields.Nut, n, "nut"))
            list.Add(Interleave(fields.K, n, "k"))
            list.Add(Interleave(fields.Alpha, n, "alpha"))

            ' speed（派生）
            Dim speed As Double() = fields.U.Magnitude()
            Dim sf(n - 1) As Single

            For i As Integer = 0 To n - 1
                sf(i) = CSng(speed(i))
            Next

            list.Add(New VtiField With {.Name = "speed", .Type = VtiFieldType.Float32, .Components = 1, .Data32 = sf})

            Return list
        End Function

        Private Function Interleave(f As EulerianField, n As Integer, name As String) As VtiField
            Dim comps As Integer = Math.Max(1, f.Components)
            Dim out(comps * n - 1) As Single
            Dim src As Double() = f.Values

            For c As Integer = 0 To n - 1
                For k As Integer = 0 To comps - 1
                    out(c * comps + k) = CSng(src(k * n + c))
                Next
            Next

            Return New VtiField With {.Name = name, .Type = VtiFieldType.Float32, .Components = comps, .Data32 = out}
        End Function

#End Region

#Region "报告"

        Private Sub WriteSummary(o As CliOptions, caseKind As String, detail As String, testCase As EulerianWindTunnelCase)
            Dim outDir As String = FullOut(o)
            Dim sb As New StringBuilder(1024)

            sb.AppendLine("{")
            sb.AppendLine($"  ""case"": ""{caseKind}"",")
            sb.AppendLine($"  ""mesh"": ""{Escape(o.MeshZip)}"",")
            sb.AppendLine($"  ""physics"": ""{Escape(o.Physics)}"",")
            sb.AppendLine($"  ""turbulence"": ""{Escape(o.Turbulence)}"",")
            sb.AppendLine($"  ""scheme"": ""{Escape(o.Scheme)}"",")
            sb.AppendLine($"  ""steps"": {o.Steps},")
            sb.AppendLine($"  ""gpu"": {(If(o.UseGpu, "true", "false"))},")
            sb.AppendLine($"  ""result"": ""{Escape(detail)}""")

            If testCase IsNot Nothing AndAlso testCase.LastForces IsNot Nothing Then
                sb.AppendLine($"  ,""Cd"": {testCase.LastForces.Cd.ToString("R", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  ,""Cl"": {testCase.LastForces.Cl.ToString("R", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  ,""Cm"": {testCase.LastForces.Cm.ToString("R", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  ,""drag"": {testCase.LastForces.ForceX.ToString("R", CultureInfo.InvariantCulture)}")
            End If

            sb.AppendLine("}")

            Dim path As String = IO.Path.Combine(outDir, "summary.json")

            IO.File.WriteAllText(path, sb.ToString(), New UTF8Encoding(False))

            Console.WriteLine($"报告已写出：{path}")
        End Sub

        Private Function Escape(s As String) As String
            If s Is Nothing Then Return ""

            Return s.Replace("\", "\\").Replace("""", "\""")
        End Function

        Public Function AutoTurbulenceLength(mesh As EulerianFvMesh) As Double
            Dim bmin As EulerianVertex = Nothing
            Dim bmax As EulerianVertex = Nothing

            Call mesh.GetBounds(bmin, bmax)

            Dim d As Double = (bmax - bmin).Length

            Return Math.Max(d * 0.05, 1.0E-4)
        End Function

        Public Function AutoReferenceArea(mesh As EulerianFvMesh) As Double
            Return Math.Max(mesh.TotalVolume, 1.0E-12)
        End Function

        Public Function AutoReferenceLength(mesh As EulerianFvMesh) As Double
            Dim bmin As EulerianVertex = Nothing
            Dim bmax As EulerianVertex = Nothing

            Call mesh.GetBounds(bmin, bmax)

            Dim s As EulerianVertex = bmax - bmin

            Return Math.Max(Math.Max(s.X, Math.Max(s.Y, s.Z)), 1.0E-12)
        End Function

#End Region

    End Module

End Namespace
