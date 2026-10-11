' /********************************************************************************/
'
'   EulerianWindTunnelCase.vb
'
'   风洞算例：由既有 Eulerian 网格（zip）装配不可压 / 可压缩风洞求解，并做气动监控。
'
'   适用对象：F1 赛车、飞机、城市建筑群。
'
'   装配内容：
'       1. 按 patch 名/类型装配来流、出口、地面、顶面、对称面；
'       2. 湍流入口（k / ε / ω 由湍流强度 I 与长度尺度 L 自动推导）；
'       3. 气动力监控（Cd / Cl / Cm + 逐 patch 分力），每 N 步记录一次；
'       4. 收敛报告（阻力系数漂移 + 连续性残差）。
'
'   可压缩开关：
'       <see cref="EulerianWindTunnelOptions.Compressible"/> = True 时改用
'       AUSM+/HLLC 密度基显式求解器（可算 F1 车身局部跨声速）。
'       大部分区域 Ma &lt; 0.3 的工况建议用不可压（更快、更稳）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

    ''' <summary>风洞算例配置。</summary>
    Public Class EulerianWindTunnelOptions

        ''' <summary>来流速度（m/s）。</summary>
        Public Property InletSpeed As Double = 30.0

        ''' <summary>来流方向（单位化；默认 +X）。</summary>
        Public Property InletDirection As Moira.CFDEngine.Eulerian.EulerianVertex =
            New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)

        ''' <summary>空气密度（kg/m³）。</summary>
        Public Property Density As Double = 1.225

        ''' <summary>空气运动粘度（m²/s）。</summary>
        Public Property KinematicViscosity As Double = 1.5E-5

        ''' <summary>湍流模型。</summary>
        Public Property Turbulence As WindTunnelTurbulence = WindTunnelTurbulence.KEpsilon

        ''' <summary>入口湍流强度（0.01 = 1%）。</summary>
        Public Property TurbulenceIntensity As Double = 0.01

        ''' <summary>入口湍流长度尺度（m；一般取模型特征尺度的 5%~10%）。</summary>
        Public Property TurbulenceLengthScale As Double = 0.05

        ''' <summary>是否使用可压缩求解器。</summary>
        Public Property Compressible As Boolean = False

        ''' <summary>可压缩时的自由来流马赫数。</summary>
        Public Property Mach As Double = 0.1

        ''' <summary>可压缩时的总温（K）。</summary>
        Public Property Temperature As Double = 288.15

        ''' <summary>可压缩通量格式（"AUSM+" 或 "HLLC"）。</summary>
        Public Property FluxScheme As String = "AUSM+"

        ''' <summary>稳态迭代数 / 瞬态步数。</summary>
        Public Property Steps As Integer = 1500

        ''' <summary>是否瞬态推进。</summary>
        Public Property Transient As Boolean = False

        ''' <summary>瞬态时间步长（&lt;= 0 时按 CFL 自动）。</summary>
        Public Property DeltaT As Double = 0

        ''' <summary>可压缩 CFL 数。</summary>
        Public Property Cfl As Double = 0.8

        ''' <summary>对流格式。</summary>
        Public Property Scheme As EulerianConvectionScheme = EulerianConvectionScheme.Upwind

        ''' <summary>速度欠松弛。</summary>
        Public Property RelaxU As Double = 0.7

        ''' <summary>压力方程最大迭代数。</summary>
        Public Property PressureMaxIterations As Integer = 200

        ''' <summary>是否使用 GPU float32 后端。</summary>
        Public Property UseGpu As Boolean = True

        ''' <summary>力系数监控间隔（每多少步记录一次）。</summary>
        Public Property ForceInterval As Integer = 10

        ''' <summary>参考面积（m²）。</summary>
        Public Property ReferenceArea As Double = 1.0

        ''' <summary>参考长度（m）。</summary>
        Public Property ReferenceLength As Double = 1.0

        ''' <summary>力矩参考点。</summary>
        Public Property MomentOrigin As Moira.CFDEngine.Eulerian.EulerianVertex =
            New Moira.CFDEngine.Eulerian.EulerianVertex()

        ''' <summary>参与气动力积分的 patch 名（空 = 全部 Wall patch 且排除地面/顶面）。</summary>
        Public ReadOnly Property ForcePatches As New List(Of String)()

        ''' <summary>排除在气动力积分之外的 patch 名（默认排除 ground / top）。</summary>
        Public ReadOnly Property ForceExcludePatches As New List(Of String)()

        Public Sub New()
            ForceExcludePatches.Add("ground")
            ForceExcludePatches.Add("top")
        End Sub

    End Class

    ''' <summary>风洞湍流模型选择。</summary>
    Public Enum WindTunnelTurbulence
        ''' <summary>层流（仅用于快速验证网格与边界）。</summary>
        Laminar = 0
        ''' <summary>标准 k-ε + 壁面函数。</summary>
        KEpsilon = 1
        ''' <summary>k-ω SST（推荐用于 F1 后翼 / 扩散器与建筑分离流）。</summary>
        KOmegaSST = 2
    End Enum

    ''' <summary>风洞算例：网格 + 场 + 求解器 + 气动监控。</summary>
    Public Class EulerianWindTunnelCase

#Region "状态"

        Public ReadOnly Property Mesh As EulerianFvMesh
        Public ReadOnly Property Fields As EulerianFieldSet
        Public ReadOnly Property Options As EulerianWindTunnelOptions

        ''' <summary>不可压求解器（可压缩模式下为 Nothing）。</summary>
        Public Property Incompressible As IncompressibleSolver

        ''' <summary>可压缩求解器（不可压模式下为 Nothing）。</summary>
        Public Property Compressible As CompressibleSolver

        ''' <summary>湍流模型（层流时为 <see cref="LaminarTurbulenceModel"/>）。</summary>
        Public ReadOnly Property TurbulenceModel As EulerianTurbulenceModel

        ''' <summary>力系数历史（按 <see cref="EulerianWindTunnelOptions.ForceInterval"/> 采样）。</summary>
        Public ReadOnly Property ForceHistory As New List(Of EulerianForceReport)()

        ''' <summary>最近一次力报告。</summary>
        Public Property LastForces As EulerianForceReport

        ''' <summary>已完成步数。</summary>
        Public Property StepCount As Integer = 0

#End Region

#Region "构造与装配"

        Public Sub New(mesh As EulerianFvMesh, ExternalOptions As EulerianWindTunnelOptions)
            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))
            If ExternalOptions Is Nothing Then Throw New ArgumentNullException(NameOf(ExternalOptions))

            Me.Mesh = mesh
            Me.Options = ExternalOptions
            Me.Fields = New EulerianFieldSet(mesh)

            Dim wallDistance As EulerianWallDistance = Nothing

            Select Case ExternalOptions.Turbulence
                Case WindTunnelTurbulence.KEpsilon
                    wallDistance = EulerianWallDistance.Build(mesh)
                    Me.TurbulenceModel = New KEpsilonModel(mesh, wallDistance)
                Case WindTunnelTurbulence.KOmegaSST
                    wallDistance = EulerianWallDistance.Build(mesh)
                    Me.TurbulenceModel = New KOmegaSSTModel(mesh, wallDistance)
                Case Else
                    Me.TurbulenceModel = New LaminarTurbulenceModel(mesh)
            End Select
        End Sub

        ''' <summary>装配边界条件与初值（构造后必须调用一次）。</summary>
        Public Sub Configure()
            Dim opts = Options
            Dim dir As Moira.CFDEngine.Eulerian.EulerianVertex = opts.InletDirection.Normalized()

            If dir.Length <= 0 Then
                dir = New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)
            End If

            If opts.Compressible Then
                Dim copts As New CompressibleOptions With {
                    .Gamma = 1.4,
                    .GasConstant = 287.05,
                    .Cfl = opts.Cfl,
                    .SteadyState = Not opts.Transient,
                    .SecondOrder = True,
                    .FreestreamMach = opts.Mach,
                    .FreestreamDirection = dir}

                Dim pInf As Double = 101325.0
                Dim rhoInf As Double = pInf / (287.05 * opts.Temperature)

                copts.FreestreamPressure = pInf
                copts.FreestreamTemperature = opts.Temperature

                copts.Scheme = If(String.Equals(opts.FluxScheme, "HLLC", StringComparison.OrdinalIgnoreCase),
                                  CType(New HllcFlux(), ICompressibleFluxScheme),
                                  CType(New AusmPlusFlux(), ICompressibleFluxScheme))

                copts.Patches("inlet") = New CompressiblePatchSpec With {
                    .Kind = CompressibleBcKind.InletSubsonic,
                    .TotalPressure = pInf * std.Pow(1.0 + 0.2 * opts.Mach * opts.Mach, 3.5),
                    .TotalTemperature = opts.Temperature * (1.0 + 0.2 * opts.Mach * opts.Mach),
                    .Direction = dir}

                copts.Patches("outlet") = New CompressiblePatchSpec With {
                    .Kind = CompressibleBcKind.OutletSubsonic,
                    .StaticPressure = pInf}

                copts.Patches("ground") = New CompressiblePatchSpec With {.Kind = CompressibleBcKind.WallSlip}
                copts.Patches("top") = New CompressiblePatchSpec With {.Kind = CompressibleBcKind.WallSlip}

                Me.Compressible = New CompressibleSolver(Mesh, Fields, copts)
                Call Compressible.InitializeFromFreestream()

                Return
            End If

            Dim iopts As New IncompressibleSolverOptions With {
                .Rho = opts.Density,
                .Nu = opts.KinematicViscosity,
                .Transient = opts.Transient,
                .DeltaT = If(opts.DeltaT > 0, opts.DeltaT, 0.001),
                .Scheme = opts.Scheme,
                .RelaxU = opts.RelaxU,
                .PressureMaxIterations = opts.PressureMaxIterations,
                .UseGpu = opts.UseGpu,
                .Turbulence = If(opts.Turbulence = WindTunnelTurbulence.Laminar, Nothing, TurbulenceModel),
                .TurbulenceIntensity = opts.TurbulenceIntensity,
                .TurbulenceLengthScale = opts.TurbulenceLengthScale}

            iopts.TurbulenceContext.Rho = opts.Density
            iopts.TurbulenceContext.Nu = opts.KinematicViscosity
            iopts.TurbulenceContext.DeltaT = If(opts.Transient, iopts.DeltaT, 0)

            ' 入口：按 patch 名 "inlet*" 命中；否则由 ConfigureDefaultBoundaries 依类型处理
            For pi As Integer = 0 To Mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = Mesh.Patches(pi)

                If geo.PatchType = EulerianPatchType.Inlet Then
                    Call iopts.SetInletVelocity(geo.Name, dir.X * opts.InletSpeed, dir.Y * opts.InletSpeed, dir.Z * opts.InletSpeed)
                End If
            Next

            Me.Incompressible = New IncompressibleSolver(Mesh, Fields, iopts)
            Call Incompressible.Initialize()

            ' 全场以远场速度起步（比零初值收敛更快，且抑制启动瞬态）
            Dim n As Integer = Mesh.CellCount
            Dim u As Double() = Fields.U.Values

            For c As Integer = 0 To n - 1
                u(c) = dir.X * opts.InletSpeed
                u(n + c) = dir.Y * opts.InletSpeed
                u(2 * n + c) = dir.Z * opts.InletSpeed
            Next

            Call Fields.U.UpdateBoundaries()
        End Sub

#End Region

#Region "推进"

        ''' <summary>
        ''' 推进 <paramref name="steps"/> 步；每 <c>ForceInterval</c> 步记录一次气动力。
        ''' </summary>
        ''' <param name="onStep">
        ''' 每步回调（步号、力报告、已收敛标志）—— 供 CLI 做逐帧快照导出。
        ''' 力报告为 Nothing 表示该步未采样。
        ''' </param>
        Public Sub Run(steps As Integer, Optional onStep As Action(Of Integer, EulerianForceReport) = Nothing)
            Dim interval As Integer = std.Max(1, Options.ForceInterval)

            For i As Integer = 1 To std.Max(0, steps)
                If Compressible IsNot Nothing Then
                    Call Compressible.Advance()
                Else
                    Call Incompressible.Iterate()
                End If

                StepCount += 1

                Dim report As EulerianForceReport = Nothing

                If StepCount Mod interval = 0 OrElse i = steps Then
                    report = ComputeForces()
                    Call ForceHistory.Add(report)
                    LastForces = report
                End If

                If onStep IsNot Nothing Then
                    Call onStep(StepCount, report)
                End If
            Next
        End Sub

        ''' <summary>稳态求解至收敛或达到最大迭代数。</summary>
        Public Function Solve(Optional tolerance As Double = 1.0E-6) As Boolean
            If Compressible IsNot Nothing Then
                Dim ok As Boolean = Compressible.Solve(Options.Steps, tolerance)
                LastForces = ComputeForces()
                Call ForceHistory.Add(LastForces)
                StepCount = Compressible.StepCount
                Return ok
            End If

            Dim interval As Integer = std.Max(1, Options.ForceInterval)
            Dim conv As Boolean = False

            For i As Integer = 1 To std.Max(1, Options.Steps)
                Call Incompressible.Iterate()
                StepCount += 1

                If StepCount Mod interval = 0 Then
                    LastForces = ComputeForces()
                    Call ForceHistory.Add(LastForces)
                End If

                If Incompressible.ResidualHistory.Count > 0 AndAlso
                   Incompressible.ResidualHistory(Incompressible.ResidualHistory.Count - 1) <= tolerance Then

                    conv = True
                    Exit For
                End If
            Next

            LastForces = ComputeForces()
            Call ForceHistory.Add(LastForces)

            Return conv
        End Function

#End Region

#Region "气动力"

        ''' <summary>计算当前气动力 / 力矩与无量纲系数。</summary>
        Public Function ComputeForces() As EulerianForceReport
            Dim patches As IEnumerable(Of String) = ResolveForcePatches()

            Dim nuEff As Double() = Nothing
            Dim rhoF As Double() = Nothing

            If Compressible IsNot Nothing Then
                ' 可压缩路径：黏性力按分子粘度估计（显式求解器暂无 νt 场）
                rhoF = New Double(Mesh.FaceCount - 1) {}
                nuEff = New Double(Mesh.FaceCount - 1) {}

                Dim n As Integer = Mesh.CellCount
                Dim rho As Double() = Fields.Rho.Values

                For i As Integer = 0 To Mesh.InternalFaceCount - 1
                    Dim f As Integer = Mesh.InternalFaces(i)
                    Dim wi As Double = Mesh.InterpWeight(i)
                    Dim r As Double = wi * rho(Mesh.InternalOwner(i)) + (1.0 - wi) * rho(Mesh.InternalNeighbour(i))

                    rhoF(f) = r
                    nuEff(f) = 1.5E-5
                Next

                For pi As Integer = 0 To Mesh.PatchCount - 1
                    Dim geo As EulerianPatchGeometry = Mesh.Patches(pi)

                    For k As Integer = 0 To geo.FaceCount - 1
                        Dim f As Integer = geo.FaceIndices(k)

                        rhoF(f) = rho(geo.OwnerCells(k))
                        nuEff(f) = 1.5E-5
                    Next
                Next
            ElseIf Incompressible IsNot Nothing Then
                nuEff = Incompressible.NuEffFace
                rhoF = Incompressible.RhoFaceArray
            End If

            Return EulerianForces.Compute(Mesh, Fields, nuEff, rhoF, Options.KinematicViscosity,
                                          patches,
                                          Options.ReferenceArea, Options.ReferenceLength,
                                          Options.Density, Options.InletSpeed,
                                          Options.MomentOrigin)
        End Function

        Private Function ResolveForcePatches() As List(Of String)
            If Options.ForcePatches.Count > 0 Then
                Return Options.ForcePatches
            End If

            Dim exclude As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each nm As String In Options.ForceExcludePatches
                Call exclude.Add(nm)
            Next

            Dim list As New List(Of String)

            For pi As Integer = 0 To Mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = Mesh.Patches(pi)

                If geo.PatchType = EulerianPatchType.Wall AndAlso Not exclude.Contains(geo.Name) Then
                    list.Add(geo.Name)
                End If
            Next

            Return list
        End Function

#End Region

#Region "报告"

        ''' <summary>收敛与气动报告。</summary>
        Public ReadOnly Property Report As String
            Get
                Dim sb As New StringBuilder(512)

                sb.AppendLine($"wind tunnel case: patch='{Mesh.PatchNames()}'")
                sb.AppendLine($"  mode = {If(Compressible IsNot Nothing, "compressible (" & Options.FluxScheme & ")", "incompressible SIMPLE")}, " &
                              $"turbulence = {TurbulenceModel.Name}")
                sb.AppendLine($"  freestream = {Options.InletSpeed.ToString("G6", CultureInfo.InvariantCulture)} m/s, " &
                              $"rho = {Options.Density.ToString("G4", CultureInfo.InvariantCulture)}, " &
                              $"nu = {Options.KinematicViscosity.ToString("E3", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  steps = {StepCount}")

                If Compressible IsNot Nothing Then
                    sb.Append(Compressible.Diagnostics)
                ElseIf Incompressible IsNot Nothing Then
                    sb.Append(Incompressible.Diagnostics)
                End If

                If LastForces IsNot Nothing Then
                    sb.AppendLine($"  {LastForces.ToString()}")
                End If

                If ForceHistory.Count >= 3 Then
                    Dim a As EulerianForceReport = ForceHistory(ForceHistory.Count - 3)
                    Dim b As EulerianForceReport = ForceHistory(ForceHistory.Count - 1)

                    sb.AppendLine($"  Cd drift (last 3 samples) = {(b.Cd - a.Cd).ToString("E3", CultureInfo.InvariantCulture)}")
                End If

                Return sb.ToString()
            End Get
        End Property

#End Region

    End Class

End Namespace
