' /********************************************************************************/
'
'   GNNWindTunnel.vb
'
'   基于 GNN 图神经网络的 CFD 代理模型 —— 风洞模拟测试（控制台 PASS/FAIL 风格）
'
'   测试流程：
'       1. （可选）注册 CUDA GPU 计算后端，失败自动回退 SIMD CPU
'       2. 生成训练数据：多组参数化球体（半径 × 离地间隙 × 来流速度）
'          运行 CFDEngine 风洞仿真（WindTunnel），采集模式A（稳态场）与
'          模式B（单步时间演化轨迹）训练样本
'       3. 训练两个 GNN 代理模型：
'             模式A SteadyStateSurrogate    —— 几何+来流 → 稳态流场（一次前向）
'             模式B AutoregressiveSurrogate —— 单步演化模型迭代 rollout
'       4. 测试场景：三维风洞中地面上放置一个球体（留出参数，不参与训练），
'          分别用 CFD 与两个 GNN 代理模型计算流场
'       5. 误差对比：速度场 RMSE（相对均匀来流基线的改进）、最大速度、
'          enstrophy、尾流亏损，PASS/FAIL 断言
'       6. 通过 VTIExporter 将 CFD 与 GNN 预测结果导出为 .vti 快照 +
'          animation.pvd，可在 ParaView 中可视化对比
'
'   运行：
'       在 test 项目中：
'           dotnet run -p:StartupObject=test.Program -- --gnn-windtunnel
'       可选参数：--size N --epochs N --steps N --radius N --clearance N
'                 --freestream F --hidden N --no-gpu
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine
Imports Moira.CFDEngine.Snapshot
Imports std = System.Math

Module GNNWindTunnel

    ''' <summary>
    ''' 运行 GNN 风洞代理模型测试。返回 True 表示全部断言通过。
    ''' </summary>
    Public Function RunGNNWindTunnelTest(Optional domainNx As Integer = 20,
                                         Optional domainNy As Integer = 14,
                                         Optional domainNz As Integer = 20,
                                         Optional epochs As Integer = 30,
                                         Optional cfdSteps As Integer = 60,
                                         Optional dt As Double = 0.1,
                                         Optional hiddenDim As Integer = 24,
                                         Optional testRadius As Integer = 4,
                                         Optional testClearance As Integer = 1,
                                         Optional testFreestream As Double = 2.2,
                                         Optional enableGpu As Boolean = True) As Boolean

        Console.WriteLine(New String("="c, 70))
        Console.WriteLine("  GNN 图神经网络 CFD 代理模型 —— 风洞模拟测试")
        Console.WriteLine("  GNN Surrogate for CFD - Wind Tunnel Test (sphere on ground)")
        Console.WriteLine(New String("="c, 70))
        Console.WriteLine()

        Dim allPass As Boolean = True
        Dim wallClock As Stopwatch = Stopwatch.StartNew()

        ' ---- 0. GPU 后端 ----
        If enableGpu Then
            MeshGNNTrainer.TryEnableGpu()
        End If

        ' ---- 1. 生成训练数据（多组参数化球体风洞仿真）----
        Console.WriteLine()
        Console.WriteLine("[1] 生成训练数据：多组参数化球体风洞仿真 ...")
        Console.WriteLine($"    计算域 {domainNx}x{domainNy}x{domainNz}，CFD 步数 {cfdSteps}，dt={dt}")

        Dim configs = GnnDataset.DefaultSweepConfigs(domainNx, domainNy, domainNz)
        Console.WriteLine($"    参数扫描 {configs.Count} 组：半径 x 离地间隙 x 来流速度")

        Dim modeASamples = GnnDataset.CollectModeADataset(configs, settleSteps:=cfdSteps + 20, dt:=dt)
        Console.WriteLine($"    模式A样本：{modeASamples.Count} 个（稳态终场）")

        Dim modeBSamples = GnnDataset.CollectModeBDataset(configs, steps:=cfdSteps, dt:=dt, collectEvery:=3)
        Console.WriteLine($"    模式B样本：{modeBSamples.Count} 个（单步时间演化轨迹）")

        ' ---- 2. 训练模式A代理模型（几何+来流 → 稳态流场）----
        Console.WriteLine()
        Console.WriteLine("[2] 训练模式A代理模型（SteadyStateSurrogate）...")
        Dim adj = CfdGraphData.GetGridAdjacency(domainNx, domainNy, domainNz)
        Dim modelA As New MeshGNN(CfdGraphData.ModeAFeatDim, hiddenDim, 3, numLayers:=3)
        modelA.PrintModelInfo()

        Dim trainerA As New MeshGNNTrainer(modelA, learningRate:=0.005F)
        trainerA.Train(modeASamples, adj, CfdGraphData.ModeAFeatDim, epochs:=epochs, printEvery:=5)

        Dim surrogateA As New SteadyStateSurrogate(modelA)

        ' ---- 3. 训练模式B代理模型（单步时间演化 → rollout）----
        Console.WriteLine()
        Console.WriteLine("[3] 训练模式B代理模型（AutoregressiveSurrogate）...")
        Dim modelB As New MeshGNN(CfdGraphData.ModeBFeatDim, hiddenDim, 3, numLayers:=3)
        modelB.PrintModelInfo()

        Dim trainerB As New MeshGNNTrainer(modelB, learningRate:=0.005F)
        trainerB.Train(modeBSamples, adj, CfdGraphData.ModeBFeatDim, epochs:=epochs, printEvery:=5)

        Dim surrogateB As New AutoregressiveSurrogate(modelB) With {
            .RolloutSteps = cfdSteps,
            .TimeStep = dt
        }

        ' ---- 4. 测试场景：地面上的球体风洞（留出参数，不参与训练）----
        Console.WriteLine()
        Console.WriteLine($"[4] 测试场景：地面上球体 r={testRadius}, 离地={testClearance}, U∞={testFreestream:F2}（留出参数）...")

        Dim testConfig As New SphereConfig(testRadius, testClearance, testFreestream, domainNx, domainNy, domainNz)
        Dim testShape = VoxelSphere.BuildGroundDomain(testConfig)

        ' CFD 基准解
        Dim tunnel = GnnDataset.CreateWindTunnel(testConfig)
        tunnel.InitializeFlow()
        tunnel.Run(cfdSteps + 20, dt)
        Dim cfdField = tunnel.Field

        ' GNN 推理
        Dim swInfer As Stopwatch = Stopwatch.StartNew()
        Dim gnnFieldA = surrogateA.Predict(testShape, testFreestream)
        Dim inferMsA = swInfer.ElapsedMilliseconds
        swInfer.Restart()
        Dim gnnFieldB = surrogateB.Predict(testShape, testFreestream)
        Dim inferMsB = swInfer.ElapsedMilliseconds

        Console.WriteLine($"    CFD 求解 {cfdSteps + 20} 步耗时: {tunnel.StepCount} 步")
        Console.WriteLine($"    GNN 推理耗时: 模式A = {inferMsA} ms（一次前向）, 模式B = {inferMsB} ms（{surrogateB.RolloutSteps} 步 rollout）")

        ' ---- 5. 误差对比与断言 ----
        Console.WriteLine()
        Console.WriteLine("[5] GNN 代理模型 vs CFD 误差对比：")

        ' 均匀来流基线（不做任何学习时的误差）
        Dim baselineField = AutoregressiveSurrogate.CreateUniformFlow(testShape, testFreestream)
        Dim baselineRmse = VelocityRmse(baselineField, cfdField, testFreestream)

        Dim rmseA = VelocityRmse(gnnFieldA, cfdField, testFreestream)
        Dim rmseB = VelocityRmse(gnnFieldB, cfdField, testFreestream)

        Console.WriteLine($"    速度场 RMSE/U∞    : 基线 = {baselineRmse:F4}, 模式A = {rmseA:F4}, 模式B = {rmseB:F4}")
        Console.WriteLine($"    最大速度          : CFD = {MaxSpeed(cfdField):F3}, 模式A = {MaxSpeed(gnnFieldA):F3}, 模式B = {MaxSpeed(gnnFieldB):F3}")
        Console.WriteLine($"    Enstrophy         : CFD = {Enstrophy(cfdField):F2}, 模式A = {Enstrophy(gnnFieldA):F2}, 模式B = {Enstrophy(gnnFieldB):F2}")
        Console.WriteLine($"    尾流亏损 (i=70%)  : CFD = {WakeDeficit(cfdField, testFreestream):F4}, 模式A = {WakeDeficit(gnnFieldA, testFreestream):F4}, 模式B = {WakeDeficit(gnnFieldB, testFreestream):F4}")

        ' 断言：无 NaN
        allPass = Assert(Not HasNaN(gnnFieldA), "模式A预测流场不应包含 NaN/Inf", allPass)
        allPass = Assert(Not HasNaN(gnnFieldB), "模式B预测流场不应包含 NaN/Inf", allPass)

        ' 断言：模式A RMSE 优于均匀来流基线（模型学到了流场结构）
        allPass = Assert(rmseA < baselineRmse,
                         $"模式A RMSE({rmseA:F4}) 应优于均匀来流基线({baselineRmse:F4})", allPass)

        ' 断言：模式A 尾流亏损方向正确（球体阻挡形成尾流）
        allPass = Assert(WakeDeficit(gnnFieldA, testFreestream) > 0,
                         "模式A预测的下游尾流亏损应为正（形成尾流）", allPass)

        ' 断言：模式B rollout 改进不超过基线太多（误差累积放宽 30%）
        allPass = Assert(rmseB < baselineRmse * 1.3,
                         $"模式B rollout RMSE({rmseB:F4}) 应不超过基线的 1.3 倍({baselineRmse * 1.3:F4})", allPass)

        ' ---- 6. 导出 .vti 快照可视化 ----
        Console.WriteLine()
        Console.WriteLine("[6] 导出 .vti 快照（ParaView 可视化）...")
        Dim outDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "gnn-windtunnel")
        System.IO.Directory.CreateDirectory(outDir)

        Dim cfdPath = System.IO.Path.Combine(outDir, "cfd_sphere.vti")
        Dim gnnAPath = System.IO.Path.Combine(outDir, "gnn_steady_sphere.vti")
        Dim gnnBPath = System.IO.Path.Combine(outDir, "gnn_rollout_sphere.vti")

        VTIExporter.Export(cfdField, cfdPath, 0, (cfdSteps + 20) * dt)
        VTIExporter.Export(gnnFieldA, gnnAPath, 0, 0)
        VTIExporter.Export(gnnFieldB, gnnBPath, 0, cfdSteps * dt)
        WritePvd(outDir, New String() {"cfd_sphere.vti", "gnn_steady_sphere.vti", "gnn_rollout_sphere.vti"})

        Console.WriteLine($"    {cfdPath}")
        Console.WriteLine($"    {gnnAPath}")
        Console.WriteLine($"    {gnnBPath}")
        Console.WriteLine($"    {System.IO.Path.Combine(outDir, "animation.pvd")}")

        ' 模型检查点保存（供优化框架复用）
        Dim ckptA = System.IO.Path.Combine(outDir, "gnn-steady-sphere.json")
        Dim ckptB = System.IO.Path.Combine(outDir, "gnn-rollout-sphere.json")
        surrogateA.Save(ckptA)
        surrogateB.Save(ckptB)
        Console.WriteLine($"    模型检查点: {ckptA}")
        Console.WriteLine($"              {ckptB}")

        wallClock.Stop()
        Console.WriteLine()
        Console.WriteLine($"总耗时: {wallClock.Elapsed.TotalSeconds:F1} s")
        Console.WriteLine()

        If allPass Then
            Console.WriteLine(New String("="c, 70))
            Console.WriteLine("  [PASS] GNN 风洞代理模型测试全部通过")
            Console.WriteLine(New String("="c, 70))
        Else
            Console.WriteLine(New String("="c, 70))
            Console.WriteLine("  [FAIL] GNN 风洞代理模型测试存在失败断言")
            Console.WriteLine(New String("="c, 70))
        End If

        Return allPass
    End Function

#Region "评估指标与断言帮助"

    ''' <summary>
    ''' 流体体素上的速度矢量 RMSE（除以来流速度归一化）。
    ''' </summary>
    Private Function VelocityRmse(pred As FluidField, ref As FluidField, freestream As Double) As Double
        Dim n = ref.TotalVoxels
        Dim ny = ref.Ny, nz = ref.Nz
        Dim sum As Double = 0.0
        Dim count As Integer = 0
        Dim pu = pred.U.Data, pv = pred.V.Data, pw = pred.W.Data
        Dim ru = ref.U.Data, rv = ref.V.Data, rw = ref.W.Data

        For t As Integer = 0 To n - 1
            ' 展平索引 → 三维坐标
            Dim i = t \ (ny * nz)
            Dim rem_ = t Mod (ny * nz)
            Dim j = rem_ \ nz
            Dim k = rem_ Mod nz
            If ref.IsActive(i, j, k) Then
                Dim du = pu(t) - ru(t)
                Dim dv = pv(t) - rv(t)
                Dim dw = pw(t) - rw(t)
                sum += du * du + dv * dv + dw * dw
                count += 1
            End If
        Next

        If count = 0 Then Return 0.0
        Return std.Sqrt(sum / count) / freestream
    End Function

    ''' <summary>全场最大速度大小（流体体素）。</summary>
    Private Function MaxSpeed(f As FluidField) As Double
        Dim maxS = 0.0
        Dim n = f.TotalVoxels
        Dim ny = f.Ny, nz = f.Nz
        For t As Integer = 0 To n - 1
            ' 展平索引 → 三维坐标
            Dim i = t \ (ny * nz)
            Dim rem_ = t Mod (ny * nz)
            Dim j = rem_ \ nz
            Dim k = rem_ Mod nz
            If f.IsActive(i, j, k) Then
                Dim u = f.U.Data(t), v = f.V.Data(t), w = f.W.Data(t)
                Dim s = std.Sqrt(u * u + v * v + w * w)
                If s > maxS Then maxS = s
            End If
        Next
        Return maxS
    End Function

    ''' <summary>总涡量 enstrophy = Σ|ω|²（中心差分，内部流体体素）。</summary>
    Private Function Enstrophy(f As FluidField) As Double
        Dim nx = f.Nx, ny = f.Ny, nz = f.Nz
        Dim total As Double = 0.0
        Dim u = f.U.Data, v = f.V.Data, w = f.W.Data

        For i As Integer = 1 To nx - 2
            For j As Integer = 1 To ny - 2
                For k As Integer = 1 To nz - 2
                    If Not f.IsActive(i, j, k) Then Continue For
                    Dim nB = (i * ny + j) * nz + k
                    Dim wx = (w(nB + nz) - w(nB - nz)) * 0.5 - (v(nB + 1) - v(nB - 1)) * 0.5
                    Dim wy = (u(nB + 1) - u(nB - 1)) * 0.5 - (w(nB + ny * nz) - w(nB - ny * nz)) * 0.5
                    Dim wz = (v(nB + ny * nz) - v(nB - ny * nz)) * 0.5 - (u(nB + nz) - u(nB - nz)) * 0.5
                    total += wx * wx + wy * wy + wz * wz
                Next
            Next
        Next
        Return total
    End Function

    ''' <summary>下游平面（i = 70% Nx）尾流速度亏损 = U∞ - mean(U)。</summary>
    Private Function WakeDeficit(f As FluidField, freestream As Double) As Double
        Dim i = CInt(f.Nx * 0.7)
        Dim sumU As Double = 0.0
        Dim count As Integer = 0
        For j As Integer = 0 To f.Ny - 1
            For k As Integer = 0 To f.Nz - 1
                If f.IsActive(i, j, k) Then
                    sumU += f.U.Data((i * f.Ny + j) * f.Nz + k)
                    count += 1
                End If
            Next
        Next
        Return If(count > 0, freestream - sumU / count, 0.0)
    End Function

    ''' <summary>检查流场是否包含 NaN / Inf。</summary>
    Private Function HasNaN(f As FluidField) As Boolean
        For Each tensor In New TensorF() {f.U, f.V, f.W}
            For Each x In tensor.Data
                If Single.IsNaN(x) OrElse Single.IsInfinity(x) Then Return True
            Next
        Next
        Return False
    End Function

    ''' <summary>写出 animation.pvd 索引（ParaView 时间序列入口）。</summary>
    Private Sub WritePvd(outDir As String, files As String())
        Dim sb As New System.Text.StringBuilder()
        sb.AppendLine("<?xml version=""1.0""?>")
        sb.AppendLine("<VTKFile type=""Collection"" version=""0.1"">")
        sb.AppendLine("  <Collection>")
        Dim t As Integer = 0
        For Each f In files
            sb.AppendLine($"    <DataSet part=""{t}"" file=""{f}""/>")
            t += 1
        Next
        sb.AppendLine("  </Collection>")
        sb.AppendLine("</VTKFile>")
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "animation.pvd"), sb.ToString())
    End Sub

    Private Function Assert(condition As Boolean, message As String, allPass As Boolean) As Boolean
        If condition Then
            Console.WriteLine($"    [PASS] {message}")
            Return allPass
        Else
            Console.WriteLine($"    [FAIL] {message}")
            Return False
        End If
    End Function

#End Region

End Module
