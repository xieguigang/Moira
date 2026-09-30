' /********************************************************************************/
'
'   ZMeshWindTunnelTest.vb
'
'   基于 ZMesh 风洞初始化工具的风洞测试流程（控制台 PASS / FAIL 风格）
'
'   测试内容：
'       1. 通过 ZMesh (Moira.ZMesh) 加载 3D 模型文件（默认 airplane1.3mf）
'          并体素化，断言模型维度 > 0、固体体素数 > 0。
'       2. BuildScene 构建风洞场景（放大计算空间 + 离地定位），
'          断言放大空间维度 = 模型维度 × domainScale（向上取整），
'          断言模型最低固体体素 j = groundClearance、固体体素数无丢失。
'       3. CreateTunnel 创建风洞模拟，初始化水平向右 (+X) 来流并运行若干时间步，
'          每隔 interval 步通过 VtiSnapshotRecorder 导出 .vti 帧文件
'          （含 animation.pvd，供 ParaView / VTK.js 可视化）。
'       4. 计算并打印湍流 / 物理指标（最大 / 平均速度、enstrophy、尾流亏损），
'          断言无 NaN、enstrophy > 0、下游尾流亏损 > 0。
'
'   运行：
'       在 test 项目中：
'           dotnet run -- --zmesh-windtunnel [模型路径] [--resolution N] [--scale N]
'                        [--clearance N] [--steps N] [--freestream F]
'                        [--voxelizer standard|sdf]
'
' /********************************************************************************/

Imports Moira.CFDEngine
Imports Moira.CFDEngine.Snapshot
Imports Moira.ZMesh
Imports std = System.Math

Module ZMeshWindTunnelTest

    ''' <summary>
    ''' 运行基于 ZMesh 的风洞测试流程。返回 True 表示全部断言通过。
    ''' </summary>
    ''' <param name="modelPath">
    ''' 3D 模型文件路径（STL/GLTF/GLB/OBJ/DAE/3DS/3MF）；
    ''' 为空时默认在输出目录查找 airplane1.3mf。
    ''' </param>
    ''' <param name="resolution">体素化分辨率（最长边体素数量，默认 32）</param>
    ''' <param name="voxelizer">体素化器实现（默认标准 Voxelizer）</param>
    ''' <param name="domainScale">计算空间放大倍数（默认 2）</param>
    ''' <param name="groundClearance">模型离地高度（默认 0，贴地）</param>
    ''' <param name="freestream">来流速度 U∞（默认 3.0）</param>
    ''' <param name="steps">时间步数（默认 40）</param>
    ''' <param name="vtiInterval">VTI 快照采样间隔（默认 10，每隔多少步存一帧）</param>
    ''' <param name="enableGpu">
    ''' 是否启用 CUDA GPU 加速（默认 True）。注册失败（无 NVIDIA 设备 / 驱动不匹配）
    ''' 时自动回退 SIMD CPU，测试继续执行。
    ''' </param>
    Public Function RunZMeshWindTunnelTest(Optional modelPath As String = Nothing,
                                           Optional resolution As Integer = 32,
                                           Optional voxelizer As VoxelizerKind = VoxelizerKind.Standard,
                                           Optional domainScale As Double = 2.0,
                                           Optional groundClearance As Integer = 0,
                                           Optional freestream As Double = 3.0,
                                           Optional steps As Integer = 40,
                                           Optional vtiInterval As Integer = 10,
                                           Optional enableGpu As Boolean = True) As Boolean

        Console.WriteLine(New String("="c, 70))
        Console.WriteLine("  ZMesh 风洞试验初始化工具 —— 模型加载 / 体素化 / 场景构建 / 仿真 / VTI 导出")
        Console.WriteLine("  ZMesh Wind Tunnel - Load / Voxelize / Scene Build / Simulate / VTI Export")
        Console.WriteLine(New String("="c, 70))
        Console.WriteLine()

        Dim allPass As Boolean = True

        ' ---- 解析模型路径 ----
        If String.IsNullOrEmpty(modelPath) Then
            modelPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "airplane1.3mf")
        End If
        Console.WriteLine($"[模型] {modelPath}")
        Console.WriteLine($"[配置] resolution={resolution}, voxelizer={voxelizer}, " &
                          $"domainScale={domainScale}, groundClearance={groundClearance}, " &
                          $"freestream={freestream}, steps={steps}, enableGpu={enableGpu}")

        If Not System.IO.File.Exists(modelPath) Then
            Console.WriteLine($"[FAIL] 找不到模型文件：{modelPath}")
            Return False
        End If

        ' ---- 0. CUDA GPU 后端 ----
        Dim gpuReady As Boolean = False
        If enableGpu Then
            Console.WriteLine()
            Console.WriteLine("[0] 注册 CUDA GPU 计算后端 ...")
            gpuReady = CudaTensorF.TryRegister()
            If gpuReady Then
                Console.WriteLine($"    [OK] CUDA 后端注册成功（{CudaTensorF.Current.Name}），")
                Console.WriteLine($"         风洞求解器的隐式扩散 / 压力泊松 / 半拉格朗日平流将走 GPU 加速")
            Else
                Console.WriteLine($"    [..] CUDA 后端不可用（{CudaTensorF.LastError}），回退 SIMD CPU")
            End If
        End If

        ' ---- 1. ZMesh：加载模型并体素化 ----
        Console.WriteLine()
        Console.WriteLine("[1] ZMesh 加载 3D 模型并体素化 ...")
        Dim model As VoxelModel
        Try
            model = WindTunnelSceneBuilder.FromModelFile(modelPath, resolution, voxelizer)
        Catch ex As Exception
            Console.WriteLine($"[FAIL] 模型加载 / 体素化异常：{ex.ToString}")
            Return False
        End Try

        Dim b = model.SolidBounds
        Console.WriteLine($"    来源模型   : {model.SourceModel}")
        Console.WriteLine($"    模型维度   : {model.Width}×{model.Height}×{model.Depth}")
        Console.WriteLine($"    固体体素数 : {model.SolidVoxelCount}")
        Console.WriteLine($"    固体包围盒 : X[{b.minX}..{b.maxX}] Y[{b.minY}..{b.maxY}] Z[{b.minZ}..{b.maxZ}]")
        If model.VoxelSize IsNot Nothing Then
            Console.WriteLine($"    体素尺寸   : ({model.VoxelSize(0):G6}, {model.VoxelSize(1):G6}, {model.VoxelSize(2):G6})")
        End If

        allPass = Assert(model.Width > 0 AndAlso model.Height > 0 AndAlso model.Depth > 0,
                         "模型体素维度应 > 0", allPass)
        allPass = Assert(model.SolidVoxelCount > 0,
                         "固体体素数应 > 0（语义反转：Landscape 固体 → 引擎障碍）", allPass)

        ' ---- 2. ZMesh：构建风洞场景（放大空间 + 离地定位）----
        Console.WriteLine()
        Console.WriteLine("[2] ZMesh BuildScene 构建风洞场景 ...")
        Dim scene As WindTunnelScene
        Try
            scene = WindTunnelSceneBuilder.BuildScene(modelPath, resolution, voxelizer,
                                                      domainScale:=domainScale,
                                                      groundClearance:=groundClearance)
        Catch ex As Exception
            Console.WriteLine($"[FAIL] 场景构建异常：{ex.Message}")
            Return False
        End Try
        Console.WriteLine($"    {scene.Describe()}")

        Dim domain = scene.Domain
        Dim expNx = std.Max(model.Width, CInt(std.Ceiling(model.Width * domainScale)))
        Dim expNy = std.Max(model.Height, CInt(std.Ceiling(model.Height * domainScale)))
        Dim expNz = std.Max(model.Depth, CInt(std.Ceiling(model.Depth * domainScale)))
        allPass = Assert(domain.Width = expNx AndAlso domain.Height = expNy AndAlso domain.Depth = expNz,
                         $"放大计算域维度应为 {expNx}×{expNy}×{expNz}", allPass)

        Dim lowestSolidJ = FindLowestSolidJ(domain)
        Console.WriteLine($"    计算域中模型最低固体体素 j = {lowestSolidJ}（期望 {groundClearance}）")
        allPass = Assert(lowestSolidJ = groundClearance,
                         $"模型最低固体体素 j 应等于离地高度 {groundClearance}", allPass)

        Dim domainSolid = CountSolid(domain)
        allPass = Assert(domainSolid = model.SolidVoxelCount,
                         $"计算域固体体素数({domainSolid})应等于模型固体数({model.SolidVoxelCount})", allPass)

        ' ---- 3. 创建风洞并运行，逐帧导出 VTI ----
        Console.WriteLine()
        Console.WriteLine("[3] 创建风洞模拟并运行 ...")
        Dim dt As Double = 0.1
        Dim tunnel = scene.CreateTunnel(freestream:=freestream, viscosity:=0.0005)

        ' 启用求解器的 CUDA 热算子路由（未注册成功时保持 CPU 路径）
        tunnel.Solver.UseCudaBackend = gpuReady

        Dim framesDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "frames_zmesh")
        Dim recorder As New VtiSnapshotRecorder(framesDir, baseName:="zmesh_windtunnel",
                                                interval:=vtiInterval,
                                                estimatedFrames:=steps \ vtiInterval + 1)
        Console.WriteLine($"    计算域维度  : {tunnel.Field.Nx}×{tunnel.Field.Ny}×{tunnel.Field.Nz}")
        Console.WriteLine($"    来流速度 U∞ : {tunnel.FreestreamVelocity}")
        Console.WriteLine($"    VTI 快照    : 每 {vtiInterval} 步一帧 → {framesDir}")

        tunnel.InitializeFlow()

        Dim startTime = DateTime.Now
        tunnel.Run(steps, dt,
                   Sub(stepIdx, t)
                       recorder.Capture(tunnel.Field, stepIdx, t)
                       If stepIdx Mod 10 = 0 OrElse stepIdx = steps Then
                           Console.WriteLine($"    步 {stepIdx,3}/{steps}  时间={t:F2}  " &
                                             $"最大速度={tunnel.ComputeMaxSpeed():F3}")
                       End If
                   End Sub)
        recorder.Finish()
        Dim elapsed = (DateTime.Now - startTime).TotalSeconds
        Console.WriteLine($"    完成，耗时 {elapsed:F2} 秒")
        Console.WriteLine($"    动画集合: {System.IO.Path.Combine(framesDir, "animation.pvd")}")

        ' 断言 vti 帧文件已生成
        Dim vtiFrames = System.IO.Directory.GetFiles(framesDir, "*.vti")
        allPass = Assert(vtiFrames.Length > 0, $"VTI 帧文件应已生成（实际 {vtiFrames.Length} 帧）", allPass)

        ' ---- 4. 计算并断言湍流 / 物理指标 ----
        Console.WriteLine()
        Console.WriteLine("[4] 湍流 / 物理结果指标：")
        Dim f = tunnel.Field
        Dim maxSpeed = tunnel.ComputeMaxSpeed()
        Dim avgSpeed = tunnel.ComputeAverageSpeed()
        Dim enstrophy = tunnel.ComputeEnstrophy()
        ' 下游平面：取模型固体包围盒下游若干格处（放大空间坐标）
        Dim downstreamI = CInt(std.Min(f.Nx - 2, (f.Nx \ 2) + model.Width \ 2 + 2))
        Dim wakeDeficit = tunnel.ComputeWakeDeficit(downstreamI)

        Console.WriteLine($"    最大速度       : {maxSpeed:F4}")
        Console.WriteLine($"    平均速度       : {avgSpeed:F4}")
        Console.WriteLine($"    总涡量 enstrophy: {enstrophy:F4}")
        Console.WriteLine($"    下游尾流亏损(i={downstreamI}): {wakeDeficit:F4}  (= U∞ - 平面平均U)")

        allPass = Assert(Not Double.IsNaN(maxSpeed) AndAlso Not Double.IsInfinity(maxSpeed),
                         "最大速度应为有限值（无 NaN / Inf）", allPass)
        allPass = Assert(Not Double.IsNaN(enstrophy) AndAlso Not Double.IsInfinity(enstrophy),
                         "enstrophy 应为有限值（无 NaN / Inf）", allPass)
        allPass = Assert(enstrophy > 0.0, "enstrophy 应 > 0（流场中形成涡旋 / 湍流结构）", allPass)
        allPass = Assert(maxSpeed > tunnel.FreestreamVelocity * 0.5,
                         "最大速度应达到来流量级（来流有效建立）", allPass)
        allPass = Assert(wakeDeficit > 0.0, "下游尾流亏损应 > 0（模型阻挡形成尾流）", allPass)

        ' ---- 5. 打印中截面速度大小切片（直观观察尾流）----
        Console.WriteLine()
        Console.WriteLine($"[5] 中截面 (k={f.Nz \ 2}) 速度大小切片（左入流 → 右出流，模型处为空洞）：")
        PrintSpeedSlice(tunnel, f.Nz \ 2)

        ' ---- 结论 ----
        Console.WriteLine()
        Console.WriteLine(New String("="c, 70))
        If allPass Then
            Console.WriteLine("  结果：全部断言通过 [PASS]")
        Else
            Console.WriteLine("  结果：存在失败断言 [FAIL]")
        End If
        Console.WriteLine(New String("="c, 70))

        Return allPass

    End Function

#Region "断言与辅助"

    ''' <summary>打印单条断言结果并累积总体状态。</summary>
    Private Function Assert(condition As Boolean, message As String, prev As Boolean) As Boolean
        Console.WriteLine($"    [{If(condition, "PASS", "FAIL")}] {message}")
        Return prev AndAlso condition
    End Function

    ''' <summary>在计算域中查找模型最低固体体素的 j 坐标；无固体返回 -1。</summary>
    Private Function FindLowestSolidJ(shape As VoxelShape) As Integer
        For j = 0 To shape.Height - 1
            For i = 0 To shape.Width - 1
                For k = 0 To shape.Depth - 1
                    If Not shape.IsActive(i, j, k) Then
                        Return j
                    End If
                Next
            Next
        Next
        Return -1
    End Function

    ''' <summary>统计计算域中的固体（非活动）体素数。</summary>
    Private Function CountSolid(shape As VoxelShape) As Integer
        Dim count = 0
        For i = 0 To shape.Width - 1
            For j = 0 To shape.Height - 1
                For k = 0 To shape.Depth - 1
                    If Not shape.IsActive(i, j, k) Then count += 1
                Next
            Next
        Next
        Return count
    End Function

    ''' <summary>打印指定 k 平面的速度大小切片（i 横向，j 纵向）。</summary>
    Private Sub PrintSpeedSlice(tunnel As WindTunnel, k As Integer)
        Dim f = tunnel.Field
        Dim chars = " .:-=+*#%@".ToCharArray()
        Dim maxS = tunnel.ComputeMaxSpeed()
        If maxS <= 0.000001 Then maxS = 0.000001
        For j = f.Ny - 1 To 0 Step -1
            Console.Write("    ")
            For i = 0 To f.Nx - 1
                If Not f.IsActive(i, j, k) Then
                    Console.Write("X"c)   ' 固体（模型本体）
                Else
                    Dim u = f.U(i, j, k), v = f.V(i, j, k), w = f.W(i, j, k)
                    Dim spd = std.Sqrt(u * u + v * v + w * w)
                    Dim normalized = spd / maxS
                    Dim idx = CInt(std.Min(chars.Length - 1, std.Max(0, normalized * (chars.Length - 1))))
                    Console.Write(chars(idx))
                End If
            Next
            Console.WriteLine()
        Next
    End Sub

#End Region

End Module
