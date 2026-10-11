Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   Program.vb —— LbmTank CLI
'
'   /demo    顶盖驱动方腔基准（Ghia 1982 对比，Double 基准求解器）+ VTI 帧序列导出
'   /float   float32 混合精度方腔求解器（LbmSolverF）验证：CPU/GPU 一致性 + Ghia 对比 + 性能
'   /tunnel  LBM float32 风洞求解器（WindTunnelSolver）冒烟测试（合成方柱绕流）
'
' /********************************************************************************/

Imports Moira.ZMesh

Module LBMProgram

    Function Main(args As String()) As Integer
        If args.Length = 0 Then
            ShowHelp()
            Return 0
        End If
        Select Case args(0).ToLowerInvariant()
            Case "/demo", "--demo", "demo" : Return Demo(args.Skip(1).ToArray())
            Case "/float", "--float", "float" : Return FloatDemo(args.Skip(1).ToArray())
            Case "/tunnel", "--tunnel", "tunnel" : Return TunnelDemo(args.Skip(1).ToArray())
            Case "/voxel", "--voxel", "voxel" : Return VoxelDemo(args.Skip(1).ToArray())
            Case "/voxeljson", "--voxeljson", "voxeljson" : Return VoxelJsonDemo(args.Skip(1).ToArray())
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
        Console.WriteLine("LbmTank — 3D 格子 Boltzmann CFD 引擎 (D3Q19, VB.NET / .NET 10)")
        Console.WriteLine()
        Console.WriteLine("  /demo [outdir] [options]   顶盖驱动方腔仿真（Double 基准）+ Ghia 对比 + VTI 导出")
        Console.WriteLine("  /float [options]           float32 求解器（LbmSolverF）验证：Ghia 对比 + CPU/GPU 基准")
        Console.WriteLine("  /tunnel [options]          float32 风洞求解器（WindTunnelSolver）冒烟测试（方柱绕流）")
        Console.WriteLine("  /voxel [model] [options]   ZMesh 体素化路径验证（Standard/Sdf/Physical/SdfRaw 对比）")
        Console.WriteLine("  /help                      显示本帮助")
        Console.WriteLine()
        Console.WriteLine("options:")
        Console.WriteLine("  --nx N        网格分辨率（立方，含壁壳层，默认 36 → 流体 34³）")
        Console.WriteLine("  --re N        雷诺数（默认 100；上限受 τ>0.505 约束）")
        Console.WriteLine("  --steps N     时间步数（默认 3000）")
        Console.WriteLine("  --interval N  每 N 步导一帧（默认 100，仅 /demo）")
        Console.WriteLine("  --u F         顶盖速度 U_wall（格子单位，默认 0.05，低马赫假设）")
        Console.WriteLine("  --model M     碰撞模型：bgk | trt | mrt（/float 仅支持 bgk|trt）")
        Console.WriteLine("  --cpu         强制 CPU 路径（不尝试 CUDA）")
        Console.WriteLine("  --gpu         强制要求 CUDA（不可用则报错退出）")
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

    ''' <summary>解析公共开关。</summary>
    Private Sub ParseCommon(args As String(), ByRef nx As Integer, ByRef re As Integer,
                            ByRef steps As Integer, ByRef uWall As Double,
                            ByRef model As CollisionModel,
                            ByRef useGpu As Boolean, ByRef requireGpu As Boolean)
        Dim i = 0
        While i < args.Length
            Select Case args(i)
                Case "--nx" : i += 1 : nx = CInt(args(i))
                Case "--re" : i += 1 : re = CInt(args(i))
                Case "--steps" : i += 1 : steps = CInt(args(i))
                Case "--u" : i += 1 : uWall = CDbl(args(i))
                Case "--model"
                    i += 1
                    Select Case args(i).ToLowerInvariant()
                        Case "bgk" : model = CollisionModel.BGK
                        Case "trt" : model = CollisionModel.TRT
                        Case "mrt" : model = CollisionModel.TRT   ' float 核心不支持 MRT，降级 TRT
                    End Select
                Case "--cpu" : useGpu = False
                Case "--gpu" : requireGpu = True
            End Select
            i += 1
        End While
    End Sub

    ''' <summary>
    ''' /float：float32 求解器验证与性能基准。
    ''' 输出 CUDA 注册状态、逐步诊断、Ghia 对比与 ms/step。
    ''' </summary>
    Private Function FloatDemo(args As String()) As Integer
        Dim nx = 36, re = 100, steps = 3000
        Dim uWall = 0.05R
        Dim model = CollisionModel.BGK
        Dim useGpu = True, requireGpu = False

        Call ParseCommon(args, nx, re, steps, uWall, model, useGpu, requireGpu)

        Console.WriteLine("=== LbmTank /float float32 方腔（混合精度验证）===")
        Console.WriteLine($"  网格 {nx}³ (含壁壳)   Re = {re}   U_wall = {uWall}   model = {model}   steps = {steps}")

        If requireGpu AndAlso Not CudaLbmBackend.TryRegister() Then
            Console.WriteLine($"  CUDA 不可用: {CudaLbmBackend.LastError}")
            Return 1
        End If

        Dim solver As New LbmSolverF(nx, nx, nx, re, uWall, model, useGpu)
        Console.WriteLine($"  流体 {solver.FluidCount} 体素   H = {solver.H}   ν = {solver.Nu:G6}   τ = {solver.Tau:F4}")
        Console.WriteLine($"  计算路径 = {If(solver.UseGpu, "CUDA (float32 设备驻留)", "CPU (float32 融合遍历)")}")
        If solver.UseGpu Then
            Console.WriteLine("  （GPU 路径矩量常驻显存，诊断指标按需回读）")
        End If

        Dim sw = System.Diagnostics.Stopwatch.StartNew()

        For s = 1 To steps
            solver.Advance()
            If s Mod 100 = 0 Then
                Call solver.EnsureMoments()
                Console.WriteLine($"  [step {s,5}]  max|u| = {solver.MaxSpeed:F5}  E = {solver.KineticEnergy:F6}  " &
                                 $"Δmass = {solver.MassDrift:E3}")
            End If
        Next
        sw.Stop()

        ' ---- Ghia 基准对比（与 Double 基准 CenterlineX 同构）----
        Call solver.EnsureMoments()
        Dim ci = nx \ 2, cj = nx \ 2
        Dim plane = solver.Ny * solver.Nz
        Dim m = solver.Nz - 2
        Dim cx(m - 1, 1) As Double
        For k = 1 To solver.Nz - 2
            Dim idx = ci * plane + cj * solver.Nz + k
            cx(k - 1, 0) = (k - 1) / solver.H
            cx(k - 1, 1) = solver.Core.Ux(idx) / uWall
        Next

        Dim errU = GhiaBenchmark.L2Error(cx, GhiaBenchmark.Y100, GhiaBenchmark.U100)

        Console.WriteLine()
        Console.WriteLine("==== Ghia 1982 (Re=100) 中心线对比（float32）====")
        Console.WriteLine("  y      u_sim     u_ref")
        For g = 0 To GhiaBenchmark.Y100.Length - 1
            Dim y = GhiaBenchmark.Y100(g)
            Dim uSim = GhiaBenchmark.L2Interp(cx, y)
            Console.WriteLine($"  {y:F4}  {uSim,8:F4}  {GhiaBenchmark.U100(g),8:F4}")
        Next
        Console.WriteLine()
        Console.WriteLine($"  L2(u) = {errU:P2}")

        Console.WriteLine()
        Console.WriteLine($"计算耗时 = {sw.Elapsed.TotalSeconds:F1} s ({sw.Elapsed.TotalMilliseconds / steps:F2} ms/step)")
        Return 0
    End Function

    ''' <summary>
    ''' /tunnel：LBM float32 风洞冒烟测试 —— 计算域中心放置方柱，
    ''' 定常来流绕流，输出逐步诊断与尾流亏损。
    ''' </summary>
    Private Function TunnelDemo(args As String()) As Integer
        Dim nx = 64, reDummy = 0, steps = 500
        Dim uWall = 0.0R
        Dim model = CollisionModel.TRT
        Dim useGpu = True, requireGpu = False

        ' 风洞参数：U∞ = 0.1 格子单位，ν 由 Re = U∞·D/ν 推导（D = 8 格子）
        Dim uInf = 0.1R, diam = 8, re = 100

        Dim i = 0
        While i < args.Length
            Select Case args(i)
                Case "--nx" : i += 1 : nx = CInt(args(i))
                Case "--steps" : i += 1 : steps = CInt(args(i))
                Case "--re" : i += 1 : re = CInt(args(i))
                Case "--u" : i += 1 : uInf = CDbl(args(i))
                Case "--d" : i += 1 : diam = CInt(args(i))
                Case "--cpu" : useGpu = False
                Case "--gpu" : requireGpu = True
            End Select
            i += 1
        End While

        Console.WriteLine("=== LbmTank /tunnel float32 风洞冒烟测试 ===")
        Console.WriteLine($"  域 {nx}×{nx}×{nx}   U∞ = {uInf}   D = {diam}   Re = {re}   steps = {steps}")

        If requireGpu AndAlso Not CudaLbmBackend.TryRegister() Then
            Console.WriteLine($"  CUDA 不可用: {CudaLbmBackend.LastError}")
            Return 1
        End If

        ' ---- 合成几何：中央方柱（展向贯通），贴地放置 ----
        Dim nz = nx \ 2
        Dim solid(nx * nx * nz - 1) As Boolean
        Dim plane = nx * nz
        Dim x0 = nx \ 4, x1 = x0 + diam
        Dim y0 = 2, y1 = y0 + diam

        For ix = 0 To nx - 1
            For iy = 0 To nx - 1
                For iz = 0 To nz - 1
                    If ix >= x0 AndAlso ix < x1 AndAlso iy >= y0 AndAlso iy < y1 Then
                        solid(ix * plane + iy * nz + iz) = True
                    End If
                Next
            Next
        Next

        Dim nu = uInf * diam / re
        Dim tunnel As New WindTunnelSolver(nx, nx, nz, solid, uInf, nu,
                                           groundNoSlip:=True, model:=CollisionModel.TRT,
                                           useGpu:=useGpu)

        Console.WriteLine($"  ν = {nu:G6}   τ = {nu / LatticeD3Q19.Cs2 + 0.5R:F4}   " &
                          $"流体 {tunnel.FluidCount} 格点   路径 = {If(tunnel.UseGpu, "CUDA", "CPU")}")

        Call tunnel.InitializeFlow()
        Dim sw = System.Diagnostics.Stopwatch.StartNew()

        For s = 1 To steps
            tunnel.Advance()
            If s Mod 100 = 0 Then
                Dim wake = tunnel.ComputeWakeDeficit(nx \ 2 + diam)
                Console.WriteLine($"  [step {s,5}]  max|u| = {tunnel.ComputeMaxSpeed():F5}  " &
                                  $"Δmass = {tunnel.ComputeMassDrift():E3}  wake = {wake:F4}")
            End If
        Next
        sw.Stop()

        Console.WriteLine()
        Console.WriteLine($"计算耗时 = {sw.Elapsed.TotalSeconds:F1} s ({sw.Elapsed.TotalMilliseconds / steps:F2} ms/step)")
        Return 0
    End Function

    ''' <summary>
    ''' /voxel：ZMesh 四条体素化路径对比验证（Standard / Sdf / Physical / SdfRaw）。
    ''' </summary>
    Private Function VoxelDemo(args As String()) As Integer
        Dim model = "G:\Moira\src\ZMesh\CityBlender\samples\city_seed42.stl"
        Dim resolution = 48

        Dim i = 0
        While i < args.Length
            Select Case args(i)
                Case "--res" : i += 1 : resolution = CInt(args(i))
                Case Else : model = args(i)
            End Select
            i += 1
        End While

        If Not IO.File.Exists(model) Then
            Console.WriteLine($"模型不存在: {model}")
            Return 1
        End If

        Console.WriteLine("=== LbmTank /voxel 体素化路径对比 ===")
        Console.WriteLine($"  模型 = {model}   targetResolution = {resolution}")

        For Each kind As VoxelizerKind In New VoxelizerKind() {
            VoxelizerKind.Standard, VoxelizerKind.Sdf,
            VoxelizerKind.Physical, VoxelizerKind.SdfRaw}

            Dim sw = System.Diagnostics.Stopwatch.StartNew()

            Try
                ' Physical：voxelSize 由目标分辨率对最长边的均分推导（等价于绕开
                ' 整数量化限制的直接物理尺寸调用；此处仅做路径演示）
                Dim scene = WindTunnelSceneBuilder.BuildScene(
                    model, resolution, kind,
                    domainScale:=1.0, groundClearance:=0,
                    voxelSize:=0)

                sw.Stop()
                Dim m = scene.VoxelModel

                Console.WriteLine($"  [{kind,-8}] {m.Width}x{m.Height}x{m.Depth}  " &
                                  $"solid = {m.SolidVoxelCount,7}  " &
                                  $"voxelSize = {If(m.VoxelSize IsNot Nothing, m.VoxelSize(0).ToString("G6"), "?")}  " &
                                  $"({sw.Elapsed.TotalSeconds:F1} s)")
            Catch ex As Exception
                sw.Stop()
                Console.WriteLine($"  [{kind,-8}] 失败: {ex.Message} ({sw.Elapsed.TotalSeconds:F1} s)")
            End Try
        Next

        Return 0
    End Function

    ''' <summary>
    ''' /voxeljson：加载 Voxelizer CLI 导出的体素 json 并直接跑 LBM 风洞冒烟测试
    ''' （验证「CLI 导出 → json 回读 → 风洞模拟」端到端链路）。
    ''' </summary>
    Private Function VoxelJsonDemo(args As String()) As Integer
        If args.Length = 0 OrElse Not IO.File.Exists(args(0)) Then
            Console.WriteLine("用法: LbmTank /voxeljson <voxel.json> [--steps N] [--cpu]")
            Return 1
        End If

        Dim jsonPath = args(0)
        Dim steps = 200
        Dim useGpu = True

        Dim i = 1
        While i < args.Length
            Select Case args(i)
                Case "--steps" : i += 1 : steps = CInt(args(i))
                Case "--cpu" : useGpu = False
            End Select
            i += 1
        End While

        Console.WriteLine("=== LbmTank /voxeljson 体素 json 回读 + 风洞冒烟测试 ===")

        Dim scene As WindTunnelScene
        Dim sw = System.Diagnostics.Stopwatch.StartNew()

        Try
            scene = WindTunnelVoxelJson.BuildSceneFromJson(jsonPath,
                                                           domainScale:=2.0, groundClearance:=0)
            Console.WriteLine($"  json 加载: {scene.Describe()}   ({sw.Elapsed.TotalSeconds:F1} s)")
        Catch ex As InvalidOperationException
            ' 容量超限等指引性错误：只呈现消息，不打印堆栈
            Console.WriteLine($"加载失败: {ex.Message}")
            Return 1
        End Try

        ' 粘度：ν = U∞·H/Re，取 U∞ = 0.1、Re = 100、H ≈ Ny/2
        Dim freestream = 0.1R
        Dim nu = freestream * System.Math.Max(4, scene.Domain.Height \ 2) / 100.0R

        Dim tunnel = WindTunnelSolver.FromVoxelShape(scene.Domain, freestream, nu,
                                                     groundNoSlip:=True,
                                                     model:=CollisionModel.TRT,
                                                     useGpu:=useGpu)
        Console.WriteLine($"  求解器: 域 {tunnel.Nx}x{tunnel.Ny}x{tunnel.Nz}   流体 {tunnel.FluidCount}   " &
                          $"路径 = {If(tunnel.UseGpu, "CUDA", "CPU")}")

        Call tunnel.InitializeFlow()
        sw.Restart()

        For s = 1 To steps
            tunnel.Advance()
        Next
        sw.Stop()

        Console.WriteLine($"  [{steps} steps]  max|u| = {tunnel.ComputeMaxSpeed():F5}  " &
                          $"Δmass = {tunnel.ComputeMassDrift():E3}  ({sw.Elapsed.TotalMilliseconds / steps:F2} ms/step)")
        Return 0
    End Function

End Module
