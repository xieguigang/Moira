Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms
Imports Galaxy.Workbench.DockDocument
Imports CDFDxCanvas
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PageCFDPlayer
    Inherits DocumentWindow

    Sub New()
        Call InitializeComponent()
    End Sub

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
        components = New Container()
        m_playTimer = New Timer(components)
        m_canvas = New CFDCanvas()
        bottomPanel = New Panel()
        Panel3 = New Panel()
        trackFrame = New TrackBar()
        lblFrame = New Label()
        Panel1 = New Panel()
        lblSpeed = New Label()
        cboSpeed = New ComboBox()
        btnPlay = New Button()
        bottomPanel.SuspendLayout()
        Panel3.SuspendLayout()
        CType(trackFrame, ISupportInitialize).BeginInit()
        Panel1.SuspendLayout()
        SuspendLayout()
        ' 
        ' m_playTimer
        ' 
        ' 
        ' m_canvas
        ' 
        m_canvas.ArrowDensity = 2
        m_canvas.AutoPointSize = True
        m_canvas.AutoRange = True
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Field = "pressure"
        m_canvas.FrameIndex = -1
        m_canvas.Location = New Point(0, 0)
        m_canvas.Name = "m_canvas"
        m_canvas.Palette = ScalerPalette.Jet
        m_canvas.RangeMax = 1R
        m_canvas.RangeMin = 0R
        m_canvas.SectionAxis = Data.CfdAxis.X
        m_canvas.SectionEnabled = False
        m_canvas.SectionPosition = 0
        m_canvas.SelectedVoxel = -1
        m_canvas.ShowArrows = False
        m_canvas.ShowDebugInfo = False
        m_canvas.ShowHoverTooltip = False
        m_canvas.Size = New Size(1282, 780)
        m_canvas.SliceOnly = False
        m_canvas.TabIndex = 0
        m_canvas.Threshold = 0R
        m_canvas.TooltipFields = Nothing
        ' 
        ' bottomPanel
        ' 
        bottomPanel.BackColor = Color.White
        bottomPanel.Controls.Add(Panel3)
        bottomPanel.Controls.Add(Panel1)
        bottomPanel.Dock = DockStyle.Bottom
        bottomPanel.Location = New Point(0, 780)
        bottomPanel.Name = "bottomPanel"
        bottomPanel.Size = New Size(1282, 64)
        bottomPanel.TabIndex = 3
        ' 
        ' Panel3
        ' 
        Panel3.BackColor = Color.White
        Panel3.Controls.Add(lblFrame)
        Panel3.Controls.Add(trackFrame)
        Panel3.Dock = DockStyle.Fill
        Panel3.Location = New Point(195, 0)
        Panel3.Name = "Panel3"
        Panel3.Size = New Size(1087, 64)
        Panel3.TabIndex = 5
        ' 
        ' trackFrame
        ' 
        trackFrame.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        trackFrame.Enabled = False
        trackFrame.Location = New Point(12, 7)
        trackFrame.Margin = New Padding(20, 15, 20, 3)
        trackFrame.Maximum = 0
        trackFrame.Name = "trackFrame"
        trackFrame.Size = New Size(1067, 45)
        trackFrame.TabIndex = 3
        trackFrame.TickStyle = TickStyle.None
        ' 
        ' lblFrame
        ' 
        lblFrame.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        lblFrame.AutoSize = True
        lblFrame.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblFrame.Location = New Point(1022, 35)
        lblFrame.Name = "lblFrame"
        lblFrame.Size = New Size(45, 17)
        lblFrame.TabIndex = 4
        lblFrame.Text = "— · —"
        ' 
        ' Panel1
        ' 
        Panel1.BackColor = Color.White
        Panel1.Controls.Add(lblSpeed)
        Panel1.Controls.Add(cboSpeed)
        Panel1.Controls.Add(btnPlay)
        Panel1.Dock = DockStyle.Left
        Panel1.Location = New Point(0, 0)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(195, 64)
        Panel1.TabIndex = 4
        ' 
        ' lblSpeed
        ' 
        lblSpeed.AutoSize = True
        lblSpeed.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSpeed.Location = New Point(62, 19)
        lblSpeed.Name = "lblSpeed"
        lblSpeed.Size = New Size(32, 17)
        lblSpeed.TabIndex = 1
        lblSpeed.Text = "速度"
        ' 
        ' cboSpeed
        ' 
        cboSpeed.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboSpeed.DropDownStyle = ComboBoxStyle.DropDownList
        cboSpeed.FlatStyle = FlatStyle.Flat
        cboSpeed.Font = New Font("Microsoft YaHei UI", 9F)
        cboSpeed.Items.AddRange(New Object() {"2 fps", "5 fps", "10 fps", "20 fps", "30 fps"})
        cboSpeed.Location = New Point(100, 16)
        cboSpeed.Name = "cboSpeed"
        cboSpeed.Size = New Size(84, 25)
        cboSpeed.TabIndex = 2
        ' 
        ' btnPlay
        ' 
        btnPlay.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        btnPlay.Enabled = False
        btnPlay.FlatAppearance.BorderSize = 0
        btnPlay.FlatStyle = FlatStyle.Flat
        btnPlay.Font = New Font("Microsoft YaHei UI", 11F)
        btnPlay.ForeColor = Color.White
        btnPlay.Location = New Point(9, 12)
        btnPlay.Name = "btnPlay"
        btnPlay.Size = New Size(36, 36)
        btnPlay.TabIndex = 0
        btnPlay.Text = "▶"
        btnPlay.UseVisualStyleBackColor = False
        ' 
        ' PageCFDPlayer
        ' 
        AutoScaleDimensions = New SizeF(7F, 17F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = Color.FromArgb(CByte(244), CByte(246), CByte(250))
        ClientSize = New Size(1282, 844)
        Controls.Add(m_canvas)
        Controls.Add(bottomPanel)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Font = New Font("Microsoft YaHei UI", 9F)
        Name = "PageCFDPlayer"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        TabPageContextMenuStrip = DockContextMenuStrip1
        Text = "CFD Player"
        bottomPanel.ResumeLayout(False)
        Panel3.ResumeLayout(False)
        Panel3.PerformLayout()
        CType(trackFrame, ISupportInitialize).EndInit()
        Panel1.ResumeLayout(False)
        Panel1.PerformLayout()
        ResumeLayout(False)
    End Sub

    Friend WithEvents m_canvas As CFDCanvas
    Friend WithEvents m_playTimer As Timer
    Friend WithEvents bottomPanel As Panel
    Friend WithEvents btnPlay As Button
    Friend WithEvents cboSpeed As ComboBox
    Friend WithEvents trackFrame As TrackBar
    Friend WithEvents lblFrame As Label
    Friend WithEvents lblSpeed As Label
    Friend WithEvents Panel1 As Panel
    Friend WithEvents Panel3 As Panel
End Class
