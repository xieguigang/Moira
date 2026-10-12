' /********************************************************************************/
'
'   FormEulerianMesh.vb
'
'   EulerianMesh.exe 的图形化封装：把「三维模型 → CFD Eulerian 网格包」的
'   全部命令行参数（maxres / convert 两套子命令）收集为界面参数，
'   实时生成命令行预览，并以外部进程方式执行、回显日志。
'
'   参数契约对齐 src\EulerianMesh\Program.vb。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO

''' <summary>
''' EulerianMesh 工具窗体（三维模型 → CFD Eulerian 网格包）。
''' </summary>
Friend Class FormEulerianMesh

    Private Const ToolExeName As String = "EulerianMesh.exe"

    Private WithEvents _runner As New CliRunner()
    Private ReadOnly _patches As New List(Of String)()
    Private _startedAt As DateTime

    ''' <summary>创建窗体并初始化界面默认值。</summary>
    Public Sub New()
        Call InitializeComponent()
        Call InitUi()
    End Sub

    Private Sub InitUi()
        cboCommand.SelectedIndex = 0
        cboType.SelectedIndex = 0
        cboMode.SelectedIndex = 0
        cboPatchType.SelectedIndex = 0
        cboPatchFace.SelectedIndex = 0
        txtExe.Text = CliArg.ResolveTool(ToolExeName)

        ' 命令行预览框与日志框不参与「变更 → 刷新预览」的接线（避免自触发递归）
        Call CliToolLayout.WirePreviewRefresh(Me, AddressOf OnParamChanged, txtCommand, txtLog)
        Call SyncCommandPages()
        Call RefreshCommand()
        Call SetStatus("就绪")
    End Sub

    Private ReadOnly Property IsMaxresCmd As Boolean
        Get
            Return cboCommand.SelectedIndex = 0
        End Get
    End Property

    ' ---------------------------------------------------------------------------------
    '  参数 → 命令行
    ' ---------------------------------------------------------------------------------

    ''' <summary>按当前界面参数拼出 EulerianMesh 的命令行实参串。</summary>
    Private Function BuildArguments() As String
        Dim parts As New List(Of String)()

        parts.Add(If(IsMaxresCmd, "maxres", "convert"))

        Dim input As String = txtInput.Text.Trim()
        Dim output As String = txtOutput.Text.Trim()

        parts.Add("--input " & CliArg.Quote(If(input = "", "<模型文件>", input)))

        If output <> "" Then
            parts.Add("--output " & CliArg.Quote(output))
        End If

        If IsMaxresCmd Then
            parts.Add("--cells " & CliArg.NumText(nudCells.Value))
            parts.Add("--max-depth " & CliArg.NumText(nudDepth.Value))
            parts.Add("--domain-scale " & CliArg.NumText(nudDomainScale.Value))
            parts.Add("--voxel-ratio " & CliArg.NumText(nudVoxelRatio.Value))
            parts.Add("--max-voxels " & CliArg.NumText(nudMaxVoxels.Value))

            If chkPlan.Checked Then parts.Add("--plan")
        Else
            parts.Add("--type " & cboType.Text)
            parts.Add("--resolution " & CliArg.NumText(nudResolution.Value))
            parts.Add("--domain-scale " & CliArg.NumText(nudDomainScaleC.Value))

            If nudPoisson.Value > 0 Then
                parts.Add("--poisson " & CliArg.NumText(nudPoisson.Value))
            End If

            If chkCutCells.Checked Then parts.Add("--cut-cells")
            If chkNoSmooth.Checked Then parts.Add("--no-smooth")
        End If

        parts.Add("--mode " & If(cboMode.SelectedIndex = 1, "internal", "external"))

        For Each line As String In txtSolids.Lines
            Dim solid As String = line.Trim()

            If solid = "" Then Continue For

            parts.Add("--internal-solid " & CliArg.Quote(solid))
        Next

        For Each rule As String In _patches
            parts.Add("--patch " & CliArg.Quote(rule))
        Next

        If chkGpu.Checked Then parts.Add("--gpu")

        Return String.Join(" ", parts)
    End Function

    ''' <summary>前置校验：返回错误信息，无错误返回 Nothing。</summary>
    Private Function ValidateInput() As String
        Dim input As String = txtInput.Text.Trim()

        If input = "" Then Return "请先选择输入模型文件"
        If Not File.Exists(input) Then Return $"输入模型文件不存在：{input}"

        Dim exe As String = txtExe.Text.Trim()

        If exe = "" OrElse Not File.Exists(exe) Then Return $"未找到工具程序 {ToolExeName}，请手动指定路径"

        If IsMaxresCmd Then
            If nudCells.Value < 8 Then Return "--cells 叶子预算至少为 8"
            If nudVoxelRatio.Value <= 0 Then Return "--voxel-ratio 必须大于 0"
            If nudDomainScale.Value < 1 Then Return "--domain-scale 不能小于 1"
        Else
            If nudResolution.Value <= 0 OrElse nudResolution.Value > 1 Then Return "--resolution 必须在 (0, 1] 区间"
            If nudDomainScaleC.Value < 1 Then Return "--domain-scale 不能小于 1"
        End If

        Return Nothing
    End Function

    Private Sub RefreshCommand()
        If txtCommand Is Nothing Then Return

        txtCommand.Text = ToolExeName & " " & BuildArguments()
    End Sub

    Private Sub SyncCommandPages()
        grpMaxres.Visible = IsMaxresCmd
        grpConvert.Visible = Not IsMaxresCmd
    End Sub

    Private Sub OnParamChanged(sender As Object, e As EventArgs)
        Call SyncCommandPages()
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
    '  文件浏览与 Patch 编辑
    ' ---------------------------------------------------------------------------------

    Private Sub btnInput_Click(sender As Object, e As EventArgs) Handles btnInput.Click
        Using dlg As New OpenFileDialog() With {
            .Filter = "3D 模型文件|*.stl;*.obj;*.gltf;*.glb;*.dae;*.3ds;*.3mf|所有文件|*.*",
            .Title = "选择三维模型文件"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtInput.Text = dlg.FileName

                If txtOutput.Text.Trim() = "" Then
                    txtOutput.Text = Path.ChangeExtension(dlg.FileName, DefaultOutputExtension())
                End If
            End If
        End Using
    End Sub

    Private Sub btnOutput_Click(sender As Object, e As EventArgs) Handles btnOutput.Click
        Using dlg As New SaveFileDialog() With {
            .Filter = "Eulerian 网格包|*.eulerian.zip|所有文件|*.*",
            .Title = "输出 Eulerian 网格包",
            .FileName = If(txtOutput.Text.Trim() = "",
                           Path.ChangeExtension(txtInput.Text.Trim(), DefaultOutputExtension()),
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

    Private Function DefaultOutputExtension() As String
        Return If(IsMaxresCmd, ".maxres.eulerian.zip", ".eulerian.zip")
    End Function

    Private Sub btnPatchAdd_Click(sender As Object, e As EventArgs) Handles btnPatchAdd.Click
        Dim name As String = txtPatchName.Text.Trim()

        If name = "" Then
            Call SetStatus("请先填写 Patch 名称", CliToolLayout.ColorWarn)
            Return
        End If

        Dim rule As String = $"{name}:{cboPatchType.Text}:{cboPatchFace.Text}"

        If _patches.Contains(rule) Then Return

        _patches.Add(rule)
        lstPatches.Items.Add(rule)
        txtPatchName.Clear()

        Call RefreshCommand()
    End Sub

    Private Sub btnPatchRemove_Click(sender As Object, e As EventArgs) Handles btnPatchRemove.Click
        If lstPatches.SelectedIndex < 0 Then Return

        _patches.RemoveAt(lstPatches.SelectedIndex)
        lstPatches.Items.RemoveAt(lstPatches.SelectedIndex)

        Call RefreshCommand()
    End Sub

    Private Sub btnCopy_Click(sender As Object, e As EventArgs) Handles btnCopy.Click
        Clipboard.SetText(txtCommand.Text)
        Call SetStatus("命令行已复制到剪贴板", CliToolLayout.ColorPrimary)
    End Sub

    Private Sub btnOpenOutput_Click(sender As Object, e As EventArgs) Handles btnOpenOutput.Click
        Dim target As String = txtOutput.Text.Trim()

        If target = "" AndAlso txtInput.Text.Trim() <> "" Then
            target = Path.ChangeExtension(txtInput.Text.Trim(), DefaultOutputExtension())
        End If

        If target <> "" AndAlso File.Exists(target) Then
            Call Process.Start(New ProcessStartInfo("explorer.exe") With {.Arguments = $"/select, ""{target}"""})
        ElseIf target <> "" AndAlso Directory.Exists(Path.GetDirectoryName(target)) Then
            Call Process.Start(New ProcessStartInfo("explorer.exe") With {.Arguments = Path.GetDirectoryName(target)})
        Else
            Call SetStatus("输出文件尚未生成", CliToolLayout.ColorWarn)
        End If
    End Sub

    Private Sub FormEulerianMesh_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If _runner.IsRunning Then
            If MessageBox.Show(Me, "工具正在运行，确定要终止进程并关闭窗口吗？", "Eulerian Mesh",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.No Then
                e.Cancel = True
                Return
            End If

            Call _runner.StopRunning()
        End If

        Call _runner.Dispose()
    End Sub
End Class
