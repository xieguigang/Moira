Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   Program.vb —— FvmTank 命令行
'
'   用法：
'     FvmTank /demo [outdir] [--nx N] [--steps N] [--rpm N] [--interval N] [--fast]
'     FvmTank /help
'
'   /demo：标准 Rushton 通气搅拌罐仿真（MRF + k-ε + 欧拉两相 + DO 传质），
'          逐帧导出 .vti + animation.pvd + frames.json + metadata.json，
'          可直接用 ParaView 打开动画播放。
'
' /********************************************************************************/

Imports System.IO
Imports Moira.CFDEngine
Imports Moira.CFDEngine.Snapshot
Imports Moira.CFDEngine.Snapshot.JSON
Imports Moira.FVMEngine

Public Module Program

    Public Function Main(args As String()) As Integer
        If args.Length = 0 Then
            ShowHelp()
            Return 0
        End If

        Select Case args(0).ToLowerInvariant()
            Case "/demo", "--demo"
                Return Demo(args.Skip(1).ToArray())
            Case "/help", "--help", "/?"
                ShowHelp()
                Return 0
            Case Else
                Console.WriteLine($"unknown command: {args(0)}")
                ShowHelp()
                Return 1
        End Select
    End Function

    Private Sub ShowHelp()
        Console.WriteLine("FvmTank — 发酵罐 FVM 仿真模拟器 (VB.NET / .NET 10 / 纯 BCL)")
        Console.WriteLine()
        Console.WriteLine("  /demo [outdir] [options]   运行标准 Rushton 通气搅拌罐仿真并导出 VTI 快照")
        Console.WriteLine("  /help                      显示本帮助")
        Console.WriteLine()
        Console.WriteLine("options:")
        Console.WriteLine("  --nx N         网格分辨率（X/Y 方向，Z 自动 = 1.25×nx，默认 36）")
        Console.WriteLine("  --steps N      伪瞬态步数（默认 240）")
        Console.WriteLine("  --rpm N        搅拌转速（默认 120）")
        Console.WriteLine("  --interval N   每 N 步导出一帧（默认 20）")
        Console.WriteLine("  --gas Q        充气量 m³/s（默认 0.0004）")
        Console.WriteLine("  --fast         跳过 O2/DO 与 k-ε 扩展（快速调试）")
    End Sub

    Private Function Demo(args As String()) As Integer
        ' ---- 参数解析 ----
        Dim outDir = "tank_out"
        Dim nx = 36
        Dim steps = 240
        Dim rpm = 120.0R
        Dim interval = 20
        Dim gas = 0.0004R

        Dim i = 0
        While i < args.Length
            Select Case args(i)
                Case "--nx" : i += 1 : nx = Integer.Parse(args(i))
                Case "--steps" : i += 1 : steps = Integer.Parse(args(i))
                Case "--rpm" : i += 1 : rpm = Single.Parse(args(i), Globalization.CultureInfo.InvariantCulture)
                Case "--interval" : i += 1 : interval = Integer.Parse(args(i))
                Case "--gas" : i += 1 : gas = Double.Parse(args(i), Globalization.CultureInfo.InvariantCulture)
                Case Else
                    If Not args(i).StartsWith("--", StringComparison.Ordinal) Then outDir = args(i)
            End Select
            i += 1
        End While

        Console.WriteLine("FvmTank demo — 发酵罐 FVM 仿真（MRF + k-ε + 欧拉两相 + DO 传质）")
        Console.WriteLine($"  grid: {nx}x{nx}x{CInt(nx * 1.25)}  rpm: {rpm}  steps: {steps}  out: {outDir}")
        Console.WriteLine()

        Dim sw = System.Diagnostics.Stopwatch.StartNew()

        ' ---- 几何 ----
        Dim nz = CInt(System.Math.Round(nx * 1.25R))
        Dim tank As New FvmTank(nx, nx, nz,
                                viscosity:=0.000001R,
                                diffusion:=2.0R * 1e-9R,
                                rpm:=rpm)

        ' ---- 求解器 ----
        Dim solver As New FvmSolver(tank)
        solver.GasFlowRate = gas

        ' ---- 快照 ----
        ' 领域模型已统一：tank.Field 就是 Moira.CFDEngine 的混合精度 FluidField，
        ' 因此 SnapshotMetadata.FromField 可直接消费，无需任何桥接。
        Dim meta = SnapshotMetadata.FromField(
            tank.Field, tank.Viscosity, tank.Diffusion, solver.Dt,
            solver:="FVM collocated SIMPLE + MRF impeller-disk + k-epsilon + Euler two-phase + Higbie kLa")
        ' FromField 不填搅拌器（面向非罐场景），这里补上 Rushton 桨的几何与运动参数
        meta.Simulation.Stirrer = StirrerInfo.FromStirrer(tank.Stirrer)
        Dim recorder As New TankVtiRecorder(outDir, interval, meta)

        Console.WriteLine($"  active voxels: {tank.VoxelShape.TotalActive}  (of {nx * nx * nz})")
        Console.WriteLine($"  tip speed: {solver.TipSpeed:F2} m/s   dt: {solver.Dt * 1000.0R:F1} ms")
        Console.WriteLine()

        ' ---- 主循环 ----
        Dim reportEvery = System.Math.Max(1, steps \ 12)
        recorder.Capture(tank.Field, 0, 0.0R)
        For s = 1 To steps
            solver.Advance()
            If s Mod reportEvery = 0 OrElse s = steps Then
                Console.WriteLine($"  [step {s,4}]  t={solver.Time,7:F3}s  maxU={MaxAbs(tank.Field.U64):F2}m/s" &
                                  $"  massRes={solver.MassResidual:F4}  P={solver.ImpellerPower,6:F1}W  Np={solver.Np:F2}" &
                                  $"  holdup={solver.GasHoldup * 100.0R:F2}%  kLa={solver.MeanKLa:F4}/s" &
                                  $"  d32={solver.MeanD32 * 1000.0R:F2}mm")
            End If
            recorder.Capture(tank.Field, s, solver.Time)
        Next
        sw.Stop()

        Console.WriteLine()
        Console.WriteLine("==== 最终指标 ====")
        Console.WriteLine($"  搅拌功率 P     = {solver.ImpellerPower:F1} W")
        Console.WriteLine($"  功率准数 Np    = {solver.Np:F2}   (Rushton 桨典型实验值 4-5)")
        Console.WriteLine($"  平均气含率     = {solver.GasHoldup * 100.0R:F2} %")
        Console.WriteLine($"  平均 kLa      = {solver.MeanKLa:F4} 1/s")
        Console.WriteLine($"  平均 DO        = {MeanDO(solver):F2} mg/L (饱和 {solver.DOSturation:F1})")
        Console.WriteLine($"  计算耗时       = {sw.Elapsed.TotalSeconds:F1} s ({sw.Elapsed.TotalMilliseconds / steps:F0} ms/step)")

        recorder.Finish()
        Console.WriteLine()
        Console.WriteLine($"VTI 快照已导出: {Path.GetFullPath(outDir)}")
        Console.WriteLine($"  {recorder.FrameCount} 帧 .vti + animation.pvd (ParaView 直接打开播放)")
        Console.WriteLine($"  frames.json + metadata.json (浏览器 VTK.js / CfdDataset 加载)")
        Return 0
    End Function

    Private Function MaxAbs(t As Microsoft.VisualBasic.MachineLearning.TensorFlow.Tensor) As Double
        Dim m = 0.0R
        For Each v In t.Data
            Dim a = System.Math.Abs(v)
            If a > m Then m = a
        Next
        Return m
    End Function

    Private Function MeanDO(solver As FvmSolver) As Double
        Dim sum_ = 0.0R
        Dim n = 0
        For idx = 0 To solver.O2.Length - 1
            If solver.Tank.VoxelShape.Shape(idx) Then
                sum_ += solver.O2.Data(idx)
                n += 1
            End If
        Next
        If n = 0 Then Return 0.0R
        Return sum_ / n
    End Function

End Module
