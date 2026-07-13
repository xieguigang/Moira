Imports CFD_win32.RibbonLib.Controls
Imports Galaxy.Workbench
Imports Microsoft.VisualStudio.WinForms.Docking
Imports ThemeVS2015

Public Class FormMain : Implements AppHost

    ReadOnly vS2015LightTheme1 As New VS2015LightTheme
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
            Return DirectCast(DockPanel1.ActiveDocument, Form)
        End Get
    End Property

    Sub New()
        ' 此调用是设计器所必需的。
        InitializeComponent()
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        DockPanel1.Theme = vS2015LightTheme1
        VisualStudioToolStripExtender1.DefaultRenderer = _toolStripProfessionalRenderer

        Call AppEnvironment.StartGlobalHttp()
        Call EnableVSRenderer(StatusStrip1)
        Call RibbonMenu.Setup(New RibbonItems(Ribbon1))
        Call CommonRuntime.Hook(Me)
        Call Globals.SetupBackendUI()
    End Sub

    Friend Sub EnableVSRenderer(ParamArray toolStrips As ToolStrip())
        For Each tool In toolStrips
            VisualStudioToolStripExtender1.SetStyle(tool, VisualStudioToolStripExtender.VsVersion.Vs2015, vS2015LightTheme1)
        Next
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
        Return DockPanel1.Documents.OfType(Of Form)
    End Function

    Public Function GetDockPanel() As Control Implements AppHost.GetDockPanel
        Return DirectCast(DockPanel1, Control)
    End Function

    Public Function GetWindowState() As FormWindowState Implements AppHost.GetWindowState
        Return WindowState
    End Function

    Public Sub SetTitle(title As String) Implements AppHost.SetTitle
        Call Invoke(Sub() Me.Text = title)
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
