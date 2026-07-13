Imports CFD_win32.RibbonLib.Controls
Imports Galaxy.Workbench
Imports Galaxy.Workbench.CommonDialogs
Imports Microsoft.VisualStudio.WinForms.Docking
Imports RibbonLib
Imports ThemeVS2015

Public Class FormMain : Implements AppHost

    Dim ribbon1 As New Ribbon
    Dim vsToolStripExtender1 As New VisualStudioToolStripExtender
    Dim vS2015LightTheme1 As New VS2015LightTheme
    Dim dockPanel As New DockPanel

    ReadOnly _toolStripProfessionalRenderer As New ToolStripProfessionalRenderer()
    Public Event ResizeForm As AppHost.ResizeFormEventHandler Implements AppHost.ResizeForm
    Public Event CloseWorkbench As AppHost.CloseWorkbenchEventHandler Implements AppHost.CloseWorkbench
    Private ReadOnly Property AppHost_ClientRectangle As Rectangle Implements AppHost.ClientRectangle
        Get
            Return New Rectangle(Location, Size)
        End Get
    End Property

    Public ReadOnly Property ActiveDocument As Form Implements AppHost.ActiveDocument
        Get
            Return DirectCast(dockPanel.ActiveDocument, Form)
        End Get
    End Property

    Sub New()

        ' 此调用是设计器所必需的。
        InitializeComponent()

        Me.Controls.Add(ribbon1)
        Me.Controls.Add(dockPanel)

        dockPanel.Dock = DockStyle.Fill
        dockPanel.ShowDocumentIcon = True
        dockPanel.DockLeftPortion = 250.0R

        vsToolStripExtender1.DefaultRenderer = _toolStripProfessionalRenderer

        ' 在 InitializeComponent() 调用之后添加任何初始化。
        ribbon1.ResourceName = $"CFD_win32.RibbonMarkup.ribbon"
        ribbon1.Dock = DockStyle.Top
        ribbon1.Height = 100
        ribbon1.SendToBack()
        ribbonItems = New RibbonItems(ribbon1)

        dockPanel.BringToFront()

        Globals.ribbonItems = ribbonItems
        Globals.dockPanel = dockPanel
        Globals.main = Me

        Call AppEnvironment.StartGlobalHttp()
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        dockPanel.Theme = vS2015LightTheme1
        EnableVSRenderer(StatusStrip1)

        Call RibbonMenu.Setup(ribbonItems)
        Call CommonRuntime.Hook(Me)
        Call Globals.SetupBackendUI()
    End Sub

    Friend Sub EnableVSRenderer(ParamArray toolStrips As ToolStrip())
        For Each tool In toolStrips
            vsToolStripExtender1.SetStyle(tool, VisualStudioToolStripExtender.VsVersion.Vs2015, vS2015LightTheme1)
        Next
    End Sub

    Private Sub CreateNewSimulation()
        Dim wizard As New FormProjectWizard()

        If wizard.ShowDialog() = DialogResult.OK Then
            Using folder As New FolderBrowserDialog With {
                .ShowNewFolderButton = True
            }
                If folder.ShowDialog = DialogResult.OK Then
                    Dim pars = wizard.GetParameters(folder.SelectedPath)
                    Dim CFD As New frmCFDCanvas With {.setup = pars}

                    Call CommonRuntime.ShowDocument(CFD)
                End If
            End Using
        End If
    End Sub

    Private Sub FormMain_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        CommonRuntime.SaveUISettings()
        Globals.settings.Save()
    End Sub

    Public Sub SetWorkbenchVisible(visible As Boolean) Implements AppHost.SetWorkbenchVisible
        Me.Visible = visible
    End Sub

    Public Sub SetWindowState(stat As FormWindowState) Implements AppHost.SetWindowState
        Me.WindowState = stat
    End Sub

    Public Function GetDesktopLocation() As Point Implements AppHost.GetDesktopLocation
        Return Location
    End Function

    Public Function GetClientSize() As Size Implements AppHost.GetClientSize
        Return Size
    End Function

    Public Function GetDocuments() As IEnumerable(Of Form) Implements AppHost.GetDocuments
        Return dockPanel.Documents.OfType(Of Form)
    End Function

    Public Function GetDockPanel() As Control Implements AppHost.GetDockPanel
        Return DirectCast(dockPanel, Control)
    End Function

    Public Function GetWindowState() As FormWindowState Implements AppHost.GetWindowState
        Return WindowState
    End Function

    Public Sub SetTitle(title As String) Implements AppHost.SetTitle
        Me.Text = title
    End Sub

    Public Sub StatusMessage(msg As String, Optional icon As Image = Nothing) Implements AppHost.StatusMessage
        Call Me.Invoke(
            Sub()
                ToolStripStatusLabel1.Text = msg
                ToolStripStatusLabel1.Image = icon
            End Sub)
    End Sub

    Public Sub Warning(msg As String) Implements AppHost.Warning
        Call StatusMessage(msg, Icons8.Warning)
    End Sub

    Public Sub LogText(text As String) Implements AppHost.LogText
        Call CommonRuntime.GetOutputWindow.AppendLine(text)
    End Sub

    Public Sub ShowProperties(obj As Object) Implements AppHost.ShowProperties
        Call CommonRuntime.GetPropertyWindow.SetObject(obj)
    End Sub
End Class
