' /********************************************************************************/
'
'   GnnDataset.vb
'
'   GNN 训练数据集生成器 —— 多组参数化球体风洞仿真采集
'
'   数据来源：
'       用 VoxelSphere 构建不同 (球体半径, 离地间隙, 来流速度) 的风洞
'       计算域，运行 CFDEngine 的 Stable Fluids 求解器（WindTunnel），
'       从流场演化过程中采集 GNN 训练样本：
'           模式A：每个配置采集 1 个样本（稳态终场）——
'                  特征 = 几何 + 来流，标签 = 稳态速度场。
'           模式B：沿时间轨迹采集单步演化样本对 ——
'                  特征 = t 时刻流场状态，标签 = t+1 时刻速度场。
'
' /********************************************************************************/

Imports Moira.CFDEngine
Imports std = System.Math

''' <summary>
''' 多球体参数扫描 GNN 数据集生成器。
''' </summary>
Public Class GnnDataset

    ''' <summary>CFD 仿真运动粘度 ν（与 WindTunnelTest 保持一致的量级）。</summary>
    Public Shared Property Viscosity As Double = 0.0005

    ''' <summary>
    ''' 默认参数扫描配置：
    ''' 半径 {3,4,5} × 离地间隙 {0,2} × 来流速度 {2.0, 2.5}，共 12 组。
    ''' </summary>
    Public Shared Function DefaultSweepConfigs(Optional nx As Integer = 20,
                                               Optional ny As Integer = 14,
                                               Optional nz As Integer = 20) As List(Of SphereConfig)
        ' 自动适配域高度：确保最大半径(5) + 最大离地间隙(2) 的球体能放下
        Dim requiredNy As Integer = 2 + 2 * 5 + 1
        If ny < requiredNy Then ny = requiredNy

        Dim configs As New List(Of SphereConfig)

        For Each r In New Integer() {3, 4, 5}
            For Each c In New Integer() {0, 2}
                For Each u In New Double() {2.0, 2.5}
                    configs.Add(New SphereConfig(r, c, u, nx, ny, nz))
                Next
            Next
        Next

        Return configs
    End Function

    ''' <summary>
    ''' 按配置创建风洞模拟（含球体计算域）。
    ''' </summary>
    Public Shared Function CreateWindTunnel(config As SphereConfig) As WindTunnel
        Dim shape = VoxelSphere.BuildGroundDomain(config)
        Return New WindTunnel(shape, config.Freestream, Viscosity)
    End Function

    ''' <summary>
    ''' 采集模式A样本：运行 CFD 至稳态，取终场。
    ''' 特征 = 几何 + 来流条件（5 维），标签 = 稳态归一化速度场。
    ''' </summary>
    ''' <param name="config">球体场景配置</param>
    ''' <param name="settleSteps">达到稳态的时间步数</param>
    ''' <param name="dt">时间步长</param>
    Public Shared Function CollectModeASample(config As SphereConfig,
                                              Optional settleSteps As Integer = 80,
                                              Optional dt As Double = 0.1) As GnnSample
        Dim shape = VoxelSphere.BuildGroundDomain(config)
        Dim tunnel = CreateWindTunnel(config)
        tunnel.InitializeFlow()
        tunnel.Run(settleSteps, dt)

        Dim feats = CfdGraphData.BuildModeAFeatures(shape, config.Freestream)
        Dim labels = CfdGraphData.ExtractLabels(tunnel.Field, config.Freestream)

        Dim sample As New GnnSample(feats, labels, $"A|{config}") With {
            .FluidMask = BuildFluidMask(shape)
        }
        Return sample
    End Function

    ''' <summary>构建流体体素掩膜（True = 流体）。</summary>
    Public Shared Function BuildFluidMask(shape As VoxelShape) As Boolean()
        Dim n As Integer = shape.TotalActive
        Dim mask(shape.Width * shape.Height * shape.Depth - 1) As Boolean
        For t As Integer = 0 To mask.Length - 1
            mask(t) = shape.IsActive(t)
        Next
        Return mask
    End Function

    ''' <summary>
    ''' 采集模式B样本：沿 CFD 时间轨迹采集单步演化样本对。
    ''' 特征 = t 时刻流场状态（7 维），标签 = t+dt 时刻归一化速度场。
    ''' </summary>
    ''' <param name="config">球体场景配置</param>
    ''' <param name="steps">总仿真步数</param>
    ''' <param name="dt">时间步长</param>
    ''' <param name="collectEvery">每隔多少步采集一个样本（1 = 每步都采）</param>
    Public Shared Function CollectModeBSamples(config As SphereConfig,
                                               Optional steps As Integer = 60,
                                               Optional dt As Double = 0.1,
                                               Optional collectEvery As Integer = 3) As List(Of GnnSample)
        Dim shape = VoxelSphere.BuildGroundDomain(config)
        Dim tunnel = CreateWindTunnel(config)
        tunnel.InitializeFlow()

        Dim samples As New List(Of GnnSample)
        Dim fluidMask = BuildFluidMask(shape)

        For s As Integer = 1 To steps
            ' t 时刻特征（步进前捕捉）+ 场量快照（计算增量标签用）
            Dim feats = CfdGraphData.BuildModeBFeatures(tunnel.Field, config.Freestream)
            Dim bu = CType(tunnel.Field.U.Data.Clone(), Single())
            Dim bv = CType(tunnel.Field.V.Data.Clone(), Single())
            Dim bw = CType(tunnel.Field.W.Data.Clone(), Single())

            ' 单步演化
            tunnel.StepForward(dt)

            ' t+dt 时刻标签（残差式：Δv = v_after - v_before）
            If s Mod collectEvery = 0 Then
                Dim labels = CfdGraphData.ExtractDeltaLabels(bu, bv, bw, tunnel.Field, config.Freestream)
                Dim sample As New GnnSample(feats, labels, $"B|{config}|step{s}") With {
                    .FluidMask = fluidMask
                }
                samples.Add(sample)
            End If
        Next

        Return samples
    End Function

    ''' <summary>
    ''' 批量采集模式A数据集（每个配置一个稳态样本）。
    ''' </summary>
    Public Shared Function CollectModeADataset(configs As IEnumerable(Of SphereConfig),
                                               Optional settleSteps As Integer = 80,
                                               Optional dt As Double = 0.1) As List(Of GnnSample)
        Dim samples As New List(Of GnnSample)
        For Each cfg In configs
            If Not ConfigFits(cfg) Then
                Console.WriteLine($"    [数据] 跳过 {cfg}（计算域高度不足）")
                Continue For
            End If
            Console.WriteLine($"    [数据] 模式A采集 {cfg} ...")
            samples.Add(CollectModeASample(cfg, settleSteps, dt))
        Next
        Return samples
    End Function

    ''' <summary>
    ''' 批量采集模式B数据集（每个配置沿轨迹采集多个单步演化样本）。
    ''' </summary>
    Public Shared Function CollectModeBDataset(configs As IEnumerable(Of SphereConfig),
                                               Optional steps As Integer = 60,
                                               Optional dt As Double = 0.1,
                                               Optional collectEvery As Integer = 3) As List(Of GnnSample)
        Dim samples As New List(Of GnnSample)
        For Each cfg In configs
            If Not ConfigFits(cfg) Then
                Console.WriteLine($"    [数据] 跳过 {cfg}（计算域高度不足）")
                Continue For
            End If
            Console.WriteLine($"    [数据] 模式B采集 {cfg} ...")
            samples.AddRange(CollectModeBSamples(cfg, steps, dt, collectEvery))
        Next
        Return samples
    End Function

    ''' <summary>检查球体配置能否放进其声明的计算域。</summary>
    Public Shared Function ConfigFits(config As SphereConfig) As Boolean
        Return config.DomainNy >= config.GroundClearance + 2 * config.Radius + 1
    End Function

End Class
