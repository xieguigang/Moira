Imports System.Threading
Imports System.Threading.Tasks
Imports Moira.FVMEngine.Eulerian
Imports Math = System.Math

' /********************************************************************************/
'
'   WindTunnelSimulationRunner.vb
'
'   WindTunnelBox 的"并发适配层"：把 FVM 求解搬到后台线程，UI 只通过
'   事件与不可变快照与之交互。
'
'   为什么必须搬到后台：
'       FVM 的一次外迭代 = 动量预测（3 个分量）+ 压力 Poisson + 通量/速度校正
'       （+ 湍流输运），比 LBM 的一步贵 1~2 个量级。原实现在 30 ms 的
'       WinForms Timer 里同步跑 RunSteps，换成 FVM 后必然卡死界面。
'
'   线程模型：
'       · 构造必须在 UI 线程（捕获 SynchronizationContext）
'       · 求解在 Task 内；每完成一个采样块生成一份不可变快照并发布
'       · 所有事件通过 SynchronizationContext.Post 封送回 UI 线程，
'         调用方无需 Invoke
'       · 取消粒度 = 一个采样块（每次迭代前检查 CancellationToken）
'
' /********************************************************************************/

''' <summary>
''' 风洞 FVM 仿真的后台编排器。UI 只与本类交互，不直接触碰求解器。
''' </summary>
Public NotInheritable Class WindTunnelSimulationRunner
    Implements IDisposable

    ''' <summary>UI 线程上下文（构造时捕获）。</summary>
    Private ReadOnly _sync As SynchronizationContext
    Private ReadOnly _gate As New Object()

    Private _cts As CancellationTokenSource
    Private _worker As Task

    Private _mesh As EulerianFvMesh
    Private _sampler As EulerianFieldSampler
    Private _case As EulerianWindTunnelCase
    Private _options As EulerianWindTunnelOptions
    Private _snapshot As EulerianFieldSnapshot

    Private _totalSteps As Integer
    Private _doneSteps As Integer
    Private _sampleInterval As Integer = 1
    Private _gpuRegistered As Boolean
    Private _gpuMessage As String = ""

    ''' <summary>已加载的网格（未加载时为 Nothing）。</summary>
    Public ReadOnly Property Mesh As EulerianFvMesh
        Get
            Return _mesh
        End Get
    End Property

    ''' <summary>采样器（未加载时为 Nothing）。</summary>
    Public ReadOnly Property Sampler As EulerianFieldSampler
        Get
            Return _sampler
        End Get
    End Property

    ''' <summary>当前算例（未配置时为 Nothing）。</summary>
    Public ReadOnly Property CaseObject As EulerianWindTunnelCase
        Get
            Return _case
        End Get
    End Property

    ''' <summary>最近发布的快照（线程安全读取）。</summary>
    Public ReadOnly Property LatestSnapshot As EulerianFieldSnapshot
        Get
            SyncLock _gate
                Return _snapshot
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _worker IsNot Nothing AndAlso Not _worker.IsCompleted
        End Get
    End Property

    Public ReadOnly Property TotalSteps As Integer
        Get
            Return _totalSteps
        End Get
    End Property

    Public ReadOnly Property DoneSteps As Integer
        Get
            Return _doneSteps
        End Get
    End Property

    ''' <summary>GPU 后端注册结果描述（供状态栏显示）。</summary>
    Public ReadOnly Property GpuMessage As String
        Get
            Return _gpuMessage
        End Get
    End Property

    Public Sub New()
        _sync = SynchronizationContext.Current
    End Sub

#Region "事件"

    ''' <summary>发布了一份新快照（已在 UI 线程）。</summary>
    Public Event Progress(snapshot As EulerianFieldSnapshot)

    ''' <summary>状态文本更新（已在 UI 线程）。</summary>
    Public Event Status(text As String)

    ''' <summary>
    ''' 求解结束（已在 UI 线程）。
    ''' <paramref name="success"/>：True = 正常跑完；False = 被取消或出错。
    ''' </summary>
    Public Event Finished(success As Boolean, message As String)

    Private Sub Post(action As Action)
        If _sync Is Nothing Then
            action()
        Else
            _sync.Post(Sub(state) action(), Nothing)
        End If
    End Sub

#End Region

#Region "网格加载"

    ''' <summary>
    ''' 加载 *.eulerian.zip 网格包，并据此建立重采样器。
    ''' </summary>
    ''' <param name="zipPath">由 src\EulerianMesh 命令行工具生成的网格包。</param>
    ''' <param name="targetCells">结构格格点上限。</param>
    ''' <returns>网格诊断文本（含单元数、非正交角、patch 表）。</returns>
    Public Function LoadMesh(zipPath As String, Optional targetCells As Integer = 250000) As String
        If String.IsNullOrEmpty(zipPath) Then
            Throw New ArgumentException("网格文件路径不能为空", NameOf(zipPath))
        End If

        Call EnsureGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Dim sampler As New EulerianFieldSampler(mesh, targetCells)

        _mesh = mesh
        _sampler = sampler
        _case = Nothing
        _snapshot = Nothing
        _options = Nothing
        _doneSteps = 0
        _totalSteps = 0

        Dim sb As New Text.StringBuilder(256)

        sb.AppendLine(mesh.Diagnostics.TrimEnd())
        sb.AppendLine(sampler.Description)
        sb.AppendLine($"gpu: {_gpuMessage}")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 首次使用时尝试注册 CUDA float32 后端；失败则静默回落 CPU 并记录原因。
    ''' </summary>
    Private Sub EnsureGpu()
        If _gpuRegistered Then Return

        _gpuRegistered = True

        Try
            If EulerianFvmCompute.TryRegister() Then
                _gpuMessage = "CUDA float32 (EulerianFvmCompute)"
            Else
                _gpuMessage = "CPU (" & If(EulerianFvmCompute.LastError, "CUDA 不可用") & ")"
            End If
        Catch ex As Exception
            _gpuMessage = "CPU (GPU 注册异常: " & ex.Message & ")"
        End Try
    End Sub

#End Region

#Region "算例配置"

    ''' <summary>
    ''' 按给定的物理参数（重新）构建算例。会丢弃已有的求解状态。
    ''' </summary>
    Public Sub Configure(options As EulerianWindTunnelOptions, Optional sampleInterval As Integer = 1)
        If _mesh Is Nothing Then
            Throw New InvalidOperationException("尚未加载网格包。")
        End If
        If options Is Nothing Then
            Throw New ArgumentNullException(NameOf(options))
        End If

        _sampleInterval = Math.Max(1, sampleInterval)
        options.ForceInterval = _sampleInterval

        Dim c As New EulerianWindTunnelCase(_mesh, options)

        Call c.Configure()

        _case = c
        _options = options
        _doneSteps = 0
        _totalSteps = 0

        Call PublishSnapshot(True)
    End Sub

    ''' <summary>当前求解参数（未配置时为 Nothing）。</summary>
    Public ReadOnly Property Options As EulerianWindTunnelOptions
        Get
            Return _options
        End Get
    End Property

#End Region

#Region "推进控制"

    ''' <summary>
    ''' 启动后台求解。
    ''' </summary>
    ''' <param name="totalSteps">本轮要跑的总步数。</param>
    Public Sub Start(totalSteps As Integer)
        If _case Is Nothing Then
            RaiseStatus("尚未配置算例。")
            Return
        End If
        If IsRunning Then Return

        totalSteps = Math.Max(0, totalSteps)

        If totalSteps = 0 Then Return

        _totalSteps = totalSteps
        _cts = New CancellationTokenSource()
        Dim token As CancellationToken = _cts.Token

        _worker = Task.Run(
            Sub()
                Try
                    Dim interval As Integer = _sampleInterval

                    While _doneSteps < _totalSteps
                        token.ThrowIfCancellationRequested()

                        Dim chunk As Integer = Math.Min(interval, _totalSteps - _doneSteps)

                        _case.Run(chunk)
                        _doneSteps += chunk

                        Call PublishSnapshot(False)
                    End While

                    Dim tail As String = If(_case.LastForces IsNot Nothing,
                                            $"  Cd={_case.LastForces.Cd.ToString("F4", Globalization.CultureInfo.InvariantCulture)}", "")

                    Post(Sub() RaiseEvent Finished(True, $"完成 {_doneSteps} 步{tail}"))

                Catch ex As OperationCanceledException
                    Post(Sub() RaiseEvent Finished(False, $"已停止（已完成 {_doneSteps} 步）"))

                Catch ex As Exception
                    Dim msg As String = BuildErrorMessage(ex)
                    Post(Sub() RaiseEvent Finished(False, msg))
                End Try
            End Sub, token)
    End Sub

    ''' <summary>请求停止（取消粒度 = 一个采样块）。</summary>
    Public Sub [Stop]()
        If _cts IsNot Nothing Then
            _cts.Cancel()
        End If
    End Sub

    ''' <summary>单步（同步，供"单步"按钮使用）。</summary>
    Public Sub StepOnce()
        If _case Is Nothing Then Return
        If IsRunning Then Return

        _case.Run(1)
        _doneSteps += 1
        _totalSteps = Math.Max(_totalSteps, _doneSteps)

        Call PublishSnapshot(True)
    End Sub

    ''' <summary>重置：丢弃求解状态，用当前参数重建算例。</summary>
    Public Sub Reset()
        If _options Is Nothing Then Return

        Call [Stop]()

        Dim opts As EulerianWindTunnelOptions = _options
        Dim interval As Integer = _sampleInterval

        Call Configure(opts, interval)
    End Sub

#End Region

#Region "快照发布"

    Private Sub PublishSnapshot(raiseProgress As Boolean)
        If _case Is Nothing OrElse _sampler Is Nothing Then Return

        Dim snap As EulerianFieldSnapshot = _sampler.Sample(_case)

        SyncLock _gate
            _snapshot = snap
        End SyncLock

        If raiseProgress Then
            Post(Sub() RaiseEvent Progress(snap))
        End If
    End Sub

    Private Sub RaiseStatus(text As String)
        Post(Sub() RaiseEvent Status(text))
    End Sub

    Private Function BuildErrorMessage(ex As Exception) As String
        Dim sb As New Text.StringBuilder(256)

        sb.AppendLine(ex.Message)

        If _mesh IsNot Nothing Then
            sb.AppendLine($"cells={_mesh.CellCount}, step={_doneSteps}")
        End If

        If _case IsNot Nothing AndAlso _case.Incompressible IsNot Nothing Then
            Dim hist As List(Of Double) = _case.Incompressible.ResidualHistory

            If hist IsNot Nothing AndAlso hist.Count > 0 Then
                sb.AppendLine($"last residual={hist(hist.Count - 1).ToString("E3", Globalization.CultureInfo.InvariantCulture)}")
            End If
        End If

        Return sb.ToString()
    End Function

#End Region

#Region "IDisposable"

    Private _disposed As Boolean

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return

        _disposed = True

        Call [Stop]()

        If _cts IsNot Nothing Then
            _cts.Dispose()
            _cts = Nothing
        End If
    End Sub

#End Region

End Class
