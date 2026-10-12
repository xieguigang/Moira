' /********************************************************************************/
'
'   ChannelTest.vb —— 教科书算例：均匀来流通过空通道（无任何障碍）
'
'   目的：把"SIMPLE + 压力修正 + 边界条件"这套算法放到**已知答案**的问题上验证。
'        空通道均匀来流的稳态解就是 u = (U∞, 0, 0)、∇p = 0
'        （入口/出口压力差仅由粘性损失决定，层流下极小）。
'
'   预期行为：
'     · 压力修正方程的右端 div(phiHbyA) 应 ≈ 0（均匀场散度自由）
'     · 解应保持 u ≈ (U∞, 0, 0)，压力保持在 O(ρU∞²) 量级（不是 1e7）
'     · 连续性误差应单调下降到机器零量级
'
'   如果空通道都会发散，则问题在算法/边界条件，与网格无关；
'   如果空通道正常，则问题在"网格 + 障碍处初值不满足离散连续性"。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Globalization
Imports Moira.CFDEngine.Eulerian
Imports Moira.FVMEngine.Eulerian
Imports Moira.ZMesh
Imports Math = System.Math

Module ChannelTest

    Private ReadOnly CI As CultureInfo = CultureInfo.InvariantCulture

    Public Function RunChannelTest(nx As Integer, ny As Integer, nz As Integer,
                                   speed As Double, nu As Double, steps As Integer,
                                   useGpu As Boolean, pressureIterations As Integer,
                                   pressureTol As Double, relaxP As Double,
                                   relaxU As Double, momSweeps As Integer,
                                   Optional obstacle As Boolean = False) As Integer

        Console.WriteLine()
        Console.WriteLine(New String("="c, 100))
        Console.WriteLine($"  [K] 通道验证（{If(obstacle, "有实心方块障碍", "无障碍")}）：{nx}x{ny}x{nz}，U∞={speed} m/s，nu={nu:G4}")
        Console.WriteLine(New String("="c, 100))

        ' ---- 构造长方体六面体网格 ----
        Dim lx As Double = 20.0, ly As Double = 1.0, lz As Double = 1.0
        Dim mesh As EulerianFvMesh = BuildBox(nx, ny, nz, lx, ly, lz, obstacle)

        Console.WriteLine(mesh.Diagnostics.TrimEnd())

        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = speed,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.0R,
            .KinematicViscosity = nu,
            .Turbulence = WindTunnelTurbulence.Laminar,
            .Compressible = False,
            .UseGpu = useGpu,
            .PressureMaxIterations = pressureIterations}

        Dim tc As New EulerianWindTunnelCase(mesh, opts)

        Call tc.Configure()

        If tc.Incompressible IsNot Nothing Then
            tc.Incompressible.Options.PressureTolerance = pressureTol
            tc.Incompressible.Options.RelaxP = relaxP
            tc.Incompressible.Options.RelaxU = relaxU
            tc.Incompressible.Options.MomentumSweeps = momSweeps
        End If

        Dim n As Integer = mesh.CellCount

        ' ---- 初始通量的散度（应为 0）----
        Dim inc As IncompressibleSolver = tc.Incompressible
        Dim f0 As Double() = PrivateArrayC(inc, "_flux")
        Dim d0(n - 1) As Double

        Call EulerianOperators.FvcDiv(mesh, f0, d0)

        Console.WriteLine($"")
        Console.WriteLine($"  入口/出口通量：{PatchFluxC(mesh, f0, "inlet"):E3} / {PatchFluxC(mesh, f0, "outlet"):E3}")
        Console.WriteLine($"  初始 maxAbs(div φ) = {MaxAbsC(d0):E4}（均匀来流应 ≈ 0）")
        Console.WriteLine($"  初始 Ux 范围 = [{MinMaxUx(tc, True):E3}, {MinMaxUx(tc, False):E3}]")

        ' ---- 推进 ----
        Console.WriteLine($"")
        Console.WriteLine("  step   max|U|        max|p|        continuity     pIt   pRes(init->final)")

        Dim sw As Stopwatch = Stopwatch.StartNew()

        For s As Integer = 1 To Math.Max(1, steps)
            Call tc.Run(1)

            Dim u As Double() = tc.Fields.U.Values
            Dim um As Double = 0, nan As Integer = 0

            For c As Integer = 0 To n - 1
                Dim m2 As Double = u(c) ^ 2 + u(n + c) ^ 2 + u(2 * n + c) ^ 2

                If Double.IsNaN(m2) OrElse Double.IsInfinity(m2) Then
                    nan += 1
                Else
                    um = Math.Max(um, m2)
                End If
            Next

            Dim pr As EulerianSolverReport = tc.Incompressible.PressureReport

            Console.WriteLine($"  {s,4}   {If(nan > 0, "NaN(" & nan & ")", Math.Sqrt(um).ToString("E3", CI)),-13} " &
                              $"{MaxAbsP(tc),-13:E3} {tc.Incompressible.ContinuityError,-14:E3} " &
                              $"{pr.Iterations,-5} {pr.InitialResidual:E3}->{pr.FinalResidual:E3}")

            If nan > 0 Then
                Console.WriteLine("  => 出现 NaN，终止")
                Exit For
            End If

            If Math.Sqrt(um) > 1.0E6 Then
                Console.WriteLine("  => 速度已超出物理量级（>1e6 m/s），判定发散，终止")
                Exit For
            End If
        Next

        sw.Stop()

        Console.WriteLine($"")
        Console.WriteLine($"  用时 {sw.Elapsed.TotalSeconds:F1} s（{sw.Elapsed.TotalMilliseconds / Math.Max(1, steps):F1} ms/step）")

        ' ---- 断言 ----
        Dim uFin As Double() = tc.Fields.U.Values
        Dim uxErr As Double = 0, uyMax As Double = 0, uzMax As Double = 0

        For c As Integer = 0 To n - 1
            uxErr = Math.Max(uxErr, Math.Abs(uFin(c) - speed))
            uyMax = Math.Max(uyMax, Math.Abs(uFin(n + c)))
            uzMax = Math.Max(uzMax, Math.Abs(uFin(2 * n + c)))
        Next

        Dim ok As Boolean = uxErr < 0.05 * speed AndAlso uyMax < 0.05 * speed AndAlso uzMax < 0.05 * speed AndAlso
                            tc.Incompressible.ContinuityError < 1.0E-3

        Console.WriteLine($"")
        Console.WriteLine($"  max|Ux − U∞| = {uxErr:E3}    max|Uy| = {uyMax:E3}    max|Uz| = {uzMax:E3}")
        Console.WriteLine($"  连续性误差 = {tc.Incompressible.ContinuityError:E3}")
        Console.WriteLine($"  判定：{If(ok, "[PASS] 均匀来流保持", "[FAIL] 未能保持均匀来流")}")

        Return If(ok, 0, 1)
    End Function

    ''' <summary>
    ''' 长方体六面体网格（与 EulerianFvmSelfTest.BuildBoxMesh 等价）。
    ''' <paramref name="obstacle"/> = True 时挖掉中部一个实心方块，
    ''' 复刻"均匀笛卡尔网格 + 障碍物"的几何（city_seed42 的同类几何）。
    ''' </summary>
    Private Function BuildBox(nx As Integer, ny As Integer, nz As Integer,
                              lx As Double, ly As Double, lz As Double,
                              Optional obstacle As Boolean = False) As EulerianFvMesh

        Dim raw As New RawCellMesh()
        Dim idx(nx, ny, nz) As Integer

        For i As Integer = 0 To nx
            For j As Integer = 0 To ny
                For k As Integer = 0 To nz
                    idx(i, j, k) = raw.AddVertex(New EulerianVertex(lx * i / nx, ly * j / ny, lz * k / nz))
                Next
            Next
        Next

        ' 障碍物范围：x ∈ [40%, 60%]，y ∈ [0%, 50%]，z 全展
        Dim i0 As Integer = CInt(0.4 * nx), i1 As Integer = CInt(0.6 * nx) - 1
        Dim j0 As Integer = 0, j1 As Integer = CInt(0.5 * ny) - 1

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim solid As Boolean = obstacle AndAlso i >= i0 AndAlso i <= i1 AndAlso j >= j0 AndAlso j <= j1

                    If solid Then Continue For

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

    Private Function MaxAbsC(a As Double()) As Double
        Dim m As Double = 0

        For i As Integer = 0 To a.Length - 1
            If Math.Abs(a(i)) > m Then m = Math.Abs(a(i))
        Next

        Return m
    End Function

    Private Function PrivateArrayC(obj As Object, fieldName As String) As Double()
        Dim f As Reflection.FieldInfo = obj.GetType().GetField(
            fieldName, Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)

        If f Is Nothing Then Throw New MissingFieldException(fieldName)

        Return CType(f.GetValue(obj), Double())
    End Function

    Private Function PatchFluxC(mesh As EulerianFvMesh, flux As Double(), name As String) As Double
        Dim pi As Integer = mesh.FindPatch(name)

        If pi < 0 Then Return 0

        Return EulerianOperators.PatchFlux(mesh, flux, pi)
    End Function

    Private Function MaxAbsP(tc As EulerianWindTunnelCase) As Double
        Dim p As Double() = tc.Fields.P.Values
        Dim m As Double = 0

        For i As Integer = 0 To p.Length - 1
            If Math.Abs(p(i)) > m Then m = Math.Abs(p(i))
        Next

        Return m
    End Function

    Private Function MinMaxUx(tc As EulerianWindTunnelCase, wantMin As Boolean) As Double
        Dim u As Double() = tc.Fields.U.Values
        Dim m As Double = If(wantMin, Double.MaxValue, Double.MinValue)

        For i As Integer = 0 To tc.Mesh.CellCount - 1
            If wantMin Then
                m = Math.Min(m, u(i))
            Else
                m = Math.Max(m, u(i))
            End If
        Next

        Return m
    End Function

End Module
