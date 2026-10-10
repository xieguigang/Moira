' /********************************************************************************/
'
'   CudaSphBackend.vb
'
'   ISphCompute3D 的 CUDA GPU 实现后端（全 float32）
'
'   作用：
'       把 3D SPH 最耗时的两趟（密度求和 / 压力+粘性求和）下放到 NVIDIA GPU。
'       边界投影、搅拌桨驱动、积分与 CFL 控制仍由主机完成，因此 GPU 与 CPU
'       两条路径共享同一套物理与同一份 SoA 状态，结果可直接对照。
'
'   设计：
'       - 内核为内联 float32 CUDA C 源码（Kernels\moira_sph_f32.cu，嵌入资源），
'         经 NVRTC 即时编译；注册必须发生在 CudaEngine.TryCreate 之前。
'       - 数据布局与 SphState3D / UniformGrid3D 严格一致，无需任何转换。
'       - 每个子步：上传 q / v / cellStart / entries，跑两个内核，回读
'         ax/ay/az 与 dens / press（供主机积分与采样）。
'       - 任一步失败（无设备 / NVRTC 编译失败 / 内核缺失）即返回 False，
'         FluidEngine3D 自动回落到 CPU 后端，仿真不中断。
'
'   用法：
'       If CudaSphBackend.TryEnableGpu(engine) Then
'           Console.WriteLine("SPH running on " & engine.Backend.Name)
'       End If
'
' /********************************************************************************/

Imports System.IO
Imports Microsoft.VisualBasic.Imaging.Physics
Imports ILCudaKernels = Microsoft.VisualBasic.Computing.ILCuda.Kernels
Imports ILCudaRuntime = Microsoft.VisualBasic.Computing.ILCuda.Runtime

''' <summary>
''' The CUDA implementation of <see cref="ISphCompute3D"/>.
''' </summary>
Public Class CudaSphBackend : Implements ISphCompute3D

    Private Const KDensity As String = "moira_sph_density"
    Private Const KForce As String = "moira_sph_force"
    Private Const KernelResourceName As String = "moira_sph_f32.cu"

    ''' <summary>reason of the most recent failure (Nothing when healthy)</summary>
    Public Shared Property LastError As String

    Private ReadOnly _engine As ILCudaRuntime.CudaEngine
    Private _failed As Boolean

    ' ---- 设备缓冲 ----
    Private d_qx, d_qy, d_qz As ILCudaRuntime.DeviceBuffer(Of Single)
    Private d_vx, d_vy, d_vz As ILCudaRuntime.DeviceBuffer(Of Single)
    Private d_dens, d_densNear As ILCudaRuntime.DeviceBuffer(Of Single)
    Private d_press, d_pressNear As ILCudaRuntime.DeviceBuffer(Of Single)
    Private d_ax, d_ay, d_az As ILCudaRuntime.DeviceBuffer(Of Single)
    Private d_cellStart, d_entries As ILCudaRuntime.DeviceBuffer(Of Integer)

    Private _capacity As Integer = -1
    Private _cellCapacity As Integer = -1

    ''' <summary>
    ''' true once a sub step has actually produced device side density / pressure;
    ''' guards the very first <see cref="SyncFields"/> (before any kernel run the
    ''' device buffers are still uninitialized).
    ''' </summary>
    Private _fieldsValid As Boolean

    Public ReadOnly Property Name As String Implements ISphCompute3D.Name
        Get
            Return "CUDA-F32"
        End Get
    End Property

    ''' <summary>true when the GPU path has already failed and must not be retried</summary>
    Public ReadOnly Property Failed As Boolean
        Get
            Return _failed
        End Get
    End Property

    ''' <summary>
    ''' when true every sub step reads back density / pressure as well;
    ''' set it to false and call <see cref="SyncFields"/> right before a snapshot
    ''' to cut the per sub step PCIe traffic in half.
    ''' </summary>
    Public Property FullSync As Boolean = True

    Private Sub New(engine As ILCudaRuntime.CudaEngine)
        _engine = engine
    End Sub

#Region "内核源码与注册"

    ''' <summary>the embedded CUDA source of the SPH kernels</summary>
    Public Shared ReadOnly Property KernelSource As String
        Get
            Dim asm = GetType(CudaSphBackend).Assembly
            Dim resourceName As String = Nothing

            For Each name As String In asm.GetManifestResourceNames()
                If name.EndsWith(KernelResourceName, StringComparison.OrdinalIgnoreCase) Then
                    resourceName = name
                    Exit For
                End If
            Next

            If resourceName Is Nothing Then
                Throw New InvalidOperationException(
                    $"程序集 {asm.GetName().Name} 中未找到嵌入的 CUDA 内核资源 '{KernelResourceName}'")
            End If

            Using stream = asm.GetManifestResourceStream(resourceName)
                Using reader As New StreamReader(stream)
                    Return reader.ReadToEnd()
                End Using
            End Using
        End Get
    End Property

    Private Shared _sourceRegistered As Boolean
    Private Shared _regLock As New Object()

    ''' <summary>
    ''' try to initialize CUDA and switch the given engine onto the GPU backend.
    ''' returns false (and leaves the CPU backend in place) when no device is
    ''' available or the kernel source cannot be compiled.
    ''' </summary>
    Public Shared Function TryEnableGpu(engine As FluidEngine3D, Optional deviceOrdinal As Integer = -1) As Boolean
        If engine Is Nothing Then Return False

        Try
            Dim backend = TryCreate(deviceOrdinal)
            If backend Is Nothing Then Return False

            engine.Backend = backend
            Return True
        Catch ex As Exception
            LastError = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>
    ''' create the CUDA backend; Nothing when the GPU is unavailable.
    ''' </summary>
    Public Shared Function TryCreate(Optional deviceOrdinal As Integer = -1) As CudaSphBackend
        Try
            ' 内核源码必须注册于 CudaEngine.TryCreate 之前（NVRTC 一次性编译整个编译单元）
            If Not _sourceRegistered Then
                SyncLock _regLock
                    If Not _sourceRegistered Then
                        Call ILCudaKernels.KernelSources.RegisterSource(KernelResourceName, KernelSource)
                        _sourceRegistered = True
                    End If
                End SyncLock
            End If

            Dim options As New ILCudaRuntime.EngineOptions()
            If deviceOrdinal >= 0 Then options.DeviceOrdinal = deviceOrdinal

            Dim engine = ILCudaRuntime.CudaEngine.TryCreate(options)
            If engine Is Nothing Then
                LastError = options.ErrorMessage
                Return Nothing
            End If

            Dim backend As New CudaSphBackend(engine)

            ' 内核存在性预检：缺失时直接放弃，交给 CPU 后端
            If backend.GetKernel(KDensity) Is Nothing OrElse backend.GetKernel(KForce) Is Nothing Then
                LastError = "SPH CUDA 内核不可用（moira_sph_density / moira_sph_force）"
                Return Nothing
            End If

            LastError = Nothing
            Return backend
        Catch ex As Exception
            LastError = ex.Message
            Return Nothing
        End Try
    End Function

    Private Function GetKernel(name As String) As ILCudaRuntime.CudaKernel
        Try
            Return _engine.GetKernel(name)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

#End Region

#Region "缓冲管理"

    Private Sub EnsureBuffers(state As SphState3D, grid As UniformGrid3D)
        Dim cap = state.px.Length
        Dim cellCap = grid.CellStart.Length

        If cap <> _capacity Then
            Call DisposeParticleBuffers()

            d_qx = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_qy = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_qz = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_vx = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_vy = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_vz = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_dens = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_densNear = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_press = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_pressNear = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_ax = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_ay = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)
            d_az = New ILCudaRuntime.DeviceBuffer(Of Single)(cap)

            _capacity = cap
            _fieldsValid = False
        End If

        If cellCap <> _cellCapacity OrElse grid.Entries.Length <> _cellCapacity Then
            If d_cellStart IsNot Nothing Then Call d_cellStart.Dispose()
            If d_entries IsNot Nothing Then Call d_entries.Dispose()

            d_cellStart = New ILCudaRuntime.DeviceBuffer(Of Integer)(cellCap)
            d_entries = New ILCudaRuntime.DeviceBuffer(Of Integer)(grid.Entries.Length)
            _cellCapacity = cellCap
        End If
    End Sub

#End Region

    ''' <summary>
    ''' run one SPH sub step on the GPU. any failure returns False so that the
    ''' host engine transparently falls back to the CPU backend.
    ''' </summary>
    Public Function RunSubstep(state As SphState3D, grid As UniformGrid3D,
                               params As SphParams3D, dt As Single) As Boolean Implements ISphCompute3D.RunSubstep

        If _failed Then Return False

        Try
            Dim n = state.Count
            If n <= 0 Then Return False

            Call EnsureBuffers(state, grid)

            Dim kd = GetKernel(KDensity)
            Dim kf = GetKernel(KForce)
            If kd Is Nothing OrElse kf Is Nothing Then
                _failed = True
                LastError = "SPH CUDA 内核不可用"
                Return False
            End If

            ' ---- 上传 ----
            Call d_qx.Write(state.qx)
            Call d_qy.Write(state.qy)
            Call d_qz.Write(state.qz)
            Call d_vx.Write(state.vx)
            Call d_vy.Write(state.vy)
            Call d_vz.Write(state.vz)
            Call d_cellStart.Write(grid.CellStart)
            Call d_entries.Write(grid.Entries)

            Dim h = params.SmoothingRadius
            Dim planner = ILCudaRuntime.LaunchPlanner.For1D(n, 256)

            ' ---- 趟 1：密度 ----
            Call kd.Launch(planner,
                           d_qx, d_qy, d_qz,
                           d_cellStart, d_entries,
                           grid.Nx, grid.Ny, grid.Nz,
                           grid.OriginX, grid.OriginY, grid.OriginZ, grid.CellSize,
                           h, params.SpikyPow2, params.SpikyPow3,
                           params.SelfDensity, params.SelfNearDensity,
                           d_dens, d_densNear, n)

            ' ---- 趟 2：压力 + 粘性 ----
            Dim vis = params.Viscosity
            If vis > 0 AndAlso vis * dt > 0.5F Then vis = 0.5F / dt
            Dim visScale = vis / If(params.RestDensity > 0, params.RestDensity, 1.0F)

            Call kf.Launch(planner,
                           d_qx, d_qy, d_qz,
                           d_vx, d_vy, d_vz,
                           d_dens, d_densNear,
                           d_cellStart, d_entries,
                           grid.Nx, grid.Ny, grid.Nz,
                           grid.OriginX, grid.OriginY, grid.OriginZ, grid.CellSize,
                           h, params.RestDensity, params.RestNearDensity, params.VolumeScale,
                           params.MinDensityRatio, params.MaxDensityRatio,
                           params.MinPressureRatio * params.PressureK,
                           params.PressureK, params.NearPressureK,
                           params.GradSpikyPow2, params.GradSpikyPow3, params.Poly6,
                           visScale, params.MaxAccel,
                           d_press, d_pressNear,
                           d_ax, d_ay, d_az, n)

            _fieldsValid = True

            ' ---- 回读：加速度每步都要（主机侧积分），标量场按需同步 ----
            state.ax = d_ax.Read()
            state.ay = d_ay.Read()
            state.az = d_az.Read()

            If FullSync Then Call SyncFields(state)

            Return True
        Catch ex As Exception
            _failed = True
            LastError = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>
    ''' read the density / pressure fields back from the device into the host state.
    ''' call this before sampling when <see cref="FullSync"/> is off.
    ''' </summary>
    Public Sub SyncFields(state As SphState3D)
        If _failed OrElse d_dens Is Nothing OrElse Not _fieldsValid Then Return

        Try
            state.dens = d_dens.Read()
            state.densNear = d_densNear.Read()
            state.press = d_press.Read()
            state.pressNear = d_pressNear.Read()
        Catch ex As Exception
            _failed = True
            LastError = ex.Message
        End Try
    End Sub

#Region "释放"

    Private Sub DisposeParticleBuffers()
        For Each b In {d_qx, d_qy, d_qz, d_vx, d_vy, d_vz,
                       d_dens, d_densNear, d_press, d_pressNear, d_ax, d_ay, d_az}
            If b IsNot Nothing Then Call b.Dispose()
        Next

        d_qx = Nothing : d_qy = Nothing : d_qz = Nothing
        d_vx = Nothing : d_vy = Nothing : d_vz = Nothing
        d_dens = Nothing : d_densNear = Nothing
        d_press = Nothing : d_pressNear = Nothing
        d_ax = Nothing : d_ay = Nothing : d_az = Nothing
        _capacity = -1
    End Sub

    ''' <summary>release every device buffer</summary>
    Public Sub Dispose()
        Call DisposeParticleBuffers()

        If d_cellStart IsNot Nothing Then Call d_cellStart.Dispose()
        If d_entries IsNot Nothing Then Call d_entries.Dispose()

        d_cellStart = Nothing
        d_entries = Nothing
        _cellCapacity = -1
    End Sub

#End Region

End Class
