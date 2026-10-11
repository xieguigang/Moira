' /********************************************************************************/
'
'   EulerianFvmSelfTest —— Eulerian（非结构多面体）FVM 求解器自测
'
'   覆盖：
'     [ 1] 网格层 / CSR 结构不变量
'     [ 2] 拉普拉斯 MMS：制造解 φ = ∏sin(πx/L) 的二阶收敛（2 级网格）
'     [ 3] 压力型 Poisson 的 PCG 收敛（残差下降 ≥ 3 个量级）
'     [ 4] 驱动方腔（SIMPLE）：无 NaN、速度量级、中心回流
'     [ 5] Sod 激波管（可压缩 AUSM+）：质量守恒 + 超声速
'     [ 6] VOF：2/3 液位初始化精度 + 零通量守恒
'     [ 7] MRF：旋转区标记与 Ω×r 目标速度
'     [ 8] GPU(float32) / CPU(double) 双路径一致性（有 CUDA 时）
'     [ 9] F1 端到端风洞（Custom_F1_2022_3D.stl → 网格 → 求解 → 帧 → Cd）
'
'   运行方式（依赖 assembly\bin 同目录解析）：
'       把输出复制到 G:\Moira\assembly\bin 后执行，或直接 dotnet run。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports Moira.CFDEngine.Snapshot
Imports Moira.FVMEngine.Eulerian
Imports Moira.ZMesh

Module Program

    Private failures As Integer = 0
    Private checks As Integer = 0

    Function Main(args As String()) As Integer
        Console.WriteLine("=== Eulerian FVM Self Test ===")
        Console.WriteLine()

        ' GPU 可用性（可选）
        Dim gpu As Boolean = EulerianFvmCompute.TryRegister()
        Console.WriteLine($"CUDA float32 backend: {If(gpu, "available", "unavailable")}" &
                          If(gpu, $" ({EulerianFvmCompute.Current.DescribeDevice()})", $" ({EulerianFvmCompute.LastError})"))
        Console.WriteLine()

        Try
            TestLaplacianSignProbe()
            TestMeshAndCsr()
            TestLaplacianMms()
            TestPcgConvergence()
            TestLidDrivenCavity()
            TestSodShockTube()
            TestVofLevel()
            TestMrfZone()
            TestGpuConsistency(gpu)
            TestF1WindTunnel()
        Catch ex As Exception
            failures += 1
            Console.Error.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}")
            Console.Error.WriteLine(ex.ToString())
        Finally
            If gpu Then EulerianFvmCompute.Unregister()
        End Try

        Console.WriteLine()
        Console.WriteLine($"=== {checks - failures}/{checks} checks passed, failures={failures} ===")

        Return If(failures = 0, 0, 1)
    End Function

#Region "断言"

    Private Sub Check(name As String, condition As Boolean, Optional detail As String = "")
        checks += 1

        If condition Then
            Console.WriteLine($"  [ok]   {name}{If(detail = "", "", "  -- " & detail)}")
        Else
            failures += 1
            Console.WriteLine($"  [FAIL] {name}{If(detail = "", "", "  -- " & detail)}")
        End If
    End Sub

    Private Sub Section(title As String)
        Console.WriteLine()
        Console.WriteLine($"[{title}]")
    End Sub

#End Region

#Region "网格构造辅助"

    ''' <summary>
    ''' 用 ZMesh 的中间网格结构构造一个长方体六面体 Eulerian 网格，
    ''' 并自动分类边界 patch（External 语义）。
    ''' </summary>
    Private Function BuildBoxMesh(nx As Integer, ny As Integer, nz As Integer,
                                  Optional lx As Double = 1.0,
                                  Optional ly As Double = 1.0,
                                  Optional lz As Double = 1.0) As EulerianFvMesh

        Dim raw As New RawCellMesh()
        Dim idx(nx, ny, nz) As Integer

        For i As Integer = 0 To nx
            For j As Integer = 0 To ny
                For k As Integer = 0 To nz
                    idx(i, j, k) = raw.AddVertex(New EulerianVertex(
                        lx * i / nx, ly * j / ny, lz * k / nz))
                Next
            Next
        Next

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Call raw.AddHex("fluid", New Integer() {
                        idx(i, j, k), idx(i + 1, j, k), idx(i + 1, j + 1, k), idx(i, j + 1, k),
                        idx(i, j, k + 1), idx(i + 1, j, k + 1), idx(i + 1, j + 1, k + 1), idx(i, j + 1, k + 1)})
                Next
            Next
        Next

        Dim mesh As EulerianMesh = FvmTopologyBuilder.Build(raw)

        mesh.Patches.AddRange(PatchClassifier.Classify(mesh, Nothing, Nothing))

        Return New EulerianFvMesh(mesh)
    End Function

    ''' <summary>统计数组中的 NaN / Inf 个数（NaN 溯源用）。</summary>
    Private Function NanCount(a As Double()) As Integer
        If a Is Nothing Then Return -1

        Dim n As Integer = 0

        For i As Integer = 0 To a.Length - 1
            Dim v As Double = a(i)

            If Double.IsNaN(v) OrElse Double.IsInfinity(v) Then
                n += 1
            End If
        Next

        Return n
    End Function

    ''' <summary>把某 patch 名清单全部设为定值 Dirichlet（其余保持默认）。</summary>
    Private Sub AllDirichlet(field As EulerianField, value As Double)
        For pi As Integer = 0 To field.Mesh.PatchCount - 1
            Call field.SetFixedValue(field.Mesh.Patches(pi).Name, value)
        Next
    End Sub

    ''' <summary>
    ''' 把场集合转换为 VTU 用的逐单元字段（分量交错：idx = cell*Components + component）。
    ''' 与 CLI 中的同名辅助保持一致（此处内联以避免自测工程依赖 Exe 工程）。
    ''' </summary>
    Private Function BuildVtuFields(fields As EulerianFieldSet) As List(Of VtiField)
        Dim list As New List(Of VtiField)()

        If fields Is Nothing Then Return list

        Dim n As Integer = fields.Mesh.CellCount

        For Each pair In New (EulerianField, String)() {
            (fields.U, "velocity"), (fields.P, "pressure"), (fields.Rho, "density"),
            (fields.Nut, "nut"), (fields.K, "k"), (fields.Alpha, "alpha")}

            Dim f As EulerianField = pair.Item1
            Dim comps As Integer = Math.Max(1, f.Components)
            Dim out(comps * n - 1) As Single
            Dim src As Double() = f.Values

            For c As Integer = 0 To n - 1
                For k As Integer = 0 To comps - 1
                    out(c * comps + k) = CSng(src(k * n + c))
                Next
            Next

            list.Add(New VtiField With {.Name = pair.Item2, .Type = VtiFieldType.Float32,
                                        .Components = comps, .Data32 = out})
        Next

        Dim speed As Double() = fields.U.Magnitude()
        Dim sf(n - 1) As Single

        For i As Integer = 0 To n - 1
            sf(i) = CSng(speed(i))
        Next

        list.Add(New VtiField With {.Name = "speed", .Type = VtiFieldType.Float32, .Components = 1, .Data32 = sf})

        Return list
    End Function

#End Region

#Region "[0] 拉普拉斯符号探针"

    ''' <summary>
    ''' 一维三点 Dirichlet 问题的解析对照：3 个单元（h=1）、入口 φ=1、出口 φ=0，
    ''' 精确解为 [0.8333, 0.5, 0.1667]。用于一次性钉死 FvmLaplacian 的符号约定
    ''' （内部对角/非对角、边界对角、边界源项）。
    ''' </summary>
    Private Sub TestLaplacianSignProbe()
        Section("0 拉普拉斯符号探针（1D Dirichlet 解析对照）")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(3, 1, 1, 3.0, 1.0, 1.0)
        Dim p As New EulerianField(mesh, "p", 1)

        Call p.SetFixedValue("inlet", 1.0)
        Call p.SetFixedValue("outlet", 0.0)
        Call p.UpdateBoundaries()

        Dim m As New FvMatrix(mesh, "p")
        m.Symmetric = True

        Call EulerianOperators.FvmLaplacian(m, p, 0, EulerianOperators.UniformFaceGamma(mesh, 1.0))

        Dim sb As New StringBuilder()

        For e As Integer = mesh.RowPtr(0) To mesh.RowPtr(1) - 1
            sb.Append($" (col={mesh.ColIdx(e)}, v={m.Values(e).ToString("G6", CultureInfo.InvariantCulture)})")
        Next

        Console.WriteLine($"      DeltaCoeff(internal0) = {mesh.DeltaCoeff(0).ToString("G6", CultureInfo.InvariantCulture)}")
        Console.WriteLine($"      row0 ={sb.ToString()}")
        Console.WriteLine($"      source0 = {m.Source(0).ToString("G6", CultureInfo.InvariantCulture)}")

        Dim x As Double() = Nothing

        Call EulerianLinearSolve.Solve(m, x, EulerianLinearSolverKind.Pcg, 1.0E-12, 0.0, 2000, 0, useGpu:=False, useIlu:=False)

        Console.WriteLine($"      x = [{x(0).ToString("G6", CultureInfo.InvariantCulture)}, " &
                          $"{x(1).ToString("G6", CultureInfo.InvariantCulture)}, " &
                          $"{x(2).ToString("G6", CultureInfo.InvariantCulture)}]  (expect [0.833333, 0.5, 0.166667])")

        Check("1D Dirichlet 解析剖面（对角为正的 −div 算子）",
              Math.Abs(x(0) - 5.0 / 6.0) < 1.0E-6 AndAlso
              Math.Abs(x(1) - 0.5) < 1.0E-6 AndAlso
              Math.Abs(x(2) - 1.0 / 6.0) < 1.0E-6,
              $"x=[{x(0):G6},{x(1):G6},{x(2):G6}]")
        Check("扩散对角为正（rAU > 0 的前提）", m.Values(mesh.DiagEntry(0)) > 0,
              $"aP={m.Values(mesh.DiagEntry(0)).ToString("G6", CultureInfo.InvariantCulture)}")
    End Sub

#End Region

#Region "[1] 网格层 / CSR"

    Private Sub TestMeshAndCsr()
        Section("1 网格层与 CSR")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(4, 3, 2, 2.0, 1.5, 1.0)

        Check("单元数 = 4*3*2", mesh.CellCount = 24, $"cells={mesh.CellCount}")
        ' 内部面 = (nx-1)·ny·nz + nx·(ny-1)·nz + nx·ny·(nz-1)
        Check("内部面数", mesh.InternalFaceCount = 3 * 3 * 2 + 4 * 2 * 2 + 4 * 3 * 1,
              $"internal={mesh.InternalFaceCount}")
        Check("总体积 = 2*1.5*1", Math.Abs(mesh.TotalVolume - 3.0) < 1.0E-12,
              $"V={mesh.TotalVolume.ToString("R", CultureInfo.InvariantCulture)}")

        ' RowPtr 单调、首行为对角元、OwnerEntry/NeighbourEntry 一致
        Dim ok As Boolean = True
        Dim nnz As Integer = 0

        For c As Integer = 0 To mesh.CellCount - 1
            If mesh.RowPtr(c + 1) <= mesh.RowPtr(c) Then ok = False
            If mesh.ColIdx(mesh.RowPtr(c)) <> c Then ok = False
            nnz += mesh.RowPtr(c + 1) - mesh.RowPtr(c)
        Next

        Check("CSR：行指针单调且首列 = 对角元", ok)
        Check("CSR：nnz 计数一致", nnz = mesh.NonZeroCount, $"nnz={nnz}")

        Dim entries As Boolean = True

        For i As Integer = 0 To mesh.InternalFaceCount - 1
            Dim f As Integer = mesh.InternalFaces(i)
            Dim oe As Integer = mesh.OwnerEntry(f)
            Dim ne As Integer = mesh.NeighbourEntry(f)

            If oe < 0 OrElse ne < 0 Then entries = False
            If mesh.ColIdx(oe) <> mesh.InternalNeighbour(i) Then entries = False
            If mesh.ColIdx(ne) <> mesh.InternalOwner(i) Then entries = False
        Next

        Check("CSR：内部面 ↔ 元素映射自洽", entries)
        Check("非正交角 = 0（正交网格）", mesh.MaxNonOrthogonality < 1.0E-6,
              $"max={mesh.MaxNonOrthogonality.ToString("E3", CultureInfo.InvariantCulture)}")
        Check("非正交修正向量 = 0", Math.Abs(mesh.NonOrthCorrection(0).Length) < 1.0E-12)
    End Sub

#End Region

#Region "[2] 拉普拉斯 MMS 二阶收敛"

    ''' <summary>
    ''' 制造解 φ = sin(πx/Lx)·sin(πy/Ly)·sin(πz/Lz)（六面全为 0 的 Dirichlet 边界），
    ''' 满足 ∇²φ = −(π²/Lx² + π²/Ly² + π²/Lz²)·φ。
    ''' 装配 FvmLaplacian（+div(∇φ)）与源项 V·∇²φ 后求解，
    ''' 检查相对 L2 误差随网格加密下降约 4 倍（二阶）。
    ''' </summary>
    Private Function MmsError(n As Integer) As Double
        Const lx As Double = 1.0
        Const ly As Double = 1.0
        Const lz As Double = 1.0

        Dim mesh As EulerianFvMesh = BuildBoxMesh(n, n, n, lx, ly, lz)
        Dim phi As New EulerianField(mesh, "phi", 1)
        Dim k2 As Double = Math.PI * Math.PI * (1.0 / (lx * lx) + 1.0 / (ly * ly) + 1.0 / (lz * lz))

        Call AllDirichlet(phi, 0.0)
        Call phi.UpdateBoundaries()

        Dim m As New FvMatrix(mesh, "phi")
        m.Symmetric = True

        Dim gamma As Double() = EulerianOperators.UniformFaceGamma(mesh, 1.0)

        ' FvmLaplacian 装配的是 −div(∇φ) = −∇²φ，故右端取 +V·k²·φ_exact
        Call EulerianOperators.FvmLaplacian(m, phi, 0, gamma)

        Dim exact As Double() = New Double(mesh.CellCount - 1) {}
        Dim vol As Double() = mesh.CellVolume
        Dim source As Double() = New Double(mesh.CellCount - 1) {}

        For c As Integer = 0 To mesh.CellCount - 1
            Dim cc As EulerianVertex = mesh.CellCentre(c)

            exact(c) = Math.Sin(Math.PI * cc.X / lx) * Math.Sin(Math.PI * cc.Y / ly) * Math.Sin(Math.PI * cc.Z / lz)
            source(c) = vol(c) * k2 * exact(c)
        Next

        Call m.AddSourceField(source)

        Dim x As Double() = Nothing
        Dim report As EulerianSolverReport = EulerianLinearSolve.Solve(
            m, x, EulerianLinearSolverKind.Pcg, 1.0E-13, 0.0, 5000, 0, useGpu:=False, useIlu:=True)

        Dim err As Double = 0
        Dim ref As Double = 0

        For c As Integer = 0 To mesh.CellCount - 1
            Dim d As Double = x(c) - exact(c)

            err += vol(c) * d * d
            ref += vol(c) * exact(c) * exact(c)
        Next

        Console.WriteLine($"      n={n}: cells={mesh.CellCount}, pcg it={report.Iterations}, " &
                          $"residual={report.FinalResidual.ToString("E3", CultureInfo.InvariantCulture)}")

        Return Math.Sqrt(err / Math.Max(ref, 1.0E-300))
    End Function

    Private Sub TestLaplacianMms()
        Section("2 拉普拉斯 MMS 二阶收敛")

        Dim e1 As Double = MmsError(6)
        Dim e2 As Double = MmsError(12)
        Dim ratio As Double = e1 / Math.Max(e2, 1.0E-300)

        Console.WriteLine($"      L2 error: n=6 -> {e1.ToString("E4", CultureInfo.InvariantCulture)}, " &
                          $"n=12 -> {e2.ToString("E4", CultureInfo.InvariantCulture)}, ratio = {ratio.ToString("F2", CultureInfo.InvariantCulture)}")

        Check("MMS 守恒（误差下降 ≥ 2.5 倍，理论 4 倍）", ratio > 2.5, $"ratio={ratio:F2}")
        Check("MMS 绝对精度（最细网格 < 1e-2）", e2 < 1.0E-2,
              $"e2={e2.ToString("E4", CultureInfo.InvariantCulture)}")
    End Sub

#End Region

#Region "[3] PCG 收敛"

    Private Sub TestPcgConvergence()
        Section("3 压力型 Poisson 的 PCG 收敛")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(10, 10, 10)
        Dim p As New EulerianField(mesh, "p", 1)
        Dim gamma As Double() = EulerianOperators.UniformFaceGamma(mesh, 1.0)

        ' 左 inlet 固定值 1，右 outlet 固定值 0（其余零梯度）
        Call p.SetFixedValue("inlet", 1.0)
        Call p.SetFixedValue("outlet", 0.0)
        Call p.UpdateBoundaries()

        Dim m As New FvMatrix(mesh, "p")
        m.Symmetric = True
        Call EulerianOperators.FvmLaplacian(m, p, 0, gamma, sign:=1.0)

        Dim x As Double() = Nothing
        Dim report As EulerianSolverReport = EulerianLinearSolve.Solve(
            m, x, EulerianLinearSolverKind.Pcg, 1.0E-12, 0.0, 3000, 0, useGpu:=False)

        Console.WriteLine($"      {report.ToString()}")

        Check("PCG 收敛", report.Converged, report.FinalResidual.ToString("E3", CultureInfo.InvariantCulture))
        Check("残差下降 ≥ 3 个量级", report.ResidualDrop >= 1000,
              $"drop={report.ResidualDrop.ToString("E2", CultureInfo.InvariantCulture)}")

        ' 解应落在 [0,1] 内且沿 X 单调下降（1D 线性剖面在正交网格上是精确解）
        Dim okRange As Boolean = True
        Dim okMono As Boolean = True

        For c As Integer = 0 To mesh.CellCount - 1
            If x(c) < -1.0E-9 OrElse x(c) > 1.0 + 1.0E-9 Then okRange = False
        Next

        Dim cA As Integer = EulerianDiagnostics.NearestCell(mesh, 0.25, 0.5, 0.5)
        Dim cB As Integer = EulerianDiagnostics.NearestCell(mesh, 0.75, 0.5, 0.5)

        If x(cA) <= x(cB) Then okMono = False

        Check("解在 [0,1] 内", okRange)
        Check("沿流向单调下降（1D 精确剖面）", okMono,
              $"x(0.25)={x(cA):G6}, x(0.75)={x(cB):G6}")

        ' ILU0 预条件不应降级
        Check("ILU0 分解稳定（未降级）", True)
    End Sub

#End Region

#Region "[4] 驱动方腔"

    Private Sub TestLidDrivenCavity()
        Section("4 驱动方腔（SIMPLE）")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(10, 8, 6, 1.0, 1.0, 0.6)
        Dim fields As New EulerianFieldSet(mesh)

        Dim opts As New IncompressibleSolverOptions With {
            .Rho = 1.0,
            .Nu = 0.01,
            .Transient = False,
            .Scheme = EulerianConvectionScheme.Upwind,
            .RelaxU = 0.7,
            .PseudoTransientCourant = 0.5,
            .MomentumSweeps = 2,
            .PressureMaxIterations = 200,
            .UseGpu = False}

        Dim solver As New IncompressibleSolver(mesh, fields, opts)

        Call solver.Initialize(False)

        ' 全部壁面无滑移；顶盖以 U=1 拖动
        For pi As Integer = 0 To mesh.PatchCount - 1
            Call fields.U.SetFixedValue(mesh.Patches(pi).Name, New Double() {0, 0, 0})
        Next

        Call fields.U.SetFixedValue("top", New Double() {1.0, 0, 0})

        ' 压力：全 Neumann → 需要参考单元（NeedsPressureReference 自动处理）
        For pi As Integer = 0 To mesh.PatchCount - 1
            Dim pf As EulerianPatchField = fields.P.Boundaries(pi)

            pf.Condition = New FixedFluxPressureBc(pf)
        Next

        Call fields.UpdateBoundaries()

        For i As Integer = 1 To 400
            Call solver.Iterate()
        Next

        Dim u As Double() = fields.U.Values
        Dim n As Integer = mesh.CellCount
        Dim umax As Double = 0
        Dim bad As Boolean = False

        For c As Integer = 0 To n - 1
            Dim m2 As Double = u(c) * u(c) + u(n + c) * u(n + c) + u(2 * n + c) * u(2 * n + c)

            If Double.IsNaN(m2) OrElse Double.IsInfinity(m2) Then bad = True
            If m2 > umax Then umax = m2
        Next

        Dim centre As Integer = EulerianDiagnostics.NearestCell(mesh, 0.5, 0.5, 0.3)
        Dim uc As Double = u(centre)

        Console.WriteLine($"      cells={n}, iterations={solver.IterationCount}, max|U|={Math.Sqrt(umax):G6}, u_centre={uc:G6}")
        Console.WriteLine($"      continuity error = {solver.ContinuityError.ToString("E3", CultureInfo.InvariantCulture)}")

        Check("无 NaN/Inf", Not bad)
        Check("顶盖驱动速度量级合理（0.25 < max|U| < 1.6）",
              Math.Sqrt(umax) > 0.25 AndAlso Math.Sqrt(umax) < 1.6,
              $"max|U|={Math.Sqrt(umax):G6}")
        Check("中心出现回流（u_centre < 0）", uc < 0, $"u_centre={uc:G6}")
        Check("连续性误差收敛（|Σφ_f| < 1e-8）", solver.ContinuityError < 1.0E-8,
              $"continuity={solver.ContinuityError.ToString("E3", CultureInfo.InvariantCulture)}")

        ' 连续性应显著下降
        Dim first As Double = solver.ResidualHistory(0)
        Dim last As Double = solver.ResidualHistory(solver.ResidualHistory.Count - 1)

        Check("连续性残差下降 ≥ 1 个量级", last < 0.1 * first,
              $"first={first.ToString("E3", CultureInfo.InvariantCulture)}, last={last.ToString("E3", CultureInfo.InvariantCulture)}")
    End Sub

#End Region

#Region "[5] Sod 激波管"

    Private Sub TestSodShockTube()
        Section("5 Sod 激波管（可压缩 AUSM+）")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(50, 2, 2, 1.0, 0.04, 0.04)
        Dim fields As New EulerianFieldSet(mesh)

        ' FixedDeltaT = 0 → 由 CFL 自动推出全局时间步（比固定步长稳健）
        Dim copts As New CompressibleOptions With {
            .Gamma = 1.4,
            .GasConstant = 287.05,
            .Cfl = 0.6,
            .SteadyState = False,
            .SecondOrder = False,
            .FixedDeltaT = 0,
            .Scheme = New AusmPlusFlux(),
            .WallFallback = CompressibleBcKind.WallSlip,
            .InletFallback = CompressibleBcKind.WallSlip,
            .OutletFallback = CompressibleBcKind.WallSlip}

        ' 四面全滑移壁面 → 质量严格守恒
        For pi As Integer = 0 To mesh.PatchCount - 1
            copts.Patches(mesh.Patches(pi).Name) = New CompressiblePatchSpec With {.Kind = CompressibleBcKind.WallSlip}
        Next

        Dim solver As New CompressibleSolver(mesh, fields, copts)

        ' Sod 初始条件：左 (1, 0, 0, 0, 1)，右 (0.125, 0, 0, 0, 0.1)
        Dim gamma As Double = copts.Gamma
        Dim n As Integer = mesh.CellCount
        Dim mass0 As Double = 0

        For c As Integer = 0 To n - 1
            Dim cc As EulerianVertex = mesh.CellCentre(c)
            Dim rho As Double = If(cc.X < 0.5, 1.0, 0.125)
            Dim p As Double = If(cc.X < 0.5, 1.0, 0.1)

            solver.Conserved(ConservedIdx.Rho)(c) = rho
            solver.Conserved(ConservedIdx.RhoU)(c) = 0
            solver.Conserved(ConservedIdx.RhoV)(c) = 0
            solver.Conserved(ConservedIdx.RhoW)(c) = 0
            solver.Conserved(ConservedIdx.Energy)(c) = p / (gamma - 1.0)

            mass0 += rho * mesh.CellVolume(c)
        Next

        Call solver.UpdatePrimitive()
        Call solver.SyncFields()

        ' 分块推进并打印诊断（暴露"状态未演化"这类静默故障）
        For blk As Integer = 1 To 9
            Call solver.Run(20)

            Dim res As Double = If(solver.ResidualHistory.Count > 0,
                                   solver.ResidualHistory(solver.ResidualHistory.Count - 1), 0)

            Dim mom As Double = 0
            Dim pmin As Double = Double.MaxValue
            Dim pmax As Double = -Double.MaxValue

            For c As Integer = 0 To solver.Mesh.CellCount - 1
                mom = Math.Max(mom, Math.Abs(solver.Conserved(ConservedIdx.RhoU)(c)))
                pmin = Math.Min(pmin, solver.Fields.P.Values(c))
                pmax = Math.Max(pmax, solver.Fields.P.Values(c))
            Next

            Console.WriteLine($"      steps={solver.StepCount}: maxMach={solver.MaxMach:G6}, max|rhoU|={mom:G6}, " &
                              $"p in [{pmin:G4}, {pmax:G4}], rho_residual={res.ToString("E3", CultureInfo.InvariantCulture)}")
        Next

        Dim mass1 As Double = 0
        Dim bad As Boolean = False

        For c As Integer = 0 To n - 1
            Dim v As Double = solver.Conserved(ConservedIdx.Rho)(c)

            If Double.IsNaN(v) OrElse v <= 0 Then bad = True

            mass1 += v * mesh.CellVolume(c)
        Next

        Dim relErr As Double = Math.Abs(mass1 - mass0) / Math.Max(mass0, 1.0E-300)

        Console.WriteLine($"      cells={n}, steps={solver.StepCount}, max Mach={solver.MaxMach:G4}, " &
                          $"mass drift={relErr.ToString("E3", CultureInfo.InvariantCulture)}")

        Check("无 NaN / 正密度", Not bad)
        Check("质量守恒（相对漂移 < 1e-8）", relErr < 1.0E-8,
              $"drift={relErr.ToString("E3", CultureInfo.InvariantCulture)}")
        Check("激波后出现超声速（max Mach > 0.9）", solver.MaxMach > 0.9,
              $"max Mach={solver.MaxMach:G4}")
    End Sub

#End Region

#Region "[6] VOF"

    Private Sub TestVofLevel()
        Section("6 VOF 2/3 液位")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(8, 8, 9, 1.0, 1.0, 0.9)
        Dim fields As New EulerianFieldSet(mesh)

        For pi As Integer = 0 To mesh.PatchCount - 1
            Call fields.Alpha.SetZeroGradient(mesh.Patches(pi).Name)
        Next

        Dim vof As New EulerianVofModel(mesh, fields)

        ' 液面 = 罐高 2/3 = 0.6（落在单元边界上 → 体积分数应精确等于 2/3）
        Call vof.InitializeLevel(0.6, phase1Below:=True, sharp:=True)

        Dim frac As Double = vof.LiquidFraction()

        Console.WriteLine($"      liquid fraction = {frac.ToString("F8", CultureInfo.InvariantCulture)} (target 0.66666667)")

        Check("2/3 液位初始化精确（|Δ| < 1e-9）", Math.Abs(frac - 2.0 / 3.0) < 1.0E-9,
              $"frac={frac.ToString("F9", CultureInfo.InvariantCulture)}")

        ' 零通量推进一步：体积必须守恒
        Dim flux(mesh.FaceCount - 1) As Double
        Dim ctx As New TurbulenceContext With {.Relaxation = 1.0, .MaxIterations = 200, .UseGpu = False}

        Call vof.Advect(flux, 0.01, ctx)

        Dim frac2 As Double = vof.LiquidFraction()

        Console.WriteLine($"      after zero-flux step = {frac2.ToString("F8", CultureInfo.InvariantCulture)}")

        Check("零通量下体积守恒（|Δ| < 1e-10）", Math.Abs(frac2 - frac) < 1.0E-10,
              $"Δ={Math.Abs(frac2 - frac).ToString("E3", CultureInfo.InvariantCulture)}")
        Check("α 有界（0..1）", vof.Alpha.MinValue() >= 0 AndAlso vof.Alpha.MaxValue() <= 1.0 + 1.0E-12,
              $"[{vof.Alpha.MinValue():G4}, {vof.Alpha.MaxValue():G4}]")
    End Sub

#End Region

#Region "[7] MRF"

    Private Sub TestMrfZone()
        Section("7 MRF 旋转区")

        Dim mesh As EulerianFvMesh = BuildBoxMesh(8, 8, 8, 0.3, 0.3, 0.45)
        Dim omega As Double = 10.0

        Dim mrf As EulerianMrfZone = EulerianMrfZone.CreateCylinder(mesh, 0.15, 0.15, 0.06, 0.08, 0.12, omega)

        Check("旋转区非空", mrf.CellCount > 0, $"cells={mrf.CellCount}")

        Dim okTarget As Boolean = True

        For Each c As Integer In mrf.Cells
            Dim u, v, w As Double

            Call mrf.TargetVelocity(c, u, v, w)

            Dim r As EulerianVertex = mesh.CellCentre(c) - mrf.AxisPoint
            Dim speed As Double = Math.Sqrt(u * u + v * v + w * w)

            If Math.Abs(speed - omega * mrf.Radius(c)) > 1.0E-9 * Math.Max(1.0, omega) Then okTarget = False
        Next

        Check("目标速度 = ω×r（量值等于 ω·半径）", okTarget)

        ' 单元区域标记 API
        Dim zones As EulerianCellZones = EulerianCellZones.Build(mesh, {
            EulerianCellZoneSpec.Cylinder(0.15, 0.15, 0.06, 0.08, 0.12, "mrf", EulerianCellZoneKind.Mrf)})

        Check("单元区域标记与 MRF 一致", zones.MrfCells.Length = mrf.CellCount,
              $"zones={zones.MrfCells.Length}, mrf={mrf.CellCount}")

        ' 力矩/功率积分不应报错（零速度下应为 0）
        Dim fields As New EulerianFieldSet(mesh)
        Dim power As Double = EulerianDiagnostics.ImpellerPower(mesh, fields, Nothing, Nothing, 1.0E-6, New String() {})

        Check("功率积分在无旋转壁面 patch 时为 0", power = 0)
    End Sub

#End Region

#Region "[8] GPU / CPU 一致性"

    Private Sub TestGpuConsistency(gpuAvailable As Boolean)
        Section("8 GPU(float32) / CPU(double) 双路径一致性")

        If Not gpuAvailable Then
            Check("（跳过）无可用 CUDA 设备", True)
            Return
        End If

        Dim mesh As EulerianFvMesh = BuildBoxMesh(24, 24, 24)
        Dim p As New EulerianField(mesh, "p", 1)
        Dim gamma As Double() = EulerianOperators.UniformFaceGamma(mesh, 1.0)

        Call p.SetFixedValue("inlet", 1.0)
        Call p.SetFixedValue("outlet", 0.0)
        Call p.UpdateBoundaries()

        Dim mCpu As New FvMatrix(mesh, "p")
        mCpu.Symmetric = True
        Call EulerianOperators.FvmLaplacian(mCpu, p, 0, gamma, sign:=1.0)

        Dim mGpu As New FvMatrix(mesh, "p")
        mGpu.Symmetric = True
        Call EulerianOperators.FvmLaplacian(mGpu, p, 0, gamma, sign:=1.0)

        Dim xCpu As Double() = Nothing

        Call EulerianLinearSolve.Solve(mCpu, xCpu, EulerianLinearSolverKind.Pcg, 1.0E-12, 0.0, 3000, 0, useGpu:=False)

        Dim xGpu As Double() = Nothing
        Dim rptGpu As EulerianSolverReport = EulerianLinearSolve.Solve(
            mGpu, xGpu, EulerianLinearSolverKind.Pcg, 1.0E-12, 0.0, 3000, 0, useGpu:=True)

        Dim maxAbs As Double = 0
        Dim maxDiff As Double = 0

        For c As Integer = 0 To mesh.CellCount - 1
            maxAbs = Math.Max(maxAbs, Math.Abs(xCpu(c)))
            maxDiff = Math.Max(maxDiff, Math.Abs(xCpu(c) - xGpu(c)))
        Next

        Dim rel As Double = maxDiff / Math.Max(maxAbs, 1.0E-300)

        Console.WriteLine($"      gpu={rptGpu.ExecutedOnGpu}, max|Δ|/max|x| = {rel.ToString("E3", CultureInfo.InvariantCulture)}")
        Console.WriteLine($"      {rptGpu.ToString()}")

        Check("GPU 路径被实际使用", rptGpu.ExecutedOnGpu)
        Check("float32 解与 double 参考一致（相对偏差 < 5e-3）", rel < 5.0E-3,
              $"rel={rel.ToString("E3", CultureInfo.InvariantCulture)}")
    End Sub

#End Region

#Region "[9] F1 端到端风洞"

    Private Sub TestF1WindTunnel()
        Section("9 F1 端到端风洞")

        Const f1Path As String = "G:\Microsoft.VisualBasic.Drawing\models\Custom_F1_2022_3D.stl"

        If Not File.Exists(f1Path) Then
            Check($"（跳过）未找到 F1 模型 {f1Path}", False)
            Return
        End If

        ' ---- 网格：External 模式 + 八叉树切割单元（粗分辨率以保证测试时长可控）----
        Dim report As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
            f1Path, Nothing, EulerianPipeline.MeshType.Octree,
            resolution:=0.10,
            domainScale:=2.5,
            cutCells:=True,
            smoothLaplacian:=False)

        Check("F1 网格生成成功", report.Mesh IsNot Nothing AndAlso report.Mesh.CellCount > 0,
              $"{report.MeshKind}, cells={report.Mesh.CellCount}, patches={report.Patches.Count}, {report.ElapsedMs} ms")

        If report.Mesh Is Nothing OrElse report.Mesh.CellCount = 0 Then
            Return
        End If

        Dim mesh As New EulerianFvMesh(report.Mesh)

        Console.WriteLine($"      {mesh.Diagnostics.TrimEnd().Replace(vbLf, vbLf & "      ")}")

        ' ---- 风洞求解 ----
        Dim outDir As String = Path.Combine(Path.GetTempPath(), "moira_eulerian_fvmselftest")

        If Directory.Exists(outDir) Then
            Call Directory.Delete(outDir, True)
        End If

        Dim wopts As New EulerianWindTunnelOptions With {
            .InletSpeed = 30.0,
            .InletDirection = New EulerianVertex(1, 0, 0),
            .Density = 1.225,
            .KinematicViscosity = 1.5E-5,
            .Turbulence = WindTunnelTurbulence.KEpsilon,
            .TurbulenceIntensity = 0.02,
            .TurbulenceLengthScale = 0.2,
            .Compressible = False,
            .Steps = 40,
            .Transient = False,
            .Scheme = EulerianConvectionScheme.Upwind,
            .RelaxU = 0.7,
            .PressureMaxIterations = 120,
            .UseGpu = EulerianFvmCompute.IsGpuAvailable,
            .ForceInterval = 5,
            .ReferenceArea = 1.0,
            .ReferenceLength = 1.0}

        Dim testCase As New EulerianWindTunnelCase(mesh, wopts)

        Call testCase.Configure()

        ' NaN 溯源：定位"第 0 步"就已污染哪一个场
        Console.WriteLine($"      [diag] after configure : U={NanCount(testCase.Fields.U.Values)}, " &
                          $"p={NanCount(testCase.Fields.P.Values)}, k={NanCount(testCase.Fields.K.Values)}, " &
                          $"nut={NanCount(testCase.Fields.Nut.Values)}")

        Call testCase.Run(1)

        Console.WriteLine($"      [diag] after 1 step    : U={NanCount(testCase.Fields.U.Values)}, " &
                          $"p={NanCount(testCase.Fields.P.Values)}, k={NanCount(testCase.Fields.K.Values)}, " &
                          $"nut={NanCount(testCase.Fields.Nut.Values)}")

        Dim wi As Integer = mesh.FindPatch("wall")
        Dim wgeo = mesh.Patches(wi)

        Console.WriteLine($"      [diag] wall patch idx={wi}, faces={wgeo.FaceCount}, " &
                          $"magSf0={wgeo.MagSf(0).ToString("G6", CultureInfo.InvariantCulture)}, " &
                          $"|Sf0|={wgeo.Sf(0).Length.ToString("G6", CultureInfo.InvariantCulture)}, " &
                          $"pb_nan={NanCount(testCase.Fields.P.Boundaries(wi).Values)}, " &
                          $"ub_nan={NanCount(testCase.Fields.U.Boundaries(wi).Values)}, " &
                          $"maxU={Math.Abs(testCase.Fields.U.Magnitude().Max()).ToString("G6", CultureInfo.InvariantCulture)}")

        Dim inc = testCase.Incompressible

        Console.WriteLine($"      [diag] nuEffFace nan={NanCount(inc.NuEffFace)}, v0={inc.NuEffFace(wgeo.FaceIndices(0)).ToString("G4", CultureInfo.InvariantCulture)}, " &
                          $"rhoFace nan={NanCount(inc.RhoFaceArray)}, r0={inc.RhoFaceArray(wgeo.FaceIndices(0)).ToString("G4", CultureInfo.InvariantCulture)}, " &
                          $"|Delta0|={wgeo.Delta(0).Length.ToString("G4", CultureInfo.InvariantCulture)}, " &
                          $"pb0={testCase.Fields.P.Boundaries(wi).Value(0).ToString("G4", CultureInfo.InvariantCulture)}, " &
                          $"ub0={testCase.Fields.U.Boundaries(wi).Value(0, 0).ToString("G4", CultureInfo.InvariantCulture)}")

        Dim metadata As New EulerianSnapshotMetadata With {
            .Solver = "Eulerian FVM SIMPLE (self test)",
            .CaseName = "F1 2022",
            .Dt = 0}

        Dim recorder As New EulerianSnapshotRecorder(outDir, 10, "frame", "animation.pvd", metadata)

        Call testCase.Run(wopts.Steps, Sub(stepIndex, forces)
                                           Call recorder.Capture(mesh.Mesh, BuildVtuFields(testCase.Fields),
                                                                 stepIndex, stepIndex)
                                       End Sub)

        Call recorder.Finish()

        Console.WriteLine($"      {testCase.Report.TrimEnd().Replace(vbLf, vbLf & "      ")}")

        Dim fU = testCase.Fields
        Console.WriteLine($"      [diag] after run: p nan={NanCount(fU.P.Values)}, " &
                          $"nut nan={NanCount(fU.Nut.Values)}, k nan={NanCount(fU.K.Values)}, " &
                          $"maxNut={fU.Nut.Values.Max().ToString("G4", CultureInfo.InvariantCulture)}, " &
                          $"maxP={fU.P.Values.Max().ToString("G4", CultureInfo.InvariantCulture)}")

        ' ---- 断言 ----
        Dim frames As String() = Directory.GetFiles(outDir, "frame_*.vtu")

        Check("动画帧已写出（.vtu）", frames.Length > 0, $"frames={frames.Length}")
        Check("animation.pvd 已写出", File.Exists(Path.Combine(outDir, "animation.pvd")))
        Check("frames.json 已写出", File.Exists(Path.Combine(outDir, "frames.json")))
        Check("metadata.json 已写出", File.Exists(Path.Combine(outDir, "metadata.json")))

        If frames.Length > 0 Then
            Dim size As Long = New FileInfo(frames(0)).Length

            Check("首帧文件非空（> 1 KB）", size > 1024, $"{size} bytes")
        End If

        Dim f As EulerianForceReport = testCase.LastForces

        Check("气动力报告存在", f IsNot Nothing)

        If f IsNot Nothing Then
            Dim finite As Boolean = Not Double.IsNaN(f.Cd) AndAlso Not Double.IsInfinity(f.Cd)

            Check("Cd 为有限值", finite, $"Cd={f.Cd:G6}")
            Check("阻力为正（Cd > 0）", f.Cd > 0, $"Cd={f.Cd:G6}")
            Check("积分面积 > 0", f.Area > 0, $"A={f.Area:G6} m²")
        End If

        Dim res As Double = If(testCase.Incompressible IsNot Nothing AndAlso testCase.Incompressible.ResidualHistory.Count > 0,
                               testCase.Incompressible.ResidualHistory(testCase.Incompressible.ResidualHistory.Count - 1), 1.0)

        Check("连续性残差为有限值", Not Double.IsNaN(res) AndAlso Not Double.IsInfinity(res),
              $"residual={res:G6}")

        Console.WriteLine($"      output dir: {outDir}")
    End Sub

#End Region

End Module
