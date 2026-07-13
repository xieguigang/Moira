Imports RibbonLib

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormMain
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FormMain))
        Timer1 = New Timer(components)
        StatusStrip1 = New StatusStrip()
        ToolStripStatusLabel1 = New ToolStripStatusLabel()
        ToolStripStatusLabel2 = New ToolStripStatusLabel()
        ToolStripProgressBar1 = New ToolStripProgressBar()
        ToolStripStatusLabel3 = New ToolStripStatusLabel()
        ToolStripStatusLabel4 = New ToolStripStatusLabel()
        ToolTip1 = New ToolTip(components)
        Ribbon1 = New Ribbon()
        DockPanel1 = New Microsoft.VisualStudio.WinForms.Docking.DockPanel()
        VisualStudioToolStripExtender1 = New Microsoft.VisualStudio.WinForms.Docking.VisualStudioToolStripExtender(components)
        StatusStrip1.SuspendLayout()
        SuspendLayout()
        ' 
        ' Timer1
        ' 
        Timer1.Enabled = True
        Timer1.Interval = 30
        ' 
        ' StatusStrip1
        ' 
        StatusStrip1.Items.AddRange(New ToolStripItem() {ToolStripStatusLabel1, ToolStripStatusLabel2, ToolStripProgressBar1, ToolStripStatusLabel3, ToolStripStatusLabel4})
        StatusStrip1.Location = New Point(0, 441)
        StatusStrip1.Name = "StatusStrip1"
        StatusStrip1.Size = New Size(933, 22)
        StatusStrip1.TabIndex = 1
        StatusStrip1.Text = "StatusStrip1"
        ' 
        ' ToolStripStatusLabel1
        ' 
        ToolStripStatusLabel1.Image = My.Resources.Resources.icons8_backlog_96
        ToolStripStatusLabel1.Name = "ToolStripStatusLabel1"
        ToolStripStatusLabel1.Size = New Size(58, 17)
        ToolStripStatusLabel1.Text = "Ready!"
        ' 
        ' ToolStripStatusLabel2
        ' 
        ToolStripStatusLabel2.Name = "ToolStripStatusLabel2"
        ToolStripStatusLabel2.Size = New Size(40, 17)
        ToolStripStatusLabel2.Text = "[-1,-1]"
        ' 
        ' ToolStripProgressBar1
        ' 
        ToolStripProgressBar1.Name = "ToolStripProgressBar1"
        ToolStripProgressBar1.Size = New Size(100, 16)
        ' 
        ' ToolStripStatusLabel3
        ' 
        ToolStripStatusLabel3.Name = "ToolStripStatusLabel3"
        ToolStripStatusLabel3.Size = New Size(665, 17)
        ToolStripStatusLabel3.Spring = True
        ' 
        ' ToolStripStatusLabel4
        ' 
        ToolStripStatusLabel4.Name = "ToolStripStatusLabel4"
        ToolStripStatusLabel4.Size = New Size(53, 17)
        ToolStripStatusLabel4.Text = "Licensed"
        ' 
        ' ToolTip1
        ' 
        ToolTip1.ToolTipIcon = ToolTipIcon.Info
        ToolTip1.ToolTipTitle = "Point Information"
        ' 
        ' Ribbon1
        ' 
        Ribbon1.Location = New Point(0, 0)
        Ribbon1.Name = "Ribbon1"
        Ribbon1.ResourceIdentifier = Nothing
        Ribbon1.ResourceName = "CFD_win32.RibbonMarkup.ribbon"
        Ribbon1.ShortcutTableResourceName = Nothing
        Ribbon1.Size = New Size(933, 116)
        Ribbon1.TabIndex = 2
        ' 
        ' DockPanel1
        ' 
        DockPanel1.Dock = DockStyle.Fill
        DockPanel1.Location = New Point(0, 116)
        DockPanel1.Name = "DockPanel1"
        DockPanel1.Size = New Size(933, 325)
        DockPanel1.TabIndex = 3
        ' 
        ' VisualStudioToolStripExtender1
        ' 
        VisualStudioToolStripExtender1.DefaultRenderer = Nothing
        ' 
        ' FormMain
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(933, 463)
        Controls.Add(DockPanel1)
        Controls.Add(Ribbon1)
        Controls.Add(StatusStrip1)
        Icon = CType(resources.GetObject("$this.Icon"), Icon)
        Name = "FormMain"
        StartPosition = FormStartPosition.Manual
        Text = "Moira Workshop 2026"
        StatusStrip1.ResumeLayout(False)
        StatusStrip1.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub
    Friend WithEvents Timer1 As Timer
    Friend WithEvents StatusStrip1 As StatusStrip
    Friend WithEvents ToolStripStatusLabel1 As ToolStripStatusLabel
    Friend WithEvents ToolStripProgressBar1 As ToolStripProgressBar
    Friend WithEvents ToolStripStatusLabel2 As ToolStripStatusLabel
    Friend WithEvents ToolTip1 As ToolTip
    Friend WithEvents ToolStripStatusLabel3 As ToolStripStatusLabel
    Friend WithEvents ToolStripStatusLabel4 As ToolStripStatusLabel
    Friend WithEvents Ribbon1 As Ribbon
    Friend WithEvents DockPanel1 As Microsoft.VisualStudio.WinForms.Docking.DockPanel
    Friend WithEvents VisualStudioToolStripExtender1 As Microsoft.VisualStudio.WinForms.Docking.VisualStudioToolStripExtender

End Class
