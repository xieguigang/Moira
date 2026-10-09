Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   FluidField.vb
'
'   流体场 —— 全部场变量以用户的 Tensor 对象承载。
'
'   设计说明：
'       - U/V/W/Pressure/Density 为 Tensor（shape = {Nx,Ny,Nz}），
'         布局 idx = i*(Ny*Nz) + j*Nz + k 与 Tensor.Item(i,j,k) 一致，
'         Snapshot 系统（VTIExporter / VtiSnapshotRecorder）按
'         field.U.Data(idx) 与 field.U(i,j,k) 两种形式消费。
'       - 算术运算在引擎内通过 Math 模块（Tensor.computeKernel 路由）
'         进行，未来把 computeKernel 切换到 CUDA 后端即可整体加速。
'       - 扩展场（湍流/两相/传质）通过 ExtraScalars 暴露，
'         由发酵罐专用 VTI 记录器一并导出。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports tfMath = Microsoft.VisualBasic.MachineLearning.TensorFlow.Math

''' <summary>
''' 发酵罐流体场：速度（U/V/W）、压力（Pressure）、密度（Density）
''' 五个标准场 + 湍流/两相/传质扩展场，全部为 Tensor 对象。
''' </summary>
Public Class FluidField

    Public ReadOnly Property Nx As Integer
    Public ReadOnly Property Ny As Integer
    Public ReadOnly Property Nz As Integer

    ''' <summary>几何掩膜；Nothing 时视为全场活动。</summary>
    Public ReadOnly Property Shape As VoxelShape

    Public ReadOnly Property U As Tensor
    Public ReadOnly Property V As Tensor
    Public ReadOnly Property W As Tensor
    Public ReadOnly Property Pressure As Tensor
    Public ReadOnly Property Density As Tensor

    Public ReadOnly Property TotalVoxels As Integer
        Get
            Return Nx * Ny * Nz
        End Get
    End Property

    ''' <summary>
    ''' 扩展标量场（湍流 k / epsilon / 气含率 alpha / 溶解氧 DO / 湍流粘度 nut），
    ''' 名称 → Tensor（shape 与标准场一致）。快照记录器会将其与五标准场
    ''' 一并写入 .vti。
    ''' </summary>
    Public ReadOnly Property ExtraScalars As New Dictionary(Of String, Tensor)

    ''' <summary>按名读取扩展场（不存在时返回 Nothing）。</summary>
    Public Function GetExtra(name As String) As Tensor
        Dim t As Tensor = Nothing
        If ExtraScalars.TryGetValue(name, t) Then Return t
        Return Nothing
    End Function

    Public Sub New(nx As Integer, ny As Integer, nz As Integer, shape As VoxelShape)
        Me.Nx = nx
        Me.Ny = ny
        Me.Nz = nz
        Me.Shape = shape
        Me.U = New Tensor(nx, ny, nz)
        Me.V = New Tensor(nx, ny, nz)
        Me.W = New Tensor(nx, ny, nz)
        Me.Pressure = New Tensor(nx, ny, nz)
        Me.Density = New Tensor(nx, ny, nz)
        Dim rho0 As Double = 1000.0R
        For idx = 0 To TotalVoxels - 1
            Density.Data(idx) = rho0
        Next
    End Sub

    ''' <summary>按当前五标准场 + 扩展场做深拷贝。</summary>
    Public Function Clone() As FluidField
        Dim c As New FluidField(Nx, Ny, Nz, Shape)
        Array.Copy(U.Data, c.U.Data, TotalVoxels)
        Array.Copy(V.Data, c.V.Data, TotalVoxels)
        Array.Copy(W.Data, c.W.Data, TotalVoxels)
        Array.Copy(Pressure.Data, c.Pressure.Data, TotalVoxels)
        Array.Copy(Density.Data, c.Density.Data, TotalVoxels)
        For Each kv In ExtraScalars
            Dim t As New Tensor(Nx, Ny, Nz)
            Array.Copy(kv.Value.Data, t.Data, TotalVoxels)
            c.ExtraScalars(kv.Key) = t
        Next
        Return c
    End Function

    ''' <summary>安全读取 (i,j,k) 处的场值（越界/非活动体素返回 0）。</summary>
    Public Shared Function At(t As Tensor, nx As Integer, ny As Integer, nz As Integer,
                             i As Integer, j As Integer, k As Integer) As Double
        If i < 0 OrElse i >= nx OrElse j < 0 OrElse j >= ny OrElse k < 0 OrElse k >= nz Then
            Return 0.0R
        End If
        Return t.Data(i * (ny * nz) + j * nz + k)
    End Function

End Class
