Imports Moira.CDFDxCanvas.Rendering

' /********************************************************************************/
'
'   WindTunnelFlowField.vb
'
'   IFlowFieldData 的实时风洞适配器 —— 把"笛卡尔重采样后的 FVM 解"
'   （EulerianFieldSnapshot 的 Ux/Uy/Uz 数组 + 域外掩膜）包装成速度场只读视图。
'
'   坐标约定（与 LBM 时代一致，保持不变）：
'       渲染使用**结构格索引空间**（世界坐标 = 格点下标），因此
'       Spacing = 1、CenterOffset = 0。
'       重采样格是等间距的（EulerianToCartesianResampler.AutoConfigure 用单一
'       步长 h），所以索引空间与真实几何只差一个统一缩放，不影响观感；
'       这样做的好处是点云 stride 降采样 / 质心补偿 / 截面判据完全不用改。
'       矢量图 Pass 需要"质心补偿空间"（世界坐标 + (ox, oy, oz)）时，
'       由调用方把补偿量写进 Origin 实现（见 WindTunnelCanvas.RefreshField）。
'
' /********************************************************************************/

''' <summary>
''' 实时风洞速度场适配器（域内格点参与矢量渲染，域内格点均可作流线种子）。
''' </summary>
Friend Class WindTunnelFlowField : Implements IFlowFieldData

    ReadOnly m_ux As Single()
    ReadOnly m_uy As Single()
    ReadOnly m_uz As Single()

    ''' <summary>域外掩膜（1 = 域外，0 = 流体）。Nothing 时不做过滤，全部视为流体。</summary>
    ReadOnly m_outside As Byte()

    ReadOnly m_nx As Integer
    ReadOnly m_ny As Integer
    ReadOnly m_nz As Integer
    ReadOnly m_ox As Single
    ReadOnly m_oy As Single
    ReadOnly m_oz As Single

    ''' <param name="outside">域外掩膜（Nothing 时不做类型过滤，全部视为流体）。</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   ux As Single(), uy As Single(), uz As Single(),
                   outside As Byte(),
                   Optional originX As Single = 0.0F,
                   Optional originY As Single = 0.0F,
                   Optional originZ As Single = 0.0F)

        m_nx = nx : m_ny = ny : m_nz = nz
        m_ux = ux : m_uy = uy : m_uz = uz
        m_outside = outside
        m_ox = originX : m_oy = originY : m_oz = originZ
    End Sub

    Public ReadOnly Property Nx As Integer Implements IFlowFieldData.Nx
        Get
            Return m_nx
        End Get
    End Property

    Public ReadOnly Property Ny As Integer Implements IFlowFieldData.Ny
        Get
            Return m_ny
        End Get
    End Property

    Public ReadOnly Property Nz As Integer Implements IFlowFieldData.Nz
        Get
            Return m_nz
        End Get
    End Property

    Public ReadOnly Property OriginX As Single Implements IFlowFieldData.OriginX
        Get
            Return m_ox
        End Get
    End Property

    Public ReadOnly Property OriginY As Single Implements IFlowFieldData.OriginY
        Get
            Return m_oy
        End Get
    End Property

    Public ReadOnly Property OriginZ As Single Implements IFlowFieldData.OriginZ
        Get
            Return m_oz
        End Get
    End Property

    ''' <summary>结构格索引空间：格距恒为 1。</summary>
    Public ReadOnly Property SpacingX As Single Implements IFlowFieldData.SpacingX
        Get
            Return 1.0F
        End Get
    End Property

    Public ReadOnly Property SpacingY As Single Implements IFlowFieldData.SpacingY
        Get
            Return 1.0F
        End Get
    End Property

    Public ReadOnly Property SpacingZ As Single Implements IFlowFieldData.SpacingZ
        Get
            Return 1.0F
        End Get
    End Property

    ''' <summary>结构格索引空间：格点即中心，偏移 0。</summary>
    Public ReadOnly Property CenterOffset As Single Implements IFlowFieldData.CenterOffset
        Get
            Return 0.0F
        End Get
    End Property

    Public Function UxAt(idx As Integer) As Single Implements IFlowFieldData.UxAt
        Return m_ux(idx)
    End Function

    Public Function UyAt(idx As Integer) As Single Implements IFlowFieldData.UyAt
        Return m_uy(idx)
    End Function

    Public Function UzAt(idx As Integer) As Single Implements IFlowFieldData.UzAt
        Return m_uz(idx)
    End Function

    ''' <summary>仅流体（域内）格点参与矢量渲染。</summary>
    Public Function IsActive(idx As Integer) As Boolean Implements IFlowFieldData.IsActive
        If m_outside Is Nothing Then Return True
        Return m_outside(idx) = 0
    End Function

    ''' <summary>
    ''' 域内格点均可作流线种子。
    ''' （LBM 时代区分 inlet 格点；Eulerian 网格的入口是 patch 而非单元，
    '''   重采样后靠近入口的格点同样是普通流体格点，故与 IsActive 一致。）
    ''' </summary>
    Public Function IsSeedable(idx As Integer) As Boolean Implements IFlowFieldData.IsSeedable
        If m_outside Is Nothing Then Return True
        Return m_outside(idx) = 0
    End Function

End Class
