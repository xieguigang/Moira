Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   VoxelShape.vb
'
'   体素几何掩膜 —— 圆柱罐壁 / 挡板 / Rushton 桨叶盘 / 气体分布环的体素化。
'
'   索引布局与 FluidField / Tensor 引擎布局一致：
'       idx = i * (Ny * Nz) + j * Nz + k      （i:x, j:y, k:z 自下而上）
'
' /********************************************************************************/

    ''' <summary>
    ''' 三维体素几何：标记哪些体素是活动流体单元（罐内液体），
    ''' 哪些是固体结构（罐壁 / 挡板 / 搅拌桨 / 桨盘轴）。
    ''' </summary>
    Public Class VoxelShape

        Public ReadOnly Property Nx As Integer
        Public ReadOnly Property Ny As Integer
        Public ReadOnly Property Nz As Integer

        Private ReadOnly _active As Boolean()

        ''' <summary>活动（液体）体素的扁平掩膜，引擎布局长度 Nx*Ny*Nz。</summary>
        Public ReadOnly Property Shape As Boolean()
            Get
                Return _active
            End Get
        End Property

        ''' <summary>固体结构体素掩膜（挡板/桨叶/轴，非罐外区域）。</summary>
        Public ReadOnly Property Solids As Boolean()

        ''' <summary>活动体素总数。</summary>
        Public ReadOnly Property TotalActive As Integer

        ''' <summary>桨盘作用区掩膜（MRF 源项加载区，引擎布局）。</summary>
        Public ReadOnly Property ImpellerZone As Boolean()

        ''' <summary>气体分布环掩膜（气相入口单元）。</summary>
        Public ReadOnly Property SpargerZone As Boolean()

        Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                       active As Boolean(), solids As Boolean(),
                       impellerZone As Boolean(), spargerZone As Boolean())
            Me.Nx = nx
            Me.Ny = ny
            Me.Nz = nz
            _active = active
            Me.Solids = solids
            Me.ImpellerZone = impellerZone
            Me.SpargerZone = spargerZone

            Dim n As Integer = 0
            For Each b In active
                If b Then n += 1
            Next
            TotalActive = n
        End Sub

        ''' <summary>体素 (i,j,k) 是否为活动流体单元。</summary>
        Public Function IsActive(i As Integer, j As Integer, k As Integer) As Boolean
            If i < 0 OrElse i >= Nx OrElse j < 0 OrElse j >= Ny OrElse k < 0 OrElse k >= Nz Then
                Return False
            End If
            Return _active(i * (Ny * Nz) + j * Nz + k)
        End Function

        ''' <summary>体素 (i,j,k) 是否为固体结构。</summary>
        Public Function IsSolid(i As Integer, j As Integer, k As Integer) As Boolean
            If i < 0 OrElse i >= Nx OrElse j < 0 OrElse j >= Ny OrElse k < 0 OrElse k >= Nz Then
                Return False
            End If
            Return Solids(i * (Ny * Nz) + j * Nz + k)
        End Function

    End Class

