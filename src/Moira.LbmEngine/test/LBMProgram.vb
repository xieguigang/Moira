Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   Program.vb —— LbmTank CLI
'
'   /demo  顶盖驱动方腔基准（Ghia 1982 对比）+ VTI 帧序列导出
'
' /********************************************************************************/

Module LBMProgram

    Function Main(args As String()) As Integer
        If args.Length = 0 Then
            ShowHelp()
            Return 0
        End If
        Select Case args(0).ToLowerInvariant()
            Case "/demo", "--demo", "demo" : Return Demo(args.Skip(1).ToArray())
            Case "/help", "--help", "-h", "help"
                ShowHelp()
                Return 0
            Case Else
                Console.WriteLine($"unknown command: {args(0)}")
                ShowHelp()
                Return 1
        End Select
    End Function

    Private Sub ShowHelp()
        Console.WriteLine("LbmTank — 3D 格子 Boltzmann CFD 引擎 (D3Q19/BGK, VB.NET / .NET 10 / 纯 BCL)")
        Console.WriteLine()
        Console.WriteLine("  /demo [outdir] [options]   顶盖驱动方腔仿真 + Ghia 基准对比 + VTI 快照导出")
        Console.WriteLine("  /help                      显示本帮助")
        Console.WriteLine()
        Console.WriteLine("options:")
        Console.WriteLine("  --nx N        网格分辨率（立方，含壁壳层，默认 36 → 流体 34³）")
        Console.WriteLine("  --re N        雷诺数（默认 100；上限受 τ>0.505 约束）")
        Console.WriteLine("  --steps N     时间步数（默认 3000）")
        Console.WriteLine("  --interval N  每 N 步导一帧（默认 100）")
        Console.WriteLine("  --u F         顶盖速度 U_wall（格子单位，默认 0.05，低马赫假设）")
        Console.WriteLine("  --model M     碰撞模型：bgk | trt | mrt（默认 bgk；trt/mrt 高 Re 稳定）")
    End Sub

    Private Function Demo(args As String()) As Integer
        Dim outDir = If(args.Length > 0 AndAlso Not args(0).StartsWith("--"), args(0), "./lbm_out")
        Dim nx = 36, re = 100, steps = 3000, interval = 100
        Dim uWall = 0.05R
        Dim model = CollisionModel.BGK

        Dim i = 0
        While i < args.Length
            Select Case args(i)
                Case "--nx" : i += 1 : nx = CInt(args(i))
                Case "--re" : i += 1 : re = CInt(args(i))
                Case "--steps" : i += 1 : steps = CInt(args(i))
                Case "--interval" : i += 1 : interval = CInt(args(i))
                Case "--u" : i += 1 : uWall = CDbl(args(i))
                Case "--model"
                    i += 1
                    Select Case args(i).ToLowerInvariant()
                        Case "bgk" : model = CollisionModel.BGK
                        Case "trt" : model = CollisionModel.TRT
                        Case "mrt" : model = CollisionModel.MRT
                        Case Else
                            Console.WriteLine($"unknown model: {args(i)}")
                            Return 1
                    End Select
            End Select
            i += 1
        End While

        Console.WriteLine("=== LbmTank /demo 顶盖驱动方腔 ===")
        Console.WriteLine($"  网格 {nx}³ (含壁壳)   Re = {re}   U_wall = {uWall}   model = {model}   steps = {steps}")

        Dim solver As New LbmSolver(nx, nx, nx, re, uWall, model)
        Console.WriteLine($"  流体 {solver.FluidCount} 体素   H = {solver.H}   ν = {solver.Nu:G6}   τ = {solver.Tau:F4}")

        Dim recorder As New LbmVtiRecorder(outDir, interval, solver)
        Dim sw = System.Diagnostics.Stopwatch.StartNew()

        For s = 1 To steps
            solver.Advance()
            If s Mod 100 = 0 Then
                Console.WriteLine($"  [step {s,5}]  max|u| = {solver.MaxSpeed:F5}  E = {solver.Energy:F6}  " &
                                 $"Δmass = {solver.MassDrift:E3}")
            End If
            recorder.Capture(solver)
        Next
        sw.Stop()

        ' ---- Ghia 基准对比 ----
        Dim cx = solver.CenterlineX()
        Dim errU = GhiaBenchmark.L2Error(cx, GhiaBenchmark.Y100, GhiaBenchmark.U100)

        Console.WriteLine()
        Console.WriteLine("==== Ghia 1982 (Re=100) 中心线对比（展向中面）====")
        Console.WriteLine("  y      u_sim     u_ref")
        For g = 0 To GhiaBenchmark.Y100.Length - 1
            Dim y = GhiaBenchmark.Y100(g)
            Dim uSim = GhiaBenchmark.L2Interp(cx, y)
            Console.WriteLine($"  {y:F4}  {uSim,8:F4}  {GhiaBenchmark.U100(g),8:F4}")
        Next
        Console.WriteLine()
        Console.WriteLine($"  L2(u) = {errU:P2}    （3D 展向中面 vs 2D 谱解，粗网格 + 未完全收敛）")
        Console.WriteLine("  v 剖面（水平中心线）不参与验收：流传 Ghia v 表系镜像混淆数据（见 README）")

        recorder.Finish()

        Console.WriteLine()
        Console.WriteLine($"VTI 快照已导出: {System.IO.Path.GetFullPath(outDir)}")
        Console.WriteLine($"  {recorder.FrameCount} 帧 .vti + animation.pvd (ParaView 直接打开播放)")
        Console.WriteLine("  frames.json + metadata.json (浏览器 VTK.js / CfdDataset 加载)")
        Console.WriteLine($"  计算耗时 = {sw.Elapsed.TotalSeconds:F1} s ({sw.Elapsed.TotalMilliseconds / steps:F0} ms/step)")
        Return 0
    End Function

End Module
