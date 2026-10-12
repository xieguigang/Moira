' /********************************************************************************/
'
'   FormVoxelizer.vb
'
'   Voxelizer.exe 的图形化封装：把「三维模型 → CFD 体素模型包」的命令行参数
'   （模型路径、输出、体素化器种类、分辨率 / 物理体素尺寸、SDF 亚采样与基准分辨率）
'   收集为界面参数，实时生成命令行预览并以外部进程执行、回显日志。
'
'   参数契约对齐 src\Voxelizer\Program.vb。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO

''' <summary>
''' Voxelizer 工具窗体（三维模型 → CFD 体素模型包）。
''' </summary>
Friend Class FormVoxelizer

    Private Const ToolExeName As String = "Voxelizer.exe"

    Private WithEvents _runner As New CliRunner()
    Private _startedAt As DateTime

    ''' <summary>创建窗体并初始化界面默认值。</summary>
    Public Sub New()
        Call InitializeComponent()
        Call InitUi()
    End Sub

    Private Sub InitUi()
        cboKind.SelectedIndex = 0
        txtExe.Text = CliArg.ResolveTool(ToolExeName)

        Call CliToolLayout.WirePreviewRefresh(Me, AddressOf OnParamChanged)
        Call SyncOptions()
        Call RefreshCommand()
        Call SetStatus("就绪")
    End Sub

    ''' <summary>当前选择的体素化器关键字（standard / sdf / physical / sdfraw）。</summary>
    Private ReadOnly Property KindName As String
        Get
            Select Case cboKind.SelectedIndex
                Case 1
                    Return "sdf"
                Case 2
                    Return "physical"
                Case 3
                    Return "sdfraw"
                Case Else
                    Return "standard"
            End Select
        End Get
    End Property

    ' ---------------------------------------------------------------------------------
    '  参数 → 命令行
    ' ---------------------------------------------------------------------------------

    ''' <summary>按当前界面参数拼出 Voxelizer 的命令行实参串（模型为位置参数，放最前）。</summary>
    Private Function BuildArguments() As String
        Dim parts As New List(Of String)()
        Dim model As String = txtModel.Text.Trim()
        Dim output As String = txtOutput.Text.Trim()

        parts.Add(CliArg.Quote(If(model = "", "<模型文件>", model)))

        If output <> "" Then
            parts.Add("-o " & CliArg.Quote(output))
        End If

        parts.Add("--voxelizer " & KindName)

        ' sdfraw 只接受 --resolution；其余模式下 --voxel-size > 0 时忽略 --resolution
        If KindName <> "sdfraw" AndAlso nudVoxelSize.Value > 0 Then
            parts.Add("--voxel-size " & CliArg.NumText(nudVoxelSize.Value))
        Else
            parts.Add("--resolution " & CliArg.NumText(nudResolution.Value))
        End If

        If KindName = "sdf" OrElse KindName = "sdfraw" Then
            parts.Add("--subsamples " & CliArg.NumText(nudSubSamples.Value))

            If KindName = "sdfraw" AndAlso nudSdfResolution.Value > 0 Then
                parts.Add("--sdf-resolution " & CliArg.NumText(nudSdfResolution.Value))
            End If
        End If

        Return String.Join(" ", parts)
    End Function

    ''' <summary>前置校验：返回错误信息，无错误返回 Nothing。</summary>
    Private Function ValidateInput() As String
        Dim model As String = txtModel.Text.Trim()

        If model = "" Then Return "请先选择模型文件"
        If Not File.Exists(model) Then Return $"模型文件不存在：{model}"

        Dim exe As String = txtExe.Text.Trim()

        If exe = "" OrElse Not File.Exists(exe) Then Return $"未找到工具程序 {ToolExeName}，请手动指定路径"

        If KindName = "sdfraw" AndAlso nudVoxelSize.Value > 0 Then
            Return "sdfraw 不支持 --voxel-size，请改用 --resolution 指定目标分辨率"
        End If

        If nudResolution.Value < 1 Then Return "--resolution 至少为 1"
        If nudSubSamples.Value < 1 Then Return "--subsamples 至少为 1"

        Dim output As String = txtOutput.Text.Trim()

        If output <> "" Then
            Dim dir As String = Path.GetDirectoryName(output)

            If dir <> "" AndAlso Not Directory.Exists(dir) Then Return $"输出目录不存在：{dir}"
        End If

        Return Nothing
    End Function

    Private Sub RefreshCommand()
        If txtCommand Is Nothing Then Return

        txtCommand.Text = ToolExeName & " " & BuildArguments()
    End Sub

    ''' <summary>按体素化器种类联动控件的可用状态。</summary>
    Private Sub SyncOptions()
        Dim kind As String = KindName
        Dim sdfLike As Boolean = (kind = "sdf" OrElse kind = "sdfraw")

        nudVoxelSize.Enabled = (kind <> "sdfraw")
        nudResolution.Enabled = Not (kind <> "sdfraw" AndAlso nudVoxelSize.Value > 0)
        nudSubSamples.Enabled = sdfLike
        nudSdfResolution.Enabled = (kind = "sdfraw")
    End Sub

    Private Sub OnParamChanged(sender As Object, e As EventArgs)
        Call SyncOptions()
        Call RefreshCommand()
    End Sub

    ' ---------------------------------------------------------------------------------
    '  运行控制
    ' ---------------------------------------------------------------------------------

    Private Sub btnRun_Click(sender As Object, e As EventArgs) Handles btnRun.Click
        Dim problem As String = ValidateInput()

        If problem IsNot Nothing Then
            Call SetStatus(problem, CliToolLayout.ColorError)
            Call CliToolLayout.AppendLogLine(txtLog, "error: " & problem)
            Return
        End If

        Dim args As String = BuildArguments()
        Dim output As String = txtOutput.Text.Trim()

        If output <> "" AndAlso output.ToLowerInvariant().EndsWith(".json") Then
            Call CliToolLayout.AppendLogLine(txtLog, "warning: *.json 输出在高分辨率下可能因 GridInfo.Mask 大数组而内存不足，推荐使用 *.voxel.zip")
        End If

        txtLog.Clear()
        _startedAt = DateTime.Now

        Call CliToolLayout.AppendLogLine(txtLog, "$ " & ToolExeName & " " & args)
        Call SetRunning(True)

        If Not _runner.Run(Me, txtExe.Text.Trim(), args, Nothing) Then
            Call SetRunning(False)
            Call SetStatus("启动失败，详见日志", CliToolLayout.ColorError)
        End If
    End Sub

    Private Sub btnStop_Click(sender As Object, e As EventArgs) Handles btnStop.Click
        Call _runner.StopRunning()
        Call SetStatus("已请求终止", CliToolLayout.ColorWarn)
    End Sub

    Private Sub btnClose_Click(sender As Object, e As EventArgs) Handles btnClose.Click
        Call Close()
    End Sub

    Private Sub SetRunning(running As Boolean)
        btnRun.Enabled = Not running
        btnStop.Enabled = running
        btnClose.Enabled = Not running
        flpParams.Enabled = Not running
        btnRun.BackColor = If(running, CliToolLayout.ColorBorder, CliToolLayout.ColorPrimary)

        If running Then
            lblElapsed.Text = "00:00:00"
            Timer1.Start()
            Call SetStatus("运行中…", CliToolLayout.ColorPrimary)
        Else
            Timer1.Stop()
        End If
    End Sub

    Private Sub SetStatus(text As String, Optional color As Color = Nothing)
        lblStatus.Text = text
        lblStatus.ForeColor = If(color.IsEmpty, CliToolLayout.ColorText, color)
    End Sub

    Private Sub _runner_LogLine(text As String) Handles _runner.LogLine
        Call CliToolLayout.AppendLogLine(txtLog, text)
    End Sub

    Private Sub _runner_Exited(exitCode As Integer, elapsed As TimeSpan) Handles _runner.Exited
        Call SetRunning(False)

        lblElapsed.Text = elapsed.ToString("hh\:mm\:ss")

        If exitCode = 0 Then
            Call SetStatus("完成（exit code = 0）", CliToolLayout.ColorOk)
        Else
            Call SetStatus($"失败（exit code = {exitCode}），请查看日志", CliToolLayout.ColorError)
        End If
    End Sub

    Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
        lblElapsed.Text = (DateTime.Now - _startedAt).ToString("hh\:mm\:ss")
    End Sub

    ' ---------------------------------------------------------------------------------
    '  文件浏览
    ' ---------------------------------------------------------------------------------

    Private Sub btnModel_Click(sender As Object, e As EventArgs) Handles btnModel.Click
        Using dlg As New OpenFileDialog() With {
            .Filter = "3D 模型文件|*.stl;*.obj;*.gltf;*.glb;*.dae;*.3ds;*.3mf|所有文件|*.*",
            .Title = "选择三维模型文件"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtModel.Text = dlg.FileName

                If txtOutput.Text.Trim() = "" Then
                    txtOutput.Text = Path.ChangeExtension(dlg.FileName, ".voxel.zip")
                End If
            End If
        End Using
    End Sub

    Private Sub btnOutput_Click(sender As Object, e As EventArgs) Handles btnOutput.Click
        Using dlg As New SaveFileDialog() With {
            .Filter = "体素模型包|*.voxel.zip|GridInfo JSON|*.json|所有文件|*.*",
            .Title = "输出体素模型文件",
            .FileName = If(txtOutput.Text.Trim() = "",
                           Path.ChangeExtension(txtModel.Text.Trim(), ".voxel.zip"),
                           txtOutput.Text.Trim())
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtOutput.Text = dlg.FileName
            End If
        End Using
    End Sub

    Private Sub btnExe_Click(sender As Object, e As EventArgs) Handles btnExe.Click
        Using dlg As New OpenFileDialog() With {
            .Filter = "可执行程序|*.exe|所有文件|*.*",
            .Title = $"选择 {ToolExeName}"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtExe.Text = dlg.FileName
            End If
        End Using
    End Sub

    Private Sub btnCopy_Click(sender As Object, e As EventArgs) Handles btnCopy.Click
        Clipboard.SetText(txtCommand.Text)
        Call SetStatus("命令行已复制到剪贴板", CliToolLayout.ColorPrimary)
    End Sub

    Private Sub btnOpenOutput_Click(sender As Object, e As EventArgs) Handles btnOpenOutput.Click
        Dim target As String = txtOutput.Text.Trim()

        If target = "" AndAlso txtModel.Text.Trim() <> "" Then
            target = Path.ChangeExtension(txtModel.Text.Trim(), ".voxel.zip")
        End If

        If target <> "" AndAlso File.Exists(target) Then
            Call Process.Start(New ProcessStartInfo("explorer.exe") With {.Arguments = $"/select, ""{target}"""})
        ElseIf target <> "" AndAlso Directory.Exists(Path.GetDirectoryName(target)) Then
            Call Process.Start(New ProcessStartInfo("explorer.exe") With {.Arguments = Path.GetDirectoryName(target)})
        Else
            Call SetStatus("输出文件尚未生成", CliToolLayout.ColorWarn)
        End If
    End Sub

    Private Sub FormVoxelizer_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If _runner.IsRunning Then
            If MessageBox.Show(Me, "工具正在运行，确定要终止进程并关闭窗口吗？", "Voxelizer",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.No Then
                e.Cancel = True
                Return
            End If

            Call _runner.StopRunning()
        End If

        Call _runner.Dispose()
    End Sub
End Class
