''' <summary>
''' 体素拾取事件参数。
''' </summary>
Public Class VoxelPickEventArgs

    ''' <summary>体素引擎索引（i*ny*nz + j*nz + k）。</summary>
    Public Property VoxelIndex As Integer

    Public Property I As Integer
    Public Property J As Integer
    Public Property K As Integer

    ''' <summary>当前标量场在该体素的值。</summary>
    Public Property FieldValue As Double

    Public Property U As Double
    Public Property V As Double
    Public Property W As Double

    ''' <summary>速度模长 |V|。</summary>
    Public Property Speed As Double

    ''' <summary>拾取发生时的帧序号。</summary>
    Public Property FrameIndex As Integer

End Class