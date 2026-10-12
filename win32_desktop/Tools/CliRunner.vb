' /********************************************************************************/
'
'   CliRunner.vb
'
'   CFD 命令行工具（EulerianMesh.exe / Voxelizer.exe / WindTunnel.exe）的进程执行封装：
'   启动外部 exe → 异步回流 stdout 与 stderr → 支持中途终止进程树。
'
'   设计要点：
'     · 全部输出经 Control.BeginInvoke 封送到 UI 线程，窗体侧无需再做线程切换；
'     · 日志行数由调用方（窗体）限流，本类只负责原样转发；
'     · 终止时先 kill 进程树（galaxy 的 Global.Tools.KillDescendantProcesses），
'       再 Process.Kill(True) 兜底，避免残留孙进程继续占用 GPU / 文件句柄。
'
'   注意：galaxy 引用中存在全局模块 Tools，故此处类型不得命名为 Tools，
'         也不得声明 Namespace Tools。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' 单个外部命令行工具的异步执行器（一个窗体持有一个实例）。
''' </summary>
Friend Class CliRunner
    Implements IDisposable

    ''' <summary>日志区最多保留的行数（超出后由窗体侧按块截断）。</summary>
    Public Const MaxLogLines As Integer = 2000

    ''' <summary>接收到一行工具输出（stdout 或 stderr）。</summary>
    Public Event LogLine(text As String)

    ''' <summary>进程已结束。</summary>
    Public Event Exited(exitCode As Integer, elapsed As TimeSpan)

    Private _proc As Process
    Private _owner As Control
    Private _watch As Stopwatch
    Private _exitCode As Integer = 0
    Private _finished As Boolean = True

    ''' <summary>是否有正在运行的子进程。</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Not _finished
        End Get
    End Property

    ''' <summary>最近一次运行的退出码（0 = 成功；1 = 运行期失败；2 = 参数错误）。</summary>
    Public ReadOnly Property ExitCode As Integer
        Get
            Return _exitCode
        End Get
    End Property

    ''' <summary>
    ''' 启动工具进程。
    ''' </summary>
    ''' <param name="owner">用于把日志封送回 UI 线程的控件（通常传窗体自身）。</param>
    ''' <param name="exePath">工具 exe 全路径。</param>
    ''' <param name="args">命令行实参串（已拼好引号）。</param>
    ''' <param name="workDir">工作目录（为空或不存在时回落到 exe 所在目录）。</param>
    ''' <returns>成功启动返回 True。</returns>
    Public Function Run(owner As Control, exePath As String, args As String, workDir As String) As Boolean
        If Not _finished Then
            Call Emit("warning: 工具进程仍在运行，请先停止")
            Return False
        End If

        If String.IsNullOrWhiteSpace(exePath) OrElse Not File.Exists(exePath) Then
            Call Emit($"error: 未找到工具程序：{exePath}")
            Call Emit("       请通过「工具程序」一栏手动指定 exe 路径")
            Return False
        End If

        If String.IsNullOrWhiteSpace(workDir) OrElse Not Directory.Exists(workDir) Then
            workDir = Path.GetDirectoryName(exePath)
        End If

        Dim psi As New ProcessStartInfo(exePath) With {
            .Arguments = args,
            .WorkingDirectory = workDir,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }

        _owner = owner
        _exitCode = 0
        _finished = False

        Try
            _proc = New Process() With {
                .StartInfo = psi,
                .EnableRaisingEvents = True
            }

            AddHandler _proc.OutputDataReceived, AddressOf OnDataReceived
            AddHandler _proc.ErrorDataReceived, AddressOf OnDataReceived
            AddHandler _proc.Exited, AddressOf OnProcessExited

            _watch = Stopwatch.StartNew()

            Call _proc.Start()
            Call _proc.BeginOutputReadLine()
            Call _proc.BeginErrorReadLine()

            Return True
        Catch ex As Exception
            _finished = True
            _proc = Nothing
            Call Emit($"error: 启动失败：{ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>终止正在运行的进程树（先子孙、后自身）。</summary>
    Public Sub StopRunning()
        Dim p As Process = _proc

        If p Is Nothing Then Return

        Try
            If Not p.HasExited Then
                Try
                    ' galaxy 的全局工具模块：类型全名为 Galaxy.Workbench.Tools
                    Call Global.Galaxy.Workbench.Tools.KillDescendantProcesses(p.Id)
                Catch ex As Exception
                    ' WMI 不可用时直接走下面的整树 kill
                End Try

                Call p.Kill(True)
                Call Emit("==== 已请求终止进程树 ====")
            End If
        Catch ex As Exception
            Call Emit($"终止进程失败：{ex.Message}")
        End Try
    End Sub

    Private Sub OnDataReceived(sender As Object, e As DataReceivedEventArgs)
        If e.Data Is Nothing Then Return

        Dim line As String = e.Data
        Dim owner As Control = _owner

        If owner IsNot Nothing AndAlso Not owner.IsDisposed AndAlso owner.IsHandleCreated Then
            Try
                Call owner.BeginInvoke(Sub() OnLogLine(line))
                Return
            Catch ex As Exception
                ' 句柄已销毁等情况：直接同步抛出事件
            End Try
        End If

        Call OnLogLine(line)
    End Sub

    Private Sub OnProcessExited(sender As Object, e As EventArgs)
        Dim code As Integer = 0

        Try
            If _proc IsNot Nothing Then code = _proc.ExitCode
        Catch ex As Exception
            code = -1
        End Try

        Dim elapsed As TimeSpan = If(_watch Is Nothing, TimeSpan.Zero, _watch.Elapsed)

        If _watch IsNot Nothing Then _watch.Stop()

        _exitCode = code
        _finished = True

        Dim owner As Control = _owner

        If owner IsNot Nothing AndAlso Not owner.IsDisposed AndAlso owner.IsHandleCreated Then
            Try
                Call owner.BeginInvoke(Sub()
                                          Call OnLogLine("")
                                          Call OnLogLine($"==== 工具退出：exit code = {code}，耗时 {elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s ====")
                                          RaiseEvent Exited(code, elapsed)
                                      End Sub)
                Return
            Catch ex As Exception
                ' fall through
            End Try
        End If

        RaiseEvent Exited(code, elapsed)
    End Sub

    Private Sub OnLogLine(text As String)
        RaiseEvent LogLine(text)
    End Sub

    Private Sub Emit(text As String)
        Dim owner As Control = _owner

        If owner IsNot Nothing AndAlso Not owner.IsDisposed AndAlso owner.IsHandleCreated Then
            Try
                Call owner.BeginInvoke(Sub() OnLogLine(text))
                Return
            Catch ex As Exception
                ' fall through
            End Try
        End If

        Call OnLogLine(text)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Call StopRunning()

        If _proc IsNot Nothing Then
            Try
                _proc.Dispose()
            Catch ex As Exception
            End Try

            _proc = Nothing
        End If
    End Sub
End Class

''' <summary>
''' 命令行实参拼装辅助：路径引号、不变文化数值、开关与可选项。
''' </summary>
Friend Module CliArg

    ''' <summary>把路径用英文双引号包裹（CLI 侧按空格切分，含空格路径必须加引号）。</summary>
    Public Function Quote(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""

        Dim s As String = path.Trim()

        If s.StartsWith(""""c) AndAlso s.EndsWith(""""c) AndAlso s.Length > 1 Then
            Return s
        End If

        Return """" & s & """"
    End Function

    ''' <summary>
    ''' 数值格式化：一律使用不变文化（CLI 侧以 InvariantCulture 解析），
    ''' 常规量用定点写法便于阅读，极小/极大量退化为科学计数法。
    ''' </summary>
    Public Function Num(value As Double) As String
        If value = 0.0R Then Return "0"

        Dim abs As Double = System.Math.Abs(value)

        If abs < 0.000001R OrElse abs >= 1000000000.0R Then
            Return value.ToString("G", CultureInfo.InvariantCulture)
        End If

        Return value.ToString("0.############", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>NumericUpDown 取值的不变文化文本。</summary>
    Public Function NumText(value As Decimal) As String
        Return Num(CDbl(value))
    End Function

    ''' <summary>开关型参数：启用时输出该开关，否则输出空串。</summary>
    Public Function Flag(name As String, enabled As Boolean) As String
        Return If(enabled, name, "")
    End Function

    ''' <summary>"选项名 值"型参数：值为空时整体不输出。</summary>
    Public Function Opt(name As String, value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return ""

        Return name & " " & Quote(value)
    End Function

    ''' <summary>把若干片段用空格连接起来（自动跳过空片段）。</summary>
    Public Function Join(ParamArray parts As String()) As String
        Dim sb As New StringBuilder()

        For Each part As String In parts
            If String.IsNullOrWhiteSpace(part) Then Continue For

            If sb.Length > 0 Then sb.Append(" "c)

            sb.Append(part)
        Next

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 在候选目录中查找工具 exe：优先应用目录（与 CFD_win32.exe 同目录的 assembly\bin），
    ''' 其次约定的 G:\Moira\assembly\bin。
    ''' </summary>
    Public Function ResolveTool(exeName As String) As String
        Dim candidates As New List(Of String)

        Try
            candidates.Add(Path.Combine(AppContext.BaseDirectory, exeName))
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "..", exeName))
        Catch ex As Exception
        End Try

        candidates.Add(Path.Combine("G:\Moira\assembly\bin", exeName))

        For Each path As String In candidates
            Try
                If File.Exists(path) Then Return Path.GetFullPath(path)
            Catch ex As Exception
            End Try
        Next

        Return exeName
    End Function
End Module
