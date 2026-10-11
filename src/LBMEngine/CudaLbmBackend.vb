Imports System.IO
Imports ILCudaKernels = Microsoft.VisualBasic.Computing.ILCuda.Kernels
Imports ILCudaRuntime = Microsoft.VisualBasic.Computing.ILCuda.Runtime

' /********************************************************************************/
'
'   CudaLbmBackend.vb
'
'   LBM float32 CUDA 后端 —— 与 CFDEngine/CudaTensorF 相同的管线模式：
'
'       1. KernelSources.RegisterSource("moira_lbm_f32.cu", source)
'          （内联 .cu 源码必须在 CudaEngine.TryCreate 之前注册，NVRTC 一次性编译）
'       2. ILCudaRuntime.CudaEngine.TryCreate(options)
'       3. 分配 DeviceBuffer(Of Single) / DeviceBuffer(Of Byte) 设备驻留缓冲
'       4. kernel.Launch(LaunchPlanner.For1D(n, 256), args...)
'
'   设备驻留策略：分布函数 f / fc 与矩量 (rho, ux, uy, uz) 常驻显存，
'   仅在需要可视化或诊断时通过 ReadMoments / DownloadF 回读，避免每步 PCIe 往返。
'
' /********************************************************************************/

''' <summary>
''' LBM 专用 float32 CUDA 计算后端。
''' 通过 <see cref="TryRegister"/> 注册，成功后经 <see cref="Current"/> 取用；
''' 注册失败自动回退宿主 CPU float 路径。
''' </summary>
Public Class CudaLbmBackend : Implements IDisposable

#Region "单元边界类型常量（与 moira_lbm_f32.cu 严格一致）"

    ''' <summary>流体单元。</summary>
    Public Const CellFluid As Byte = 0
    ''' <summary>固体单元（无滑移 halfway bounce-back，冻结 f = w）。</summary>
    Public Const CellSolid As Byte = 1
    ''' <summary>入口单元（定常平衡态分布，rho = 1，u = uin）。</summary>
    Public Const CellInlet As Byte = 2
    ''' <summary>出口单元（零梯度，复制 x-1 邻居碰撞后分布）。</summary>
    Public Const CellOutlet As Byte = 3
    ''' <summary>运动固体单元（bounce-back + 6*w*ex*uwall*rho 动量修正，方腔顶盖）。</summary>
    Public Const CellMovingSolid As Byte = 5

#End Region

#Region "静态注册"

    Private Shared _current As CudaLbmBackend
    Private Shared _sourceRegistered As Boolean
    Private Shared _kernelSource As String
    Private Shared ReadOnly _regLock As New Object

    ''' <summary>最近一次注册失败的错误信息。</summary>
    Public Shared Property LastError As String

    ''' <summary>当前已注册的后端实例（未注册时为 Nothing）。</summary>
    Public Shared ReadOnly Property Current As CudaLbmBackend
        Get
            Return _current
        End Get
    End Property

    ''' <summary>嵌入程序集的内核源码文本。</summary>
    Public Shared ReadOnly Property LbmKernelSource As String
        Get
            If _kernelSource Is Nothing Then
                Dim asm As Reflection.Assembly = GetType(CudaLbmBackend).Assembly

                For Each name As String In asm.GetManifestResourceNames()
                    If name.EndsWith("moira_lbm_f32.cu", StringComparison.OrdinalIgnoreCase) Then
                        Using s As Stream = asm.GetManifestResourceStream(name)
                            Using reader As New StreamReader(s)
                                _kernelSource = reader.ReadToEnd()
                            End Using
                        End Using
                        Exit For
                    End If
                Next
            End If

            If _kernelSource Is Nothing Then
                Throw New FileNotFoundException("嵌入资源 moira_lbm_f32.cu 未找到（请检查 vbproj 的 EmbeddedResource 声明）")
            End If

            Return _kernelSource
        End Get
    End Property

    ''' <summary>
    ''' 尝试注册 LBM float32 CUDA 后端（幂等）。
    ''' 成功后 <see cref="Current"/> 可用；失败返回 False 并将原因写入 <see cref="LastError"/>。
    ''' </summary>
    Public Shared Function TryRegister(Optional deviceOrdinal As Integer = -1) As Boolean
        If _current IsNot Nothing Then
            Return True
        End If

        SyncLock _regLock
            If _current IsNot Nothing Then
                Return True
            End If

            Try
                ' 内核源码必须注册于 CudaEngine.TryCreate 之前（NVRTC 一次性编译整个编译单元）
                If Not _sourceRegistered Then
                    Call ILCudaKernels.KernelSources.RegisterSource("moira_lbm_f32.cu", LbmKernelSource)
                    _sourceRegistered = True
                End If

                Dim options As New ILCudaRuntime.EngineOptions()
                If deviceOrdinal >= 0 Then
                    options.DeviceOrdinal = deviceOrdinal
                End If

                Dim engine = ILCudaRuntime.CudaEngine.TryCreate(options)
                If engine Is Nothing Then
                    LastError = options.ErrorMessage
                    Return False
                End If

                _current = New CudaLbmBackend(engine)
                Return True

            Catch ex As Exception
                LastError = ex.Message
                Return False
            End Try
        End SyncLock
    End Function

    ''' <summary>注销当前后端并释放全部设备资源。</summary>
    Public Shared Sub Unregister()
        SyncLock _regLock
            If _current IsNot Nothing Then
                Call _current.Dispose()
                _current = Nothing
            End If
        End SyncLock
    End Sub

#End Region

#Region "内核句柄"

    Private Const KCollide As String = "moira_lbm_collide"
    Private Const KStream As String = "moira_lbm_stream"
    Private Const KMoments As String = "moira_lbm_moments"
    Private Const KInitEq As String = "moira_lbm_init_eq"

    Private ReadOnly _engine As ILCudaRuntime.CudaEngine
    Private _kCollide, _kStream, _kMoments, _kInitEq As ILCudaRuntime.CudaKernel

    Private Sub New(engine As ILCudaRuntime.CudaEngine)
        _engine = engine
        _kCollide = Kernel(KCollide)
        _kStream = Kernel(KStream)
        _kMoments = Kernel(KMoments)
        _kInitEq = Kernel(KInitEq)
    End Sub

    Private Function Kernel(name As String) As ILCudaRuntime.CudaKernel
        Try
            Return _engine.GetKernel(name)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>全部四个内核句柄均可用。</summary>
    Public ReadOnly Property IsReady As Boolean
        Get
            Return _kCollide IsNot Nothing AndAlso _kStream IsNot Nothing AndAlso
                   _kMoments IsNot Nothing AndAlso _kInitEq IsNot Nothing
        End Get
    End Property

#End Region

#Region "设备驻留状态"

    ''' <summary>
    ''' 单个求解器的设备驻留缓冲集合。
    ''' f / fc 为方向主序（19*n），矩量与边界类型为逐格点（n）。
    ''' </summary>
    Public NotInheritable Class DeviceState : Implements IDisposable

        ''' <summary>当前分布函数（19*n，方向主序）。</summary>
        Public F As ILCudaRuntime.DeviceBuffer(Of Single)
        ''' <summary>碰撞后分布函数暂存（19*n，方向主序）。</summary>
        Public Fc As ILCudaRuntime.DeviceBuffer(Of Single)
        Public Rho As ILCudaRuntime.DeviceBuffer(Of Single)
        Public Ux As ILCudaRuntime.DeviceBuffer(Of Single)
        Public Uy As ILCudaRuntime.DeviceBuffer(Of Single)
        Public Uz As ILCudaRuntime.DeviceBuffer(Of Single)
        ''' <summary>单元边界类型掩码。</summary>
        Public Btype As ILCudaRuntime.DeviceBuffer(Of Byte)

        Public N As Integer
        Public Q As Integer

        Public Sub Dispose() Implements IDisposable.Dispose
            If F IsNot Nothing Then Call F.Dispose() : F = Nothing
            If Fc IsNot Nothing Then Call Fc.Dispose() : Fc = Nothing
            If Rho IsNot Nothing Then Call Rho.Dispose() : Rho = Nothing
            If Ux IsNot Nothing Then Call Ux.Dispose() : Ux = Nothing
            If Uy IsNot Nothing Then Call Uy.Dispose() : Uy = Nothing
            If Uz IsNot Nothing Then Call Uz.Dispose() : Uz = Nothing
            If Btype IsNot Nothing Then Call Btype.Dispose() : Btype = Nothing
        End Sub
    End Class

    ''' <summary>
    ''' 分配设备驻留缓冲并上传边界类型掩码。
    ''' </summary>
    ''' <param name="n">格点总数（nx*ny*nz）。</param>
    ''' <param name="btype">边界类型掩码（长度 n，取值见 Cell* 常量）。</param>
    Public Function Allocate(n As Integer, btype As Byte()) As DeviceState
        If btype Is Nothing OrElse btype.Length <> n Then
            Throw New ArgumentException($"btype 长度必须为 {n}")
        End If

        Dim state As New DeviceState With {
            .N = n,
            .Q = 19
        }

        state.F = New ILCudaRuntime.DeviceBuffer(Of Single)(19 * n)
        state.Fc = New ILCudaRuntime.DeviceBuffer(Of Single)(19 * n)
        state.Rho = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        state.Ux = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        state.Uy = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        state.Uz = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        state.Btype = New ILCudaRuntime.DeviceBuffer(Of Byte)(n)

        Call state.Btype.Write(btype)
        Call state.Rho.Fill(1.0F)
        Call state.Ux.Fill(0.0F)
        Call state.Uy.Fill(0.0F)
        Call state.Uz.Fill(0.0F)
        ' f / Fc 由 InitEquilibrium 或 UploadF 填充

        Return state
    End Function

#End Region

#Region "内核 Launch 封装"

    ''' <summary>全域初始化：f = f_eq(rho = 1, u = uin)。</summary>
    Public Sub InitEquilibrium(state As DeviceState, uinx As Single, uiny As Single, uinz As Single)
        Call _kInitEq.Launch(ILCudaRuntime.LaunchPlanner.For1D(state.N, 256),
                             state.F, uinx, uiny, uinz, state.N)
    End Sub

    ''' <summary>碰撞内核：f -> fc（BGK / TRT）。</summary>
    ''' <param name="omega">ω⁻ = 1/τ</param>
    ''' <param name="omegaP">ω⁺（TRT 用，按 τ 与 MagicLambda = 1/4 预计算）</param>
    ''' <param name="trt">True = TRT，False = BGK</param>
    Public Sub Collide(state As DeviceState, omega As Single, omegaP As Single, trt As Boolean)
        Call _kCollide.Launch(ILCudaRuntime.LaunchPlanner.For1D(state.N, 256),
                              state.F, state.Fc, state.Rho, state.Ux, state.Uy, state.Uz,
                              omega, omegaP, If(trt, 1, 0), state.N)
    End Sub

    ''' <summary>
    ''' 流传播内核：fc -> f，融合 bounce-back / specular / 入口 / 出口 / 固体冻结，
    ''' 并就地写出矩量。
    ''' </summary>
    ''' <param name="uwall">顶盖运动壁速度（仅 CellMovingSolid 拉取时生效）</param>
    ''' <param name="uinx"></param><param name="uiny"></param><param name="uinz">入口平衡态速度</param>
    Public Sub Stream(state As DeviceState,
                      uwall As Single, uinx As Single, uiny As Single, uinz As Single,
                      nx As Integer, ny As Integer, nz As Integer)

        Call _kStream.Launch(ILCudaRuntime.LaunchPlanner.For1D(state.N, 256),
                             state.Fc, state.F, state.Rho, state.Ux, state.Uy, state.Uz,
                             state.Btype, uwall, uinx, uiny, uinz,
                             nx, ny, nz, state.N)
    End Sub

    ''' <summary>矩量内核：从 f 就地重算 (rho, ux, uy, uz)。</summary>
    Public Sub ComputeMoments(state As DeviceState)
        Call _kMoments.Launch(ILCudaRuntime.LaunchPlanner.For1D(state.N, 256),
                              state.F, state.Rho, state.Ux, state.Uy, state.Uz,
                              state.Btype, state.N)
    End Sub

    ''' <summary>上传完整的分布函数宿主数组（19*n，方向主序）。</summary>
    Public Sub UploadF(state As DeviceState, f As Single())
        Call state.F.Write(f)
    End Sub

    ''' <summary>回读完整的分布函数宿主数组（19*n，方向主序）。</summary>
    Public Function DownloadF(state As DeviceState) As Single()
        Return state.F.Read()
    End Function

    ''' <summary>
    ''' 回读矩量场（各返回长度 n 的新数组）。
    ''' 仅在可视化 / 诊断帧调用，避免每步 PCIe 往返。
    ''' </summary>
    Public Sub ReadMoments(state As DeviceState,
                           ByRef rho As Single(), ByRef ux As Single(),
                           ByRef uy As Single(), ByRef uz As Single())
        rho = state.Rho.Read()
        ux = state.Ux.Read()
        uy = state.Uy.Read()
        uz = state.Uz.Read()
    End Sub

#End Region

#Region "IDisposable"

    Public Sub Dispose() Implements IDisposable.Dispose
        ' 引擎句柄由 ILCuda 内部管理，这里仅做引用置空；
        ' DeviceState 由求解器各自持有并释放。
    End Sub

#End Region

End Class
