' /********************************************************************************/
'
'   StreamlineDiag —— WindTunnelBox「流线图」空白问题诊断程序
'
'   背景：
'       在 src\WindTunnelBox\FormWindTunnel.vb 中加载
'       src\ZMesh\CityBlender\samples\city_seed42.eulerian.zip，
'       跑完 FVM 模拟后选择【流线图】，画布一片空白。
'       怀疑：WindTunnelCanvas 默认 HideStraightStreamlines = True，
'             用 StreamlineSet.MinCurvature = 0.05（Straightness >= 0.05 才显示）
'             把"直线形态"的流线全部过滤掉了。
'
'   本程序复刻 WindTunnelBox 的**完整数据链路**（无 GUI）：
'
'       [A] 网格层      city_seed42.stl → EulerianPipeline → *.eulerian.zip
'       [B] 求解层      EulerianWindTunnelCase（与 FormWindTunnel.BuildOptions 同参数）
'                       + EulerianFvmCompute（CUDA float32 可选）
'       [C] 重采样层    EulerianFieldSampler（逐字复刻 Simulation\EulerianFieldSnapshot.vb）
'                       —— 关键：最近单元中心（零阶，分片常数）
'       [D] 流线层      StreamlineTracer.Trace（src\CDFDxCanvas\Rendering\Streamlines.vb 真身）
'                       + Straightness / MinCurvature 过滤 / BuildLines / StreamlineAnimator
'       [E] 交叉验证    WindTunnel.vbproj 的 CLI 路径（EulerianToCartesianResampler.AutoConfigure
'                       → *.vti）对比同一条链路产出的流线形态
'
'   用法：
'       StreamlineDiag mesh      <eulerian.zip>                     仅网格诊断
'       StreamlineDiag convert   <model.stl> <out.zip> [resolution] STL → Eulerian 网格
'       StreamlineDiag solve     <eulerian.zip> [steps]            求解 + 全套流线诊断
'       StreamlineDiag all       <model.stl> <out.zip> [steps]     转换 + 求解 + 诊断
'
'   日志：stdout 同时以 UTF-8 写入 <本程序目录>\logs\ 下的时间戳日志文件。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports Moira.CFDEngine.Eulerian
Imports Moira.CFDEngine.Snapshot
Imports Moira.FVMEngine.Eulerian
Imports Moira.ZMesh
Imports Moira.CDFDxCanvas.Rendering
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Math = System.Math

Module Program

    Private ReadOnly CI As CultureInfo = CultureInfo.InvariantCulture
    Private _log As StreamWriter
    Private _started As Stopwatch
    Private _gpuReady As Boolean

    ' ============================================================================
    '  入口
    ' ============================================================================

    Function Main(args As String()) As Integer
        _started = Stopwatch.StartNew()

        Dim logDir As String = IO.Path.Combine(AppContext.BaseDirectory, "..\..\..\logs")
        logDir = IO.Path.GetFullPath(logDir)
        Call Directory.CreateDirectory(logDir)

        Dim logPath As String = IO.Path.Combine(logDir, $"streamline_diag_{Date.Now:yyyyMMdd_HHmmss}.log")
        _log = New StreamWriter(logPath, False, New UTF8Encoding(False)) With {.AutoFlush = True}

        Dim code As Integer = 2

        Try
            If args.Length = 0 Then
                Call Usage()
                Return 2
            End If

            Select Case args(0).ToLowerInvariant()
                Case "mesh"
                    If args.Length < 2 Then Throw New ArgumentException("mesh 需要 <eulerian.zip>")
                    code = RunMeshOnly(args(1))

                Case "convert"
                    If args.Length < 3 Then Throw New ArgumentException("convert 需要 <model.stl> <out.zip> [resolution]")
                    Dim res As Double = If(args.Length > 3, Double.Parse(args(3), CI), 0.0)
                    code = RunConvert(args(1), args(2), res)

                Case "solve"
                    If args.Length < 2 Then Throw New ArgumentException("solve 需要 <eulerian.zip> [steps]")
                    Dim steps As Integer = If(args.Length > 2, Integer.Parse(args(2), CI), 200)
                    code = RunSolve(args(1), steps)

                Case "all"
                    If args.Length < 3 Then Throw New ArgumentException("all 需要 <model.stl> <out.zip> [steps]")
                    Dim steps As Integer = If(args.Length > 3, Integer.Parse(args(3), CI), 200)
                    code = RunConvert(args(1), args(2), 0.0)

                    If code = 0 Then
                        code = RunSolve(args(2), steps)
                    End If

                Case Else
                    Call Usage()
                    code = 2
            End Select

        Catch ex As Exception
            Call Line($"")
            Call Line($"[FATAL] {ex.GetType().Name}: {ex.Message}")
            Call Line(ex.ToString())
            code = 1

        Finally
            Call Line($"")
            Call Line($"=== 用时 {_started.Elapsed.TotalSeconds:F1} s，日志：{logPath} ===")

            If _log IsNot Nothing Then
                _log.Flush()
                _log.Dispose()
            End If
        End Try

        Return code
    End Function

    Private Sub Usage()
        Call Line("StreamlineDiag —— WindTunnelBox 流线图空白问题诊断")
        Call Line("")
        Call Line("  StreamlineDiag mesh    <eulerian.zip>                       仅网格诊断")
        Call Line("  StreamlineDiag convert <model.stl> <out.zip> [resolution]   STL → Eulerian 网格")
        Call Line("  StreamlineDiag solve   <eulerian.zip> [steps]               求解 + 流线诊断")
        Call Line("  StreamlineDiag all     <model.stl> <out.zip> [steps]        转换 + 求解 + 诊断")
    End Sub

    ''' <summary>同时写控制台与日志文件。</summary>
    Private Sub Line(text As String)
        Console.WriteLine(text)

        If _log IsNot Nothing Then
            _log.WriteLine(text)
        End If
    End Sub

    Private Sub Section(title As String)
        Call Line("")
        Call Line(New String("="c, 100))
        Call Line($"  {title}")
        Call Line(New String("="c, 100))
    End Sub

    Private Sub Sub_(title As String)
        Call Line("")
        Call Line($"--- {title} " & New String("-"c, Math.Max(0, 90 - title.Length)))
    End Sub

    ' ============================================================================
    '  [A] STL → Eulerian 网格
    ' ============================================================================

    Private Function RunConvert(modelPath As String, outZip As String, resolution As Double) As Integer
        Call Section($"[A] STL → Eulerian 网格：{IO.Path.GetFileName(modelPath)}")

        Call Line($"model      : {modelPath}")
        Call Line($"output zip : {outZip}")
        Call Line($"resolution : {If(resolution > 0, resolution.ToString("G4", CI), "(默认 0.05)")}")

        If Not File.Exists(modelPath) Then
            Call Line($"[FAIL] 找不到模型文件：{modelPath}")
            Return 1
        End If

        Call EnableGpu()

        Dim modelExtentAsDiagonal As Double = ModelDiagonal(modelPath)
        Call Line($"模型包围盒对角线 = {modelExtentAsDiagonal.ToString("G4", CI)} m")

        Dim res As Double = If(resolution > 0, resolution, 0.05)
        Call Line($"EulerianPipeline 目标网格尺度 = 对角线 × resolution = " &
                  $"{(modelExtentAsDiagonal * res).ToString("G4", CI)} m")

        Dim sw As Stopwatch = Stopwatch.StartNew()

        Try
            Dim report As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
                modelPath, outZip, EulerianPipeline.MeshType.Octree,
                resolution:=res,
                domainScale:=2.0,
                cutCells:=False,
                smoothLaplacian:=False)

            sw.Stop()

            Call Line($"[OK] {report.ToString()}")
            Call Line($"     网格算法 = {report.MeshKind}，用时 {sw.Elapsed.TotalSeconds:F1} s")

            Dim info As EulerianMeshInfo = EulerianMeshPackage.ReadMetadata(outZip)

            Call Line($"     包元数据：points={info.PointCount}, faces={info.FaceCount}, cells={info.CellCount}, " &
                      $"boundaryFaces={info.BoundaryFaceCount}")
            Call Line($"     bounds min = [{info.BoundsMin(0):G6}, {info.BoundsMin(1):G6}, {info.BoundsMin(2):G6}]")
            Call Line($"     bounds max = [{info.BoundsMax(0):G6}, {info.BoundsMax(1):G6}, {info.BoundsMax(2):G6}]")
            Call Line($"     totalVolume = {info.TotalVolume:G6}")
            Call Line($"     generator = {info.Generator}")

            Dim extentX As Double = info.BoundsMax(0) - info.BoundsMin(0)
            Dim extentY As Double = info.BoundsMax(1) - info.BoundsMin(1)
            Dim extentZ As Double = info.BoundsMax(2) - info.BoundsMin(2)
            Dim cellSize As Double = Math.Pow(Math.Max(info.TotalVolume, 1.0E-300) / Math.Max(1, info.CellCount), 1.0 / 3.0)

            Call Line($"     计算域 = {extentX:G6} × {extentY:G6} × {extentZ:G6} m")
            Call Line($"     等效立方单元边长 ≈ {cellSize:G4} m  →  " &
                      $"约 {extentX / cellSize:F1} × {extentY / cellSize:F1} × {extentZ / cellSize:F1} 单元")

            For Each p As EulerianPatchInfo In info.Patches
                Call Line($"       patch {p.Name,-14} {p.Type,-10} faces={p.FaceCount}")
            Next

            Return 0

        Catch ex As Exception
            sw.Stop()
            Call Line($"[FAIL] 网格转换异常：{ex.GetType().Name}: {ex.Message}")
            Call Line(ex.ToString())
            Return 1
        End Try
    End Function

    ''' <summary>用 STL 的边界框对角线长度估算 modelExtent（与 EulerianPipeline 的语义一致）。</summary>
    Private Function ModelDiagonal(path As String) As Double
        Try
            Dim text As String = Nothing

            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim buf(255) As Byte
                Dim n As Integer = fs.Read(buf, 0, 256)
                text = Encoding.ASCII.GetString(buf, 0, n)
            End Using

            Dim isAscii As Boolean = text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase) AndAlso
                                     text.IndexOf("facet", StringComparison.OrdinalIgnoreCase) >= 0

            Dim minX As Double = Double.MaxValue, minY As Double = Double.MaxValue, minZ As Double = Double.MaxValue
            Dim maxX As Double = Double.MinValue, maxY As Double = Double.MinValue, maxZ As Double = Double.MinValue
            Dim cnt As Integer = 0

            If isAscii Then
                For Each raw As String In File.ReadLines(path)
                    Dim ln As String = raw.Trim()

                    If Not ln.StartsWith("vertex", StringComparison.OrdinalIgnoreCase) Then Continue For

                    Dim parts As String() = ln.Split(New Char() {" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)

                    If parts.Length < 4 Then Continue For

                    Dim x As Double, y As Double, z As Double

                    If Not Double.TryParse(parts(1), NumberStyles.Float, CI, x) Then Continue For
                    If Not Double.TryParse(parts(2), NumberStyles.Float, CI, y) Then Continue For
                    If Not Double.TryParse(parts(3), NumberStyles.Float, CI, z) Then Continue For

                    If x < minX Then minX = x
                    If y < minY Then minY = y
                    If z < minZ Then minZ = z
                    If x > maxX Then maxX = x
                    If y > maxY Then maxY = y
                    If z > maxZ Then maxZ = z
                    cnt += 1
                Next
            Else
                Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Using br As New BinaryReader(fs)
                        Call br.ReadBytes(80)
                        Dim tri As UInteger = br.ReadUInt32()

                        For t As ULong = 0 To tri - 1UL
                            ' 法向
                            Call br.ReadSingle() : Call br.ReadSingle() : Call br.ReadSingle()

                            For v As Integer = 0 To 2
                                Dim x As Double = br.ReadSingle()
                                Dim y As Double = br.ReadSingle()
                                Dim z As Double = br.ReadSingle()

                                If x < minX Then minX = x
                                If y < minY Then minY = y
                                If z < minZ Then minZ = z
                                If x > maxX Then maxX = x
                                If y > maxY Then maxY = y
                                If z > maxZ Then maxZ = z
                                cnt += 1
                            Next

                            Call br.ReadUInt16()
                        Next
                    End Using
                End Using
            End If

            If cnt = 0 OrElse maxX < minX Then
                Return 1.0
            End If

            Dim dx As Double = maxX - minX, dy As Double = maxY - minY, dz As Double = maxZ - minZ

            Call Line($"  [STL 包围盒] min=({minX:G6}, {minY:G6}, {minZ:G6})  max=({maxX:G6}, {maxY:G6}, {maxZ:G6})  vertices={cnt}")

            Return Math.Sqrt(dx * dx + dy * dy + dz * dz)

        Catch ex As Exception
            Call Line($"  [WARN] 模型包围盒解析失败（{ex.Message}），退回 1.0")
            Return 1.0
        End Try
    End Function

    ' ============================================================================
    '  GPU
    ' ============================================================================

    Private Sub EnableGpu()
        Call Section("[GPU] CUDA float32 后端注册（FVM 求解加速）")

        Try
            _gpuReady = EulerianFvmCompute.TryRegister()

            If _gpuReady Then
                Call Line($"[OK] 已启用：{EulerianFvmCompute.Current.DescribeDevice()}")
            Else
                Call Line($"[WARN] 不可用，回落 CPU double 路径：{EulerianFvmCompute.LastError}")
            End If
        Catch ex As Exception
            _gpuReady = False
            Call Line($"[WARN] GPU 注册异常，回落 CPU：{ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ' ============================================================================
    '  [B] 网格诊断（仅加载既有 zip）
    ' ============================================================================

    Private Function RunMeshOnly(zipPath As String) As Integer
        Call Section($"[B] Eulerian 网格诊断：{IO.Path.GetFileName(zipPath)}")

        If Not File.Exists(zipPath) Then
            Call Line($"[FAIL] 找不到网格包：{zipPath}")
            Return 1
        End If

        Dim info As EulerianMeshInfo = EulerianMeshPackage.ReadMetadata(zipPath)

        Call Line($"points={info.PointCount}, faces={info.FaceCount}, cells={info.CellCount}, " &
                  $"boundaryFaces={info.BoundaryFaceCount}, generator={info.Generator}")

        Dim extX As Double = info.BoundsMax(0) - info.BoundsMin(0)
        Dim extY As Double = info.BoundsMax(1) - info.BoundsMin(1)
        Dim extZ As Double = info.BoundsMax(2) - info.BoundsMin(2)
        Dim cellSize As Double = Math.Pow(Math.Max(info.TotalVolume, 1.0E-300) / Math.Max(1, info.CellCount), 1.0 / 3.0)

        Call Line($"bounds min = [{info.BoundsMin(0):G6}, {info.BoundsMin(1):G6}, {info.BoundsMin(2):G6}]")
        Call Line($"bounds max = [{info.BoundsMax(0):G6}, {info.BoundsMax(1):G6}, {info.BoundsMax(2):G6}]")
        Call Line($"domain     = {extX:G6} × {extY:G6} × {extZ:G6} m")
        Call Line($"avg cell   ≈ {cellSize:G4} m  →  约 {extX / cellSize:F1} × {extY / cellSize:F1} × {extZ / cellSize:F1} 单元")

        For Each p As EulerianPatchInfo In info.Patches
            Call Line($"  patch {p.Name,-14} {p.Type,-10} faces={p.FaceCount}")
        Next

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)

        Sub_("FVM 网格视图")
        Call Line(mesh.Diagnostics.TrimEnd())

        ' 单元尺度分布（八叉树网格的关键：单元尺寸是否均匀）
        Dim vol As Double() = mesh.CellVolumeArray()
        Dim n As Integer = vol.Length
        Dim sorted(n - 1) As Double
        Array.Copy(vol, sorted, n)
        Array.Sort(sorted)

        Call Line($"  单元体积分位（等效边长 = V^(1/3)）：")
        Call Line($"    min    = {Cbrt(sorted(0)):F3} m")
        Call Line($"    p05    = {Cbrt(sorted(CInt(0.05 * (n - 1)))):F3} m")
        Call Line($"    p50    = {Cbrt(sorted(n \ 2)):F3} m")
        Call Line($"    p95    = {Cbrt(sorted(CInt(0.95 * (n - 1)))):F3} m")
        Call Line($"    max    = {Cbrt(sorted(n - 1)):F3} m")

        Return 0
    End Function

    Private Function Cbrt(v As Double) As Double
        If v <= 0 Then Return 0
        Return Math.Exp(Math.Log(v) / 3.0)
    End Function

    ' ============================================================================
    '  [C] 求解 + 流线诊断
    ' ============================================================================

    Private Function RunSolve(zipPath As String, steps As Integer) As Integer
        Call Section($"[C] FVM 求解 + 流线诊断：{IO.Path.GetFileName(zipPath)}（{steps} 步）")

        If Not File.Exists(zipPath) Then
            Call Line($"[FAIL] 找不到网格包：{zipPath}")
            Return 1
        End If

        Call EnableGpu()

        ' ------------------------------------------------------------------
        ' 1. 加载网格（与 WindTunnelSimulationRunner.LoadMesh 完全一致）
        ' ------------------------------------------------------------------
        Sub_("1. 加载网格（WindTunnelSimulationRunner.LoadMesh 路径）")

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Call Line(mesh.Diagnostics.TrimEnd())

        ' ------------------------------------------------------------------
        ' 2. 重采样器（逐字复刻 Simulation\EulerianFieldSnapshot.vb）
        ' ------------------------------------------------------------------
        Sub_("2. 结构化重采样器（EulerianFieldSampler 复刻）")

        Dim sampler As New BoxSampler(mesh, 250000)

        Call Line($"重采样结构格 = {sampler.Nx} × {sampler.Ny} × {sampler.Nz}  " &
                  $"（{sampler.Nx * sampler.Ny * sampler.Nz} 节点，h={sampler.Spacing:G6} m）")
        Call Line($"结构格原点   = ({sampler.OriginX:G6}, {sampler.OriginY:G6}, {sampler.OriginZ:G6})")
        Call Line($"域外掩膜     = {sampler.OutsideCount} / {sampler.Nx * sampler.Ny * sampler.Nz} " &
                  $"({100.0 * sampler.OutsideCount / (sampler.Nx * sampler.Ny * sampler.Nz):F2}% 判为固体/域外)")

        ' 结构格 ↔ 单元的映射块状程度（本问题的核心指标）
        Call Line("")
        Call Line("  【映射块状度】相邻结构格节点是否落在同一个网格单元里：")

        Dim sameX As Double = sampler.SameCellFractionX()
        Dim sameY As Double = sampler.SameCellFractionY()
        Dim sameZ As Double = sampler.SameCellFractionZ()

        Call Line($"    +x 方向相邻节点同单元比例 = {100.0 * sameX:F2}%")
        Call Line($"    +y 方向相邻节点同单元比例 = {100.0 * sameY:F2}%")
        Call Line($"    +z 方向相邻节点同单元比例 = {100.0 * sameZ:F2}%")
        Call Line($"    → 平均 {100.0 * (sameX + sameY + sameZ) / 3.0:F2}% 的相邻节点速度完全相同")

        ' 结构格节点间隔 / 单元等效边长
        Call Line($"    结构格间距 h = {sampler.Spacing:F4} m，" &
                  $"网格 p50 单元等效边长 = {Cbrt(Median(mesh.CellVolumeArray())):F4} m " &
                  $"→ 每个单元平均覆盖 {(Cbrt(Median(mesh.CellVolumeArray())) / sampler.Spacing) ^ 3:F2} 个结构格节点")

        ' ------------------------------------------------------------------
        ' 3. 算例配置（与 FormWindTunnel.BuildOptions 的 UI 默认值一致）
        ' ------------------------------------------------------------------
        Sub_("3. 算例配置（FormWindTunnel.BuildOptions 默认值）")

        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = 30.0R,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.225R,
            .KinematicViscosity = 0.000015R,
            .Turbulence = WindTunnelTurbulence.KEpsilon,
            .TurbulenceIntensity = 0.02R,
            .Compressible = False,
            .UseGpu = _gpuReady,
            .ReferenceArea = 1.0R,
            .PressureMaxIterations = 200}

        Call Line($"InletSpeed      = {opts.InletSpeed} m/s (+x)")
        Call Line($"Density / Nu    = {opts.Density} kg/m³ / {opts.KinematicViscosity:G4} m²/s")
        Call Line($"Turbulence      = {opts.Turbulence}，Ti = {opts.TurbulenceIntensity}")
        Call Line($"UseGpu          = {opts.UseGpu}")
        Call Line($"TurbLengthScale = {opts.TurbulenceLengthScale}（GUI 未设置 → 用类默认值）")

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim testCase As New EulerianWindTunnelCase(mesh, opts)

        Call testCase.Configure()
        Call Line($"Configure 完成，用时 {sw.Elapsed.TotalSeconds:F2} s")

        Call Line($"  配置后：U NaN={NanCount(testCase.Fields.U.Values)}, p NaN={NanCount(testCase.Fields.P.Values)}")

        ' ------------------------------------------------------------------
        ' 4. 推进
        ' ------------------------------------------------------------------
        Sub_($"4. 推进 {steps} 步")

        ' 缓存若干步的快照，用于观察流线形态随收敛的演化
        Dim snapSteps As New List(Of Integer) From {1, Math.Max(1, steps \ 4), Math.Max(1, steps \ 2), steps}
        Dim snapshots As New List(Of Tuple(Of Integer, BoxSnapshot))

        For Each s As Integer In snapSteps.Distinct().OrderBy(Function(x) x)
            Dim need As Integer = s - testCase.StepCount

            If need > 0 Then
                Dim t As Stopwatch = Stopwatch.StartNew()
                Call testCase.Run(need)
                t.Stop()

                Call Line($"  step {testCase.StepCount,5}：{t.Elapsed.TotalSeconds:F2} s " &
                          $"({t.Elapsed.TotalMilliseconds / need:F1} ms/step)，" &
                          $"res={LastResidual(testCase):E3}，cont={Continuity(testCase):E3}，" &
                          $"Cd={If(testCase.LastForces IsNot Nothing, testCase.LastForces.Cd, 0):G5}")
            End If

            snapshots.Add(Tuple.Create(testCase.StepCount, sampler.Sample(testCase)))
        Next

        Call Line("")
        Call Line(testCase.Report.TrimEnd())

        Dim final As BoxSnapshot = snapshots(snapshots.Count - 1).Item2

        ' ------------------------------------------------------------------
        ' 5. 重采样速度场质量
        ' ------------------------------------------------------------------
        Sub_("5. 重采样速度场质量（决定流线是否可能弯曲）")

        Call Line($"  快照：{final.Nx}×{final.Ny}×{final.Nz}，MaxSpeed={final.MaxSpeed:F4} m/s")

        Dim stats As FieldStats = FieldStats.Compute(final)

        Call Line($"  流体节点数        = {stats.Active}")
        Call Line($"  |u|  min/mean/max = {stats.SpeedMin:F4} / {stats.SpeedMean:F4} / {stats.SpeedMax:F4} m/s")
        Call Line($"  Ux   min/mean/max = {stats.UxMin:F4} / {stats.UxMean:F4} / {stats.UxMax:F4} m/s")
        Call Line($"  Uy   min/mean/max = {stats.UyMin:F4} / {stats.UyMean:F4} / {stats.UyMax:F4} m/s")
        Call Line($"  Uz   min/mean/max = {stats.UzMin:F4} / {stats.UzMean:F4} / {stats.UzMax:F4} m/s")
        Call Line("")
        Call Line($"  【零阶重采样证据】相邻结构格节点的速度差（分片常数 ⇒ 多数为 0）：")
        Call Line($"    |ΔUx| 沿 +x: 平均 {stats.dUxX:F4}，为 0 的比例 {100.0 * stats.ZeroUxX:F2}%")
        Call Line($"    |ΔUy| 沿 +x: 平均 {stats.dUyX:F4}，为 0 的比例 {100.0 * stats.ZeroUyX:F2}%")
        Call Line($"    |ΔUz| 沿 +x: 平均 {stats.dUzX:F4}，为 0 的比例 {100.0 * stats.ZeroUzX:F2}%")
        Call Line($"    |ΔUx| 沿 +y: 为 0 的比例 {100.0 * stats.ZeroUxY:F2}%")
        Call Line($"    |ΔUx| 沿 +z: 为 0 的比例 {100.0 * stats.ZeroUxZ:F2}%")
        Call Line("")
        Call Line($"  流向速度亏损（|Ux| < 0.9·U∞ 的流体节点）= {stats.SlowCount} " &
                  $"（{100.0 * stats.SlowCount / Math.Max(1, stats.Active):F2}%）")
        Call Line($"  横向速度显著（|Uy| 或 |Uz| > 1 m/s）的流体节点 = {stats.CrossCount} " &
                  $"（{100.0 * stats.CrossCount / Math.Max(1, stats.Active):F2}%）")

        ' ------------------------------------------------------------------
        ' 6. 流线诊断（WindTunnelBox 实际使用的路径）
        ' ------------------------------------------------------------------
        Sub_("6. 流线追踪（WindTunnelCanvas.RefreshField 路径 + StreamlineTracer 真身）")

        Dim allReports As New List(Of TraceReport)

        For Each tup As Tuple(Of Integer, BoxSnapshot) In snapshots
            Dim rep As TraceReport = TraceAndAnalyze($"step={tup.Item1} 零阶（当前实现）", tup.Item2, 4, 0.7F, 0.0F)
            allReports.Add(rep)
        Next

        Dim finalReport As TraceReport = allReports(allReports.Count - 1)

        ' ------------------------------------------------------------------
        ' 7. 对照实验
        ' ------------------------------------------------------------------
        Sub_("7. 对照实验：把『零阶分片常数』换成『节点插值 + 平滑』后流线是否还是直线")

        Dim smooth3 As BoxSnapshot = final.Smoothed(3)
        Dim smooth9 As BoxSnapshot = final.Smoothed(9)
        Dim smooth20 As BoxSnapshot = final.Smoothed(20)

        Call TraceAndAnalyze("step=final 平滑3轮", smooth3, 4, 0.7F, 0.0F)
        Call TraceAndAnalyze("step=final 平滑9轮", smooth9, 4, 0.7F, 0.0F)
        Call TraceAndAnalyze("step=final 平滑20轮", smooth20, 4, 0.7F, 0.0F)

        Call Line("")
        Call Line("  【步长敏感性】同一条零阶场，仅把 RK2 弧长步长从 0.7 缩小到 0.2：")

        Call TraceAndAnalyze("step=final 零阶 h=0.2", final, 4, 0.2F, 0.0F)

        Call Line("")
        Call Line("  【阈值敏感性】同一条零阶场，种子步长 8（种子更稀）：")

        Call TraceAndAnalyze("step=final 零阶 seedStride=8", final, 8, 0.7F, 0.0F)

        ' ------------------------------------------------------------------
        ' 8. WindTunnel CLI 路径交叉验证
        ' ------------------------------------------------------------------
        Sub_("8. 交叉验证：WindTunnel.vbproj CLI 路径（AutoConfigure 重采样 + .vti）")

        Call CrossCheckCli(zipPath, mesh, testCase)

        ' ------------------------------------------------------------------
        ' 9. 结论
        ' ------------------------------------------------------------------
        Sub_("9. 结论")

        Dim straightHidden As Integer = 0
        Dim straightTotal As Integer = 0

        For Each pl As StreamlinePolyline In finalReport.StreamSet.Polylines
            straightTotal += 1

            If pl.Straightness < StreamlineSet.DefaultStraightness Then
                straightHidden += 1
            End If
        Next

        Call Line($"  WindTunnelBox 最终帧：种子 {finalReport.StreamSet.SeedCount} 个 → 有效流线 {straightTotal} 条，" &
                  $"总线段 {finalReport.StreamSet.SegmentCount} 段")
        Call Line($"  Straightness 分布：min={finalReport.MinStraightness:G6}, " &
                  $"median={finalReport.MedianStraightness:G6}, mean={finalReport.MeanStraightness:G6}, " &
                  $"max={finalReport.MaxStraightness:G6}")
        Call Line($"  低于 StreamlineSet.DefaultStraightness ({StreamlineSet.DefaultStraightness}) 的流线：" &
                  $"{straightHidden} / {straightTotal}")
        Call Line($"  HideStraightStreamlines=True 时 BuildLines 输出线段数 = {finalReport.KeptSegments} " &
                  $"，光点粒子数 = {finalReport.ParticleCount}")
        Call Line($"")

        If finalReport.KeptSegments = 0 Then
            Call Line("  => 启用直线过滤后【画布上一条流线都没有】，与用户观察到的『流线图一片空白』一致。")
        Else
            Call Line("  => 启用直线过滤后仍有可见线段。")
        End If

        Call Line($"")
        Call Line("  请对照第 6 / 7 节：若零阶路径的 Straightness 全部 < 0.05，")
        Call Line("  而平滑路径出现明显 > 0.05 的流线，则根因是")
        Call Line("  【EulerianFieldSampler 的最近单元零阶重采样使速度场分片常数】。")
        Return 0
    End Function

    Private Function LastResidual(c As EulerianWindTunnelCase) As Double
        If c.Incompressible Is Nothing Then Return 0

        Dim h As List(Of Double) = c.Incompressible.ResidualHistory

        If h Is Nothing OrElse h.Count = 0 Then Return 0

        Return h(h.Count - 1)
    End Function

    Private Function Continuity(c As EulerianWindTunnelCase) As Double
        If c.Incompressible Is Nothing Then Return 0
        Return c.Incompressible.ContinuityError
    End Function

    Private Function NanCount(a As Double()) As Integer
        If a Is Nothing Then Return -1

        Dim n As Integer = 0

        For i As Integer = 0 To a.Length - 1
            If Double.IsNaN(a(i)) OrElse Double.IsInfinity(a(i)) Then n += 1
        Next

        Return n
    End Function

    Private Function Median(a As Double()) As Double
        Dim b(a.Length - 1) As Double
        Array.Copy(a, b, a.Length)
        Array.Sort(b)
        Return b(b.Length \ 2)
    End Function

    ' ============================================================================
    '  流线追踪 + 指标统计
    ' ============================================================================

    Private Class TraceReport

        Public Title As String
        Public StreamSet As StreamlineSet
        Public MinStraightness As Double
        Public MedianStraightness As Double
        Public MeanStraightness As Double
        Public MaxStraightness As Double
        Public KeptSegments As Integer
        Public ParticleCount As Integer
        Public ValidLines As Integer
        Public CurvedLines As Integer
        Public MaxDeviation As Double        ' 顶点的最大 |Δy|+|Δz| 偏离种子点
        Public MaxCurvedStraightness As Double
        Public BestLineDescription As String = ""
        Public AllStraight As Boolean

    End Class

    ''' <summary>追踪一遍流线并打印统计。</summary>
    Private Function TraceAndAnalyze(label As String, snap As BoxSnapshot,
                                     seedStride As Integer, stepLength As Single, minCurvature As Single) As TraceReport

        Dim rep As New TraceReport With {.Title = label}
        Dim field As New DiagFlowField(snap)

        Dim opts As New StreamlineOptions With {
            .SeedStride = Math.Max(1, seedStride),
            .StepLength = stepLength,
            .MaxSteps = 3000,
            .MinSpeed = 0.0005F,
            .MaxSegments = 400000}

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim set1 As StreamlineSet = Nothing

        Try
            set1 = StreamlineTracer.Trace(field, opts)
        Catch ex As Exception
            Call Line($"  [{label}] 追踪异常：{ex.GetType().Name}: {ex.Message}")
            rep.StreamSet = New StreamlineSet With {.Polylines = New StreamlinePolyline() {}}
            Return rep
        End Try

        sw.Stop()
        rep.StreamSet = set1

        ' 直线度统计
        Dim vals As New List(Of Double)
        Dim maxDev As Double = 0
        Dim bestMax As Double = -1
        Dim bestDesc As String = ""

        For Each pl As StreamlinePolyline In set1.Polylines
            If pl Is Nothing OrElse pl.VertexCount < 2 Then Continue For

            vals.Add(pl.Straightness)

            If pl.Straightness > bestMax Then
                bestMax = pl.Straightness

                Dim dy As Double = pl.Ys(pl.VertexCount - 1) - pl.Ys(0)
                Dim dz As Double = pl.Zs(pl.VertexCount - 1) - pl.Zs(0)
                Dim dyy As Double = 0, dzz As Double = 0

                For s As Integer = 0 To pl.VertexCount - 1
                    dyy = Math.Max(dyy, Math.Abs(pl.Ys(s) - pl.Ys(0)))
                    dzz = Math.Max(dzz, Math.Abs(pl.Zs(s) - pl.Zs(0)))
                Next

                bestDesc = $"v={pl.VertexCount}, L={pl.TotalLength:G6}, " &
                           $"种子(i0,j,k) 位移 Δy={dy:G4}/max|Δy|={dyy:G4}, Δz={dz:G4}/max|Δz|={dzz:G4}"
            End If

            Dim dev As Double = 0

            For s As Integer = 0 To pl.VertexCount - 1
                dev = Math.Max(dev, Math.Abs(pl.Ys(s) - pl.Ys(0)) + Math.Abs(pl.Zs(s) - pl.Zs(0)))
            Next

            If dev > maxDev Then maxDev = dev
        Next

        vals.Sort()

        rep.ValidLines = vals.Count
        rep.MinStraightness = If(vals.Count > 0, vals(0), 0)
        rep.MedianStraightness = If(vals.Count > 0, vals(vals.Count \ 2), 0)
        rep.MeanStraightness = If(vals.Count > 0, vals.Average(), 0)
        rep.MaxStraightness = If(vals.Count > 0, vals(vals.Count - 1), 0)
        rep.MaxDeviation = maxDev
        rep.MaxCurvedStraightness = bestMax
        rep.BestLineDescription = bestDesc
        rep.AllStraight = vals.Count > 0 AndAlso vals(vals.Count - 1) < StreamlineSet.DefaultStraightness

        ' 过滤后的可见线段（WindTunnelCanvas 走的就是这条路）
        set1.MinCurvature = If(minCurvature > 0, minCurvature, StreamlineSet.DefaultStraightness)

        Dim lines As LineSegment() = set1.BuildLines(Nothing, 210, 0, 0, 0)

        rep.KeptSegments = lines.Length

        Dim anim As New StreamlineAnimator(set1, 2)
        rep.ParticleCount = anim.ParticleCount

        rep.CurvedLines = set1.CurvedPolylines().Count()

        ' 打印
        Call Line("")
        Call Line($"  [{label}]  追踪 {sw.Elapsed.TotalMilliseconds:F1} ms")
        Call Line($"      种子数={set1.SeedCount}，有效流线={rep.ValidLines}，总线={set1.SegmentCount}，" &
                  $"截断={set1.Truncated}")
        Call Line($"      Straightness: min={rep.MinStraightness:G6} median={rep.MedianStraightness:G6} " &
                  $"mean={rep.MeanStraightness:G6} max={rep.MaxStraightness:G6}  " &
                  $"(阈值 {StreamlineSet.DefaultStraightness})")
        Call Line($"      最大横向偏移 max(|Δy|+|Δz|) = {rep.MaxDeviation:G6} 格")
        Call Line($"      过滤后可见线段 = {rep.KeptSegments}，曲线流线 = {rep.CurvedLines}，" &
                  $"光点粒子 = {rep.ParticleCount}")
        Call Line($"      最弯的一条：straightness={rep.MaxCurvedStraightness:G6}  {rep.BestLineDescription}")

        Return rep
    End Function

    ' ============================================================================
    '  WindTunnel CLI 路径交叉验证
    ' ============================================================================

    Private Sub CrossCheckCli(zipPath As String, mesh As EulerianFvMesh, testCase As EulerianWindTunnelCase)
        ' CLI 路径用的是 EulerianToCartesianResampler.AutoConfigure（Origin = 包围盒最小角，
        ' 且 Resample() 走的是同一个最近单元零阶逻辑）。
        Dim rs As New EulerianToCartesianResampler(mesh.Mesh)
        Call rs.AutoConfigure()

        Call Line($"  CLI AutoConfigure: {rs.Nx}×{rs.Ny}×{rs.Nz}, h={rs.SpacingX:G6} m, " &
                  $"origin=({rs.OriginX:G6}, {rs.OriginY:G6}, {rs.OriginZ:G6})")

        Dim n As Integer = rs.Nx * rs.Ny * rs.Nz
        Dim u As Double() = testCase.Fields.U.Values

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim uRes As Single() = rs.Resample(u, 3)
        sw.Stop()

        Call Line($"  Resample(velocity) 用时 {sw.Elapsed.TotalSeconds:F2} s，输出 {uRes.Length} 个 float")

        ' 拆成三个分量数组（Resample 输出为 idx*3 + comp 交错）
        Dim ux(n - 1) As Single, uy(n - 1) As Single, uz(n - 1) As Single
        Dim outside(n - 1) As Byte
        Dim nonZero As Integer = 0
        Dim outsideCount As Integer = 0

        For i As Integer = 0 To rs.Nx - 1
            Dim x As Double = rs.OriginX + i * rs.SpacingX

            For j As Integer = 0 To rs.Ny - 1
                Dim y As Double = rs.OriginY + j * rs.SpacingY

                For k As Integer = 0 To rs.Nz - 1
                    Dim z As Double = rs.OriginZ + k * rs.SpacingZ
                    Dim idx As Integer = (i * rs.Ny + j) * rs.Nz + k
                    Dim c As Integer = rs.NearestCell(x, y, z)

                    If c < 0 Then
                        outside(idx) = 1
                        outsideCount += 1
                        Continue For
                    End If

                    ux(idx) = uRes(idx * 3 + 0)
                    uy(idx) = uRes(idx * 3 + 1)
                    uz(idx) = uRes(idx * 3 + 2)
                    nonZero += 1
                Next
            Next
        Next

        Call Line($"  映射成功 {nonZero} / {n} 节点，域外（未找到单元） {outsideCount} " &
                  $"（{100.0 * outsideCount / n:F2}%）")

        Dim snap As New BoxSnapshot(rs.Nx, rs.Ny, rs.Nz, ux, uy, uz, outside)

        Call TraceAndAnalyze("WindTunnel CLI（AutoConfigure 零阶）", snap, 4, 0.7F, 0.0F)

        ' CLI 的 .vti 输出是给离线查看器的；同样用零阶，结论一致。
        Call Line("")
        Call Line("  说明：CLI 的 --format vti 与 WindTunnelBox 的重采样都是『最近单元中心（零阶）』，")
        Call Line("        因此两条路径产出的流线形态属于同一类，可直接对照上面的数字。")
    End Sub

End Module
