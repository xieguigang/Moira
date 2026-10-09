' /********************************************************************************/
'
'   Program.vb
'
'   发酵罐 3D SPH 搅拌仿真 —— 控制台示例入口
'
'   场景：
'       - 圆柱形发酵罐（半径 --radius，高 --height）
'       - 罐底附近有旋转搅拌桨（Rushton 圆盘涡轮，转速 --rpm）
'       - 发酵液液面位于罐高的 --fill（默认 3/4）处
'
'   输出：
'       每一帧采样一次粒子场到规则体素网格，用 CFDEngine.Snapshot 写出
'           frame_XXXX.vti      —— 二进制 VTK ImageData（mask/pressure/density/velocity）
'           animation.pvd       —— ParaView 时间集合，可直接播放动画
'           frames.json         —— 浏览器端（VTK.js）帧清单
'           metadata.json       —— 网格 / 掩膜 / dt / 求解器元数据（CfdDataset 可直接加载）
'
'   运行：
'       dotnet run --project src\SPHEngine -- --steps 60 --interval 1
'       dotnet run --project src\SPHEngine -- --particles 8000 --steps 20 --no-gpu
'
' /********************************************************************************/

Imports std = System.Math
Imports Microsoft.VisualBasic.Imaging.Physics

Module Program

    ''' <summary>参数默认值</summary>
    Private particles As Integer = 15000
    Private gridRes As Integer = 40
    Private tankRadius As Double = 0.15
    Private tankHeight As Double = 0.45
    Private fill As Double = 0.75
    Private rpm As Double = 150.0
    Private steps As Integer = 60
    Private dt As Double = 0.02
    Private interval As Integer = 1
    Private viscosity As Single = 4.0F
    Private soundFactor As Single = 8.0F
    Private outDir As String = Nothing
    Private enableGpu As Boolean = True
    Private showHelp As Boolean = False

    Sub Main()
        Dim argv = System.Environment.GetCommandLineArgs()
        Call ParseArgs(argv)

        If showHelp Then
            Call PrintHelp()
            Return
        End If

        If outDir Is Nothing Then
            outDir = IO.Path.Combine(AppContext.BaseDirectory, "fermenter_frames")
        End If

        Call PrintBanner()

        ' ---- 1. 构建发酵罐场景 ----
        Console.WriteLine("[1] 构建圆柱形发酵罐场景 ...")
        Dim t0 = DateTime.Now

        Dim tank As New FermenterTankSPH(
            tankRadius:=tankRadius,
            tankHeight:=tankHeight,
            fillFraction:=fill,
            particles:=particles,
            rpm:=rpm,
            nx:=gridRes,
            viscosity:=viscosity,
            soundSpeedFactor:=soundFactor)

        Console.WriteLine($"    罐体      : 半径 {tank.TankRadius:F3} m, 高 {tank.TankHeight:F3} m")
        Console.WriteLine($"    液面      : {tank.LiquidHeight:F3} m (罐高的 {tank.FillFraction:P0})")
        Console.WriteLine($"    搅拌桨    : 半径 {tank.Impeller.Radius:F3} m @ z={tank.Impeller.ZCenter:F3} m, " &
                          $"{tank.Impeller.Rpm:F1} rpm")
        Console.WriteLine($"    SPH 粒子  : {tank.ParticleCount} 个, 间距 {tank.Spacing * 1000:F1} mm, " &
                          $"平滑半径 {tank.SmoothingRadius * 1000:F1} mm")
        Console.WriteLine($"    输出网格  : {tank.Field.Nx} x {tank.Field.Ny} x {tank.Field.Nz} 体素 " &
                          $"(活动 {tank.Shape.TotalActive})")
        Console.WriteLine($"    静止密度  : {tank.Engine.RestDensity:F6}（自动标定）")
        Console.WriteLine($"    人工声速  : {tank.Engine.EffectiveSoundSpeed:F2} m/s, K={tank.Engine.EffectivePressureK:F0}")
        Console.WriteLine($"    建场耗时  : {(DateTime.Now - t0).TotalSeconds:F2} s")
        Console.WriteLine()

        Call PrintLiquidProfile(tank)

        ' ---- 2. 可选 CUDA 加速 ----
        If enableGpu Then
            Console.WriteLine("[2] 尝试启用 CUDA GPU 后端 ...")
            If CudaSphBackend.TryEnableGpu(tank.Engine) Then
                Console.WriteLine($"    GPU 后端已启用：{tank.Engine.Backend.Name}")
                ' 标量场（密度/压力）只在采样帧回读，减少每子步的 PCIe 传输
                DirectCast(tank.Engine.Backend, CudaSphBackend).FullSync = False
            Else
                Console.WriteLine($"    GPU 不可用（{CudaSphBackend.LastError}），回落 CPU 并行后端：{tank.Engine.Backend.Name}")
            End If
        Else
            tank.Engine.Backend = New SphCpuCompute3D()
            Console.WriteLine($"[2] 已按 --no-gpu 强制 CPU 后端：{tank.Engine.Backend.Name}")
        End If
        Console.WriteLine()

        ' ---- 3. 运行并导出快照 ----
        Console.WriteLine($"[3] 运行 {steps} 步（每步 {dt:F4} s，模拟总时长 {steps * dt:F3} s），" &
                          $"每 {interval} 步存一帧 ...")
        Console.WriteLine($"    输出目录  : {outDir}")
        Console.WriteLine()

        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim frameCount As Integer = 0

        Call tank.Run(steps, dt, interval, outDir,
                      Sub(stepIdx, time, maxSpeed)
                          If stepIdx = 1 OrElse (stepIdx Mod 5) = 0 OrElse stepIdx = steps Then
                              Console.WriteLine($"    步 {stepIdx,4} / {steps}  t={time,7:F3}s  " &
                                                $"maxSpeed={maxSpeed,6:F3} m/s  " &
                                                $"meanRho={tank.MeanNormalizedDensity():F4}  " &
                                                $"子步={tank.Engine.LastSubSteps}")
                          End If
                      End Sub)
        sw.Stop()

        frameCount = IO.Directory.GetFiles(outDir, "frame_*.vti").Length

        Console.WriteLine()
        Console.WriteLine($"    完成！耗时 {sw.Elapsed.TotalSeconds:F2} s " &
                          $"（平均每步 {sw.Elapsed.TotalMilliseconds / steps:F1} ms）")
        Console.WriteLine()

        ' ---- 4. 产物清单 ----
        Console.WriteLine("[4] 快照产物：")
        For Each f In {"animation.pvd", "frames.json", "metadata.json"}
            Dim p = IO.Path.Combine(outDir, f)
            If IO.File.Exists(p) Then
                Console.WriteLine($"    {f,-16} {New IO.FileInfo(p).Length / 1024:F1} KB")
            End If
        Next
        Console.WriteLine($"    frame_*.vti      {frameCount} 帧")
        Console.WriteLine()
        Console.WriteLine("    在 ParaView 中打开 animation.pvd 即可回放 CFD 动画；")
        Console.WriteLine("    浏览器端（VTK.js）可读取 frames.json 逐帧加载。")
        Console.WriteLine()

        Console.WriteLine("[5] 终态统计：")
        Console.WriteLine("    " & tank.Summary())
        Call PrintFieldStats(tank)
        Call PrintBands(tank)
        Call PrintFinalProfile(tank)
        Console.WriteLine()
    End Sub

    ''' <summary>
    ''' 打印初始液面沿 Z 方向的层平均填充率（用于核对液位 3/4 与圆柱掩膜）
    ''' </summary>
    Private Sub PrintLiquidProfile(tank As FermenterTankSPH)
        Call tank.SampleField()

        Dim f = tank.Field
        Dim vz = tank.VoxelSizeZ
        Console.WriteLine("    初始液面剖面（层平均填充率，1 = 满液）：")

        Dim stride = std.Max(1, f.Nz \ 12)

        For k As Integer = f.Nz - 1 To 0 Step -stride
            Dim sum As Double = 0
            Dim count As Integer = 0

            For i As Integer = 0 To f.Nx - 1
                For j As Integer = 0 To f.Ny - 1
                    If Not f.IsActive(i, j, k) Then Continue For
                    sum += f.Density(i, j, k)
                    count += 1
                Next
            Next

            If count = 0 Then Continue For

            Dim mean = sum / count
            Dim barLen = CInt(std.Min(40, mean * 40))
            Dim z = (k + 0.5) * vz
            Dim mark = If(z > tank.LiquidHeight, "  (空气)", "")

            Console.WriteLine($"      z={z:F3} m  {mean:F3} |{New String("#"c, barLen)}{mark}")
        Next

        Console.WriteLine()
    End Sub

    ''' <summary>打印终态液面剖面（核对搅拌后的液面形态）</summary>
    Private Sub PrintFinalProfile(tank As FermenterTankSPH)
        Dim f = tank.Field
        Dim vz = tank.VoxelSizeZ

        Console.WriteLine("    终态液面剖面（层平均填充率）：")

        Dim stride = std.Max(1, f.Nz \ 10)

        For k As Integer = f.Nz - 1 To 0 Step -stride
            Dim sum As Double = 0
            Dim count As Integer = 0

            For i As Integer = 0 To f.Nx - 1
                For j As Integer = 0 To f.Ny - 1
                    If Not f.IsActive(i, j, k) Then Continue For
                    sum += f.Density(i, j, k)
                    count += 1
                Next
            Next

            If count = 0 Then Continue For

            Dim mean = sum / count
            Dim barLen = CInt(std.Min(40, mean * 40))
            Console.WriteLine($"      z={(k + 0.5) * vz:F3} m  {mean:F3} |{New String("#"c, barLen)}")
        Next
    End Sub

    ''' <summary>打印分层密度（核对静水压分层与液面高度）</summary>
    Private Sub PrintBands(tank As FermenterTankSPH)
        Dim bands = tank.DensityBands(6)
        Dim H = tank.TankHeight

        Console.WriteLine($"    分层密度  : 液面 {tank.SurfaceHeight():F3} m / 静止 {tank.LiquidHeight:F3} m")

        For b As Integer = bands.Length - 1 To 0 Step -1
            Console.WriteLine($"      z={H * b / 6:F3}-{H * (b + 1) / 6:F3} m  n={bands(b).count,6}  rho={bands(b).rho:F4}")
        Next
    End Sub

    ''' <summary>统计导出网格场（即 .vti 中实际写入的数据）</summary>
    Private Sub PrintFieldStats(tank As FermenterTankSPH)
        Dim f = tank.Field
        Dim maxS As Double = 0, sumS As Double = 0, cnt As Integer = 0
        Dim maxD As Double = 0, minP As Double = Double.MaxValue, maxP As Double = Double.MinValue

        For i As Integer = 0 To f.Nx - 1
            For j As Integer = 0 To f.Ny - 1
                For k As Integer = 0 To f.Nz - 1
                    If Not f.IsActive(i, j, k) Then Continue For

                    Dim u = f.U(i, j, k), v = f.V(i, j, k), w = f.W(i, j, k)
                    Dim s = std.Sqrt(u * u + v * v + w * w)
                    If s > maxS Then maxS = s
                    sumS += s
                    cnt += 1

                    If f.Density(i, j, k) > maxD Then maxD = f.Density(i, j, k)
                    If f.Pressure(i, j, k) < minP Then minP = f.Pressure(i, j, k)
                    If f.Pressure(i, j, k) > maxP Then maxP = f.Pressure(i, j, k)
                Next
            Next
        Next

        If cnt = 0 Then Return

        Console.WriteLine($"    导出场    : 活动体素 {cnt}，最大速度 {maxS:F3} m/s，" &
                          $"平均速度 {sumS / cnt:F4} m/s")
        Console.WriteLine($"    密度范围  : [0, {maxD:F3}]   压力范围 : [{minP:F3}, {maxP:F3}]")
    End Sub

    Private Sub PrintBanner()
        Console.WriteLine(New String("="c, 72))
        Console.WriteLine("  发酵罐 CFD 仿真（3D SPH） —— 搅拌混合流场与 VTI 快照导出")
        Console.WriteLine("  Fermentation Tank CFD (3D SPH) - stirred flow & VTI snapshot export")
        Console.WriteLine(New String("="c, 72))
        Console.WriteLine()
    End Sub

    Private Sub ParseArgs(argv As String())
        Dim i As Integer = 1

        While i < argv.Length
            Dim a = argv(i).ToLower()

            Select Case a
                Case "--help", "-h", "help", "?"
                    showHelp = True
                Case "--particles"
                    i += 1 : Integer.TryParse(Arg(argv, i), particles)
                Case "--grid"
                    i += 1 : Integer.TryParse(Arg(argv, i), gridRes)
                Case "--radius"
                    i += 1 : Double.TryParse(Arg(argv, i), tankRadius)
                Case "--height"
                    i += 1 : Double.TryParse(Arg(argv, i), tankHeight)
                Case "--fill"
                    i += 1 : Double.TryParse(Arg(argv, i), fill)
                Case "--rpm"
                    i += 1 : Double.TryParse(Arg(argv, i), rpm)
                Case "--steps"
                    i += 1 : Integer.TryParse(Arg(argv, i), steps)
                Case "--dt"
                    i += 1 : Double.TryParse(Arg(argv, i), dt)
                Case "--interval"
                    i += 1 : Integer.TryParse(Arg(argv, i), interval)
                Case "--viscosity"
                    i += 1 : Single.TryParse(Arg(argv, i), viscosity)
                Case "--sound"
                    i += 1 : Single.TryParse(Arg(argv, i), soundFactor)
                Case "--out"
                    i += 1 : outDir = Arg(argv, i)
                Case "--no-gpu"
                    enableGpu = False
            End Select

            i += 1
        End While

        If gridRes < 8 Then gridRes = 8
        If particles < 100 Then particles = 100
        If steps < 1 Then steps = 1
        If interval < 1 Then interval = 1
        If dt <= 0 Then dt = 0.02
        If fill <= 0 OrElse fill > 1 Then fill = 0.75
    End Sub

    Private Function Arg(argv As String(), i As Integer) As String
        If i < argv.Length Then Return argv(i)
        Return ""
    End Function

    Private Sub PrintHelp()
        Console.WriteLine("用法：dotnet run --project src\SPHEngine -- [选项]")
        Console.WriteLine()
        Console.WriteLine("  --particles N   SPH 粒子数（默认 15000）")
        Console.WriteLine("  --grid N        输出体素网格 X/Y 分辨率（默认 40）")
        Console.WriteLine("  --radius R      罐体半径 [m]（默认 0.15）")
        Console.WriteLine("  --height H      罐体高度 [m]（默认 0.45）")
        Console.WriteLine("  --fill F        液位 / 罐高（默认 0.75）")
        Console.WriteLine("  --rpm N         搅拌桨转速（默认 150）")
        Console.WriteLine("  --steps N       输出步数（默认 60）")
        Console.WriteLine("  --dt T          每步模拟时间 [s]（默认 0.02）")
        Console.WriteLine("  --interval N    每隔多少步存一帧（默认 1）")
        Console.WriteLine("  --viscosity V   速度松弛粘性 [1/s]（默认 4）")
        Console.WriteLine("  --sound S       人工声速 / 参考速度（默认 8）")
        Console.WriteLine("  --out DIR       快照输出目录")
        Console.WriteLine("  --no-gpu        强制 CPU 后端")
        Console.WriteLine("  --help          显示本帮助")
    End Sub

End Module
