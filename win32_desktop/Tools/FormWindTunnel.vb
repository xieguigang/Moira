' /********************************************************************************/
'
'   FormWindTunnel.vb
'
'   WindTunnel.exe 的图形化封装：把 Eulerian FVM 风洞 / 发酵罐模拟的命令行参数
'   （网格、输出、格式、物理与湍流、来流与物性、求解控制、探针、tank 几何与搅拌）
'   收集为界面参数，实时生成命令行预览并以外部进程执行、回显日志。
'
'   默认值严格取自 src\WindTunnel\CliOptions.vb；与默认值相同的高级参数不写入命令行，
'   以保证命令行简洁可读（行为完全等价）。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO

''' <summary>
''' WindTunnel 工具窗体（CFD 模型文件 → 风洞绕流 / 发酵罐搅拌模拟）。
''' </summary>
Friend Class FormWindTunnel

    Private Const ToolExeName As String = "WindTunnel.exe"

    Private WithEvents _runner As New CliRunner()
    Private _startedAt As DateTime

    ''' <summary>创建窗体并初始化界面默认值。</summary>
    Public Sub New()
        Call InitializeComponent()
        Call InitUi()
    End Sub

    Private Sub InitUi()
        cboCommand.SelectedIndex = 0
        cboFormat.SelectedIndex = 0
        cboPhysics.SelectedIndex = 0
        cboTurbulence.SelectedIndex = 0
        cboSolver.SelectedIndex = 0
        cboScheme.SelectedIndex = 0
        cboFlux.SelectedIndex = 0
        txtExe.Text = CliArg.ResolveTool(ToolExeName)

        Call CliToolLayout.WirePreviewRefresh(Me, AddressOf OnParamChanged, txtCommand, txtLog)
        Call SyncCommandPages()
        Call RefreshCommand()
        Call SetStatus("就绪")
    End Sub

    ' ---------------------------------------------------------------------------------
    '  取值
    ' ---------------------------------------------------------------------------------

    Private ReadOnly Property IsTank As Boolean
        Get
            Return cboCommand.SelectedIndex = 1
        End Get
    End Property

    Private ReadOnly Property FormatName As String
        Get
            Select Case cboFormat.SelectedIndex
                Case 1 : Return "vti"
                Case 2 : Return "both"
                Case Else : Return "vtu"
            End Select
        End Get
    End Property

    Private ReadOnly Property PhysicsName As String
        Get
            Return If(cboPhysics.SelectedIndex = 1, "compressible", "incompressible")
        End Get
    End Property

    Private ReadOnly Property TurbulenceName As String
        Get
            Select Case cboTurbulence.SelectedIndex
                Case 1 : Return "sst"
                Case 2 : Return "none"
                Case Else : Return "ke"
            End Select
        End Get
    End Property

    Private ReadOnly Property SolverName As String
        Get
            Return If(cboSolver.SelectedIndex = 1, "pimple", "simple")
        End Get
    End Property

    Private ReadOnly Property SchemeName As String
        Get
            Select Case cboScheme.SelectedIndex
                Case 1 : Return "central"
                Case 2 : Return "linear-upwind"
                Case 3 : Return "quick"
                Case Else : Return "upwind"
            End Select
        End Get
    End Property

    Private ReadOnly Property FluxName As String
        Get
            Return If(cboFlux.SelectedIndex = 1, "HLLC", "AUSM+")
        End Get
    End Property

    ' ---------------------------------------------------------------------------------
    '  参数 → 命令行
    ' ---------------------------------------------------------------------------------

    ''' <summary>数值型参数：仅当与默认值不同时才写入命令行。</summary>
    Private Shared Sub AddNum(parts As List(Of String), name As String, nud As NumericUpDown, def As Double)
        If CDbl(nud.Value) <> def Then
            parts.Add(name & " " & CliArg.NumText(nud.Value))
        End If
    End Sub

    ''' <summary>枚举/文本型参数：仅当与默认值不同时才写入命令行。</summary>
    Private Shared Sub AddIf(parts As List(Of String), name As String, value As String, def As String)
        If value <> def Then
            parts.Add(name & " " & value)
        End If
    End Sub

    ''' <summary>按当前界面参数拼出 WindTunnel 的命令行实参串。</summary>
    Private Function BuildArguments() As String
        Dim parts As New List(Of String)()
        Dim mesh As String = txtMesh.Text.Trim()
        Dim outDir As String = txtOutDir.Text.Trim()

        If IsTank Then
            parts.Add("tank")
        Else
            parts.Add("windtunnel")
        End If

        If IsTank Then
            If mesh <> "" Then parts.Add("--mesh " & CliArg.Quote(mesh))
        Else
            parts.Add("--mesh " & CliArg.Quote(If(mesh = "", "<eulerian.zip>", mesh)))
        End If

        If outDir <> "" Then parts.Add("--out " & CliArg.Quote(outDir))

        Call AddIf(parts, "--format", FormatName, "vtu")
        Call AddIf(parts, "--physics", PhysicsName, "incompressible")
        Call AddIf(parts, "--turbulence", TurbulenceName, "ke")
        Call AddIf(parts, "--solver", SolverName, "simple")
        Call AddIf(parts, "--scheme", SchemeName, "upwind")

        If PhysicsName = "compressible" Then
            parts.Add("--flux " & FluxName)
        End If

        If Not IsTank Then
            Call AddNum(parts, "--inlet-u", nudInletU, 30.0R)

            If CDbl(nudDirX.Value) <> 1.0R OrElse CDbl(nudDirY.Value) <> 0.0R OrElse CDbl(nudDirZ.Value) <> 0.0R Then
                parts.Add("--inlet-dir " & CliArg.NumText(nudDirX.Value) & "," & CliArg.NumText(nudDirY.Value) & "," & CliArg.NumText(nudDirZ.Value))
            End If
        End If

        Call AddNum(parts, "--rho", nudRho, 1.225R)
        Call AddNum(parts, "--nu", nudNu, 0.000015R)
        Call AddNum(parts, "--p0", nudP0, 101325.0R)
        Call AddNum(parts, "--t0", nudT0, 288.15R)

        If PhysicsName = "compressible" Then
            Call AddNum(parts, "--mach", nudMach, 0.1R)
        End If

        Call AddNum(parts, "--ti", nudTi, 0.01R)
        Call AddNum(parts, "--tl", nudTl, 0.0R)

        parts.Add("--steps " & CliArg.NumText(nudSteps.Value))

        Call AddNum(parts, "--dt", nudDt, 0.0R)
        Call AddNum(parts, "--cfl", nudCfl, 0.8R)
        Call AddNum(parts, "--relax-u", nudRelaxU, 0.7R)
        Call AddNum(parts, "--p-iter", nudPIter, 200.0R)
        Call AddNum(parts, "--interval", nudInterval, 10.0R)

        If Not IsTank Then
            Call AddNum(parts, "--ref-area", nudRefArea, 0.0R)
            Call AddNum(parts, "--ref-length", nudRefLength, 0.0R)
        End If

        If IsTank Then
            parts.Add("--diameter " & CliArg.NumText(nudDiameter.Value))
            parts.Add("--rpm " & CliArg.NumText(nudRpm.Value))
            parts.Add("--resolution " & CliArg.NumText(nudRes.Value))
            parts.Add(If(chkCutCells.Checked, "--cut-cells", "--no-cut-cells"))

            Call AddNum(parts, "--height", nudHeight, 0.0R)
            Call AddNum(parts, "--liquid-level", nudLevel, 0.0R)
            Call AddNum(parts, "--drive", nudDrive, 0.0R)

            If txtGeoDir.Text.Trim() <> "" Then parts.Add("--geometry-dir " & CliArg.Quote(txtGeoDir.Text.Trim()))
            If txtMeshOut.Text.Trim() <> "" Then parts.Add("--mesh-out " & CliArg.Quote(txtMeshOut.Text.Trim()))
        End If

        parts.Add(If(chkGpu.Checked, "--gpu", "--no-gpu"))

        If Not chkReport.Checked Then parts.Add("--no-report")
        If chkVerbose.Checked Then parts.Add("--verbose")
        If Not chkTransient.Checked Then parts.Add("--steady")

        For Each line As String In txtProbes.Lines
            Dim probe As String = line.Trim()

            If probe = "" Then Continue For

            parts.Add("--probe " & probe)
        Next

        Return String.Join(" ", parts)
    End Function

    ''' <summary>前置校验：返回错误信息，无错误返回 Nothing。</summary>
    Private Function ValidateInput() As String
        Dim exe As String = txtExe.Text.Trim()

        If exe = "" OrElse Not File.Exists(exe) Then Return $"未找到工具程序 {ToolExeName}，请手动指定路径"

        Dim mesh As String = txtMesh.Text.Trim()

        If Not IsTank Then
            If mesh = "" Then Return "windtunnel 子命令必须提供 CFD 网格包（--mesh）"
            If Not File.Exists(mesh) Then Return $"网格包不存在：{mesh}"
        ElseIf mesh <> "" AndAlso Not File.Exists(mesh) Then
            Return $"网格包不存在：{mesh}"
        End If

        If nudSteps.Value < 1 Then Return "--steps 至少为 1"
        If nudInterval.Value < 1 Then Return "--interval 至少为 1"
        If Not IsTank AndAlso nudInletU.Value < 0 Then Return "--inlet-u 不能为负"

        Return Nothing
    End Function

    Private Sub RefreshCommand()
        If txtCommand Is Nothing Then Return

        txtCommand.Text = ToolExeName & " " & BuildArguments()
    End Sub

    Private Sub SyncCommandPages()
        grpTank.Visible = IsTank
        grpFlow.Visible = Not IsTank
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

        Dim outDir As String = txtOutDir.Text.Trim()

        If outDir <> "" AndAlso Not Directory.Exists(outDir) Then
            Try
                Call Directory.CreateDirectory(outDir)
                Call CliToolLayout.AppendLogLine(txtLog, $"已创建输出目录：{outDir}")
            Catch ex As Exception
                Call SetStatus($"无法创建输出目录：{ex.Message}", CliToolLayout.ColorError)
                Return
            End Try
        End If

        Dim args As String = BuildArguments()

        txtLog.Clear()
        _startedAt = DateTime.Now

        Call CliToolLayout.AppendLogLine(txtLog, "$ " & ToolExeName & " " & args)
        Call SetRunning(True)

        Dim workDir As String = If(outDir <> "" AndAlso Directory.Exists(outDir), outDir, Nothing)

        If Not _runner.Run(Me, txtExe.Text.Trim(), args, workDir) Then
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
            Call SetStatus("完成（exit code = 0），输出见 --out 目录", CliToolLayout.ColorOk)
        Else
            Call SetStatus($"失败（exit code = {exitCode}），请查看日志", CliToolLayout.ColorError)
        End If
    End Sub

    Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
        lblElapsed.Text = (DateTime.Now - _startedAt).ToString("hh\:mm\:ss")
    End Sub

    ' ---------------------------------------------------------------------------------
    '  文件 / 目录浏览
    ' ---------------------------------------------------------------------------------

    Private Sub btnMesh_Click(sender As Object, e As EventArgs) Handles btnMesh.Click
        Using dlg As New OpenFileDialog() With {
            .Filter = "Eulerian 网格包|*.eulerian.zip|所有文件|*.*",
            .Title = "选择 CFD 网格包"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtMesh.Text = dlg.FileName

                If txtOutDir.Text.Trim() = "" Then
                    txtOutDir.Text = Path.Combine(Path.GetDirectoryName(dlg.FileName), "windtunnel_out")
                End If
            End If
        End Using
    End Sub

    Private Sub btnOutDir_Click(sender As Object, e As EventArgs) Handles btnOutDir.Click
        Using dlg As New FolderBrowserDialog() With {
            .Description = "选择输出目录（frame_*.vtu / animation.pvd / summary.json）",
            .ShowNewFolderButton = True
        }
            If txtOutDir.Text.Trim() <> "" Then dlg.SelectedPath = txtOutDir.Text.Trim()

            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtOutDir.Text = dlg.SelectedPath
            End If
        End Using
    End Sub

    Private Sub btnGeoDir_Click(sender As Object, e As EventArgs) Handles btnGeoDir.Click
        Using dlg As New FolderBrowserDialog() With {
            .Description = "选择 STL 几何输出目录",
            .ShowNewFolderButton = True
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtGeoDir.Text = dlg.SelectedPath
            End If
        End Using
    End Sub

    Private Sub btnMeshOut_Click(sender As Object, e As EventArgs) Handles btnMeshOut.Click
        Using dlg As New SaveFileDialog() With {
            .Filter = "Eulerian 网格包|*.eulerian.zip|所有文件|*.*",
            .Title = "网格另存为"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtMeshOut.Text = dlg.FileName
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
        Dim dir As String = txtOutDir.Text.Trim()

        If dir = "" AndAlso txtMesh.Text.Trim() <> "" Then
            dir = Path.GetDirectoryName(txtMesh.Text.Trim())
        End If

        If dir <> "" AndAlso Directory.Exists(dir) Then
            Call Process.Start(New ProcessStartInfo("explorer.exe") With {.Arguments = dir})
        Else
            Call SetStatus("输出目录尚未生成", CliToolLayout.ColorWarn)
        End If
    End Sub

    Private Sub FormWindTunnel_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If _runner.IsRunning Then
            If MessageBox.Show(Me, "模拟正在运行，确定要终止进程并关闭窗口吗？", "WindTunnel",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.No Then
                e.Cancel = True
                Return
            End If

            Call _runner.StopRunning()
        End If

        Call _runner.Dispose()
    End Sub
End Class
