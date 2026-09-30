' /********************************************************************************/
'
'   WindTunnelScene.vb
'
'   风洞试验三维场景 —— 模型加载 + 体素化结果的封装，对接 CFDEngine 风洞模拟
'
'   作用：
'       持有 <see cref="WindTunnelSceneBuilder"/> 产出的一切中间结果：
'           - 源模型文件 / 体素化配置（元数据记录，便于日志与测试断言）
'           - CFDEngine 语义的模型原始体素空间 VoxelModel（True = 流体）
'           - 放大定位后的风洞计算域 VoxelShape（X/Z 居中、Y 离地定位）
'       并提供 <see cref="CreateTunnel"/> 直接创建
'       <see cref="WindTunnel"/> 风洞模拟实例，供外部调用者执行风洞仿真：
'
'           scene = WindTunnelSceneBuilder.BuildScene("model.3mf", ...)
'           tunnel = scene.CreateTunnel(freestream:=3.0)
'           tunnel.InitializeFlow()
'           tunnel.Run(steps, dt)
'
' /********************************************************************************/

''' <summary>
''' 风洞试验三维场景 —— 模型体素化 + 计算域放大定位结果的封装。
''' </summary>
Public Class WindTunnelScene

#Region "元数据"

    ''' <summary>源模型文件路径。</summary>
    Public ReadOnly Property SourceFile As String

    ''' <summary>体素化分辨率（最长边上的体素数量）。</summary>
    Public ReadOnly Property Resolution As Integer

    ''' <summary>实际使用的体素化器实现。</summary>
    Public ReadOnly Property Voxelizer As VoxelizerKind

    ''' <summary>计算空间相对模型 grid 的放大倍数。</summary>
    Public ReadOnly Property DomainScale As Double

    ''' <summary>模型最低固体体素离地面 (j=0) 的高度。</summary>
    Public ReadOnly Property GroundClearance As Integer

#End Region

#Region "体素数据"

    ''' <summary>
    ''' 模型原始尺寸下的体素空间（CFDEngine 语义：True = 流体，False = 固体障碍）。
    ''' </summary>
    Public ReadOnly Property VoxelModel As VoxelModel

    ''' <summary>
    ''' 放大定位后的风洞计算域（True = 活动流体，False = 固体障碍）。
    ''' 尺寸 = 模型维度 × DomainScale；模型 X/Z 居中，
    ''' 最低固体体素位于 GroundClearance 高度。
    ''' </summary>
    Public ReadOnly Property Domain As VoxelShape

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建风洞场景（一般由 <see cref="WindTunnelSceneBuilder.BuildScene"/> 调用）。
    ''' </summary>
    Public Sub New(sourceFile As String,
                   resolution As Integer,
                   voxelizer As VoxelizerKind,
                   model As VoxelModel,
                   domain As VoxelShape,
                   domainScale As Double,
                   groundClearance As Integer)

        If model Is Nothing Then Throw New ArgumentNullException(NameOf(model))
        If domain Is Nothing Then Throw New ArgumentNullException(NameOf(domain))

        Me.SourceFile = sourceFile
        Me.Resolution = resolution
        Me.Voxelizer = voxelizer
        Me.VoxelModel = model
        Me.Domain = domain
        Me.DomainScale = domainScale
        Me.GroundClearance = groundClearance

    End Sub

#End Region

#Region "创建风洞模拟"

    ''' <summary>
    ''' 用本场景的计算域创建风洞模拟实例。
    ''' </summary>
    ''' <param name="freestream">来流速度 U∞（网格单位 / 时间，沿 +X）</param>
    ''' <param name="viscosity">运动粘度 ν</param>
    ''' <param name="groundNoSlip">底面地壁是否无滑移（默认 False，自由滑移）</param>
    Public Function CreateTunnel(Optional freestream As Double = 2.0,
                                 Optional viscosity As Double = 0.0005,
                                 Optional groundNoSlip As Boolean = False) As WindTunnel

        Dim tunnel As New WindTunnel(Domain, freestream, viscosity)
        tunnel.DomainScale = DomainScale
        tunnel.GroundClearance = GroundClearance
        tunnel.GroundNoSlip = groundNoSlip
        Return tunnel

    End Function

#End Region

#Region "摘要"

    ''' <summary>
    ''' 场景摘要信息（模型维度 / 固体数 / 计算域维度）。
    ''' </summary>
    Public Function Describe() As String
        Dim m = VoxelModel
        Dim d = Domain
        Return $"WindTunnelScene[{SourceFile}] " &
               $"model={m.Width}x{m.Height}x{m.Depth} solid={m.SolidVoxelCount} " &
               $"domain={d.Nx}x{d.Ny}x{d.Nz} " &
               $"scale={DomainScale} clearance={GroundClearance} voxelizer={Voxelizer}"
    End Function

    Public Overrides Function ToString() As String
        Return Describe()
    End Function

#End Region

End Class
