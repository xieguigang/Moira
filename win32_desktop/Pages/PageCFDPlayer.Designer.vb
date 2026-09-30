Imports Galaxy.Workbench.DockDocument
Imports Moira.CDFDxCanvas
Imports Moira.CDFDxCanvas.Data

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PageCFDPlayer
    Inherits DocumentWindow

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
        m_canvas = New CFDCanvas()
        bottomPanel = New Panel()
        TableLayoutPanel1 = New TableLayoutPanel()
        trackFrame = New TrackBar()
        TableLayoutPanel2 = New TableLayoutPanel()
        lblFrame = New Label()
        Panel1 = New Panel()
        lblSpeed = New Label()
        cboSpeed = New ComboBox()
        btnPlay = New Button()
        m_playTimer = New Timer(components)
        bottomPanel.SuspendLayout()
        TableLayoutPanel1.SuspendLayout()
        CType(trackFrame, ComponentModel.ISupportInitialize).BeginInit()
        TableLayoutPanel2.SuspendLayout()
        Panel1.SuspendLayout()
        SuspendLayout()
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
        m_canvas.Palette = Imaging.Drawing2D.Colors.ScalerPalette.Jet
        m_canvas.RangeMax = 1R
        m_canvas.RangeMin = 0R
        m_canvas.SectionAxis = CfdAxis.X
        m_canvas.SectionEnabled = False
        m_canvas.SectionPosition = 0
        m_canvas.SelectedVoxel = -1
        m_canvas.ShowArrows = False
        m_canvas.ShowDebugInfo = False
        m_canvas.ShowHoverTooltip = False
        m_canvas.Size = New Size(1183, 433)
        m_canvas.SliceOnly = False
        m_canvas.TabIndex = 1
        m_canvas.Threshold = 0R
        m_canvas.TooltipFields = Nothing
        ' 
        ' bottomPanel
        ' 
        bottomPanel.BackColor = Color.White
        bottomPanel.Controls.Add(TableLayoutPanel1)
        bottomPanel.Controls.Add(Panel1)
        bottomPanel.Dock = DockStyle.Bottom
        bottomPanel.Location = New Point(0, 433)
        bottomPanel.Name = "bottomPanel"
        bottomPanel.Size = New Size(1183, 73)
        bottomPanel.TabIndex = 4
        ' 
        ' TableLayoutPanel1
        ' 
        TableLayoutPanel1.BackColor = Color.White
        TableLayoutPanel1.ColumnCount = 1
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        TableLayoutPanel1.Controls.Add(trackFrame, 0, 0)
        TableLayoutPanel1.Controls.Add(TableLayoutPanel2, 0, 1)
        TableLayoutPanel1.Dock = DockStyle.Fill
        TableLayoutPanel1.Location = New Point(195, 0)
        TableLayoutPanel1.Name = "TableLayoutPanel1"
        TableLayoutPanel1.RowCount = 2
        TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 65.7534256F))
        TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Percent, 34.2465744F))
        TableLayoutPanel1.Size = New Size(988, 73)
        TableLayoutPanel1.TabIndex = 5
        ' 
        ' trackFrame
        ' 
        trackFrame.Dock = DockStyle.Fill
        trackFrame.Enabled = False
        trackFrame.Location = New Point(3, 8)
        trackFrame.Margin = New Padding(3, 8, 3, 3)
        trackFrame.Name = "trackFrame"
        trackFrame.Size = New Size(982, 37)
        trackFrame.TabIndex = 4
        trackFrame.TickStyle = TickStyle.None
        ' 
        ' TableLayoutPanel2
        ' 
        TableLayoutPanel2.ColumnCount = 2
        TableLayoutPanel2.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        TableLayoutPanel2.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        TableLayoutPanel2.Controls.Add(lblFrame, 1, 0)
        TableLayoutPanel2.Dock = DockStyle.Fill
        TableLayoutPanel2.Location = New Point(3, 51)
        TableLayoutPanel2.Name = "TableLayoutPanel2"
        TableLayoutPanel2.RowCount = 1
        TableLayoutPanel2.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        TableLayoutPanel2.Size = New Size(982, 19)
        TableLayoutPanel2.TabIndex = 5
        ' 
        ' lblFrame
        ' 
        lblFrame.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Right
        lblFrame.AutoSize = True
        lblFrame.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblFrame.Location = New Point(939, 0)
        lblFrame.Name = "lblFrame"
        lblFrame.Size = New Size(40, 19)
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
        Panel1.Size = New Size(195, 73)
        Panel1.TabIndex = 4
        ' 
        ' lblSpeed
        ' 
        lblSpeed.AutoSize = True
        lblSpeed.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSpeed.Location = New Point(60, 24)
        lblSpeed.Name = "lblSpeed"
        lblSpeed.Size = New Size(33, 15)
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
        cboSpeed.Location = New Point(98, 21)
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
        btnPlay.Location = New Point(15, 19)
        btnPlay.Name = "btnPlay"
        btnPlay.Size = New Size(36, 36)
        btnPlay.TabIndex = 0
        btnPlay.Text = "▶"
        btnPlay.UseVisualStyleBackColor = False
        ' 
        ' m_playTimer
        ' 
        ' 
        ' PageCFDPlayer
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1183, 506)
        Controls.Add(m_canvas)
        Controls.Add(bottomPanel)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Name = "PageCFDPlayer"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        TabPageContextMenuStrip = DockContextMenuStrip1
        Text = "CFD Player"
        bottomPanel.ResumeLayout(False)
        TableLayoutPanel1.ResumeLayout(False)
        TableLayoutPanel1.PerformLayout()
        CType(trackFrame, ComponentModel.ISupportInitialize).EndInit()
        TableLayoutPanel2.ResumeLayout(False)
        TableLayoutPanel2.PerformLayout()
        Panel1.ResumeLayout(False)
        Panel1.PerformLayout()
        ResumeLayout(False)
    End Sub

    Friend WithEvents m_canvas As CFDCanvas
    Friend WithEvents bottomPanel As Panel
    Friend WithEvents lblFrame As Label
    Friend WithEvents Panel1 As Panel
    Friend WithEvents lblSpeed As Label
    Friend WithEvents cboSpeed As ComboBox
    Friend WithEvents btnPlay As Button
    Friend WithEvents m_playTimer As Timer
    Friend WithEvents trackFrame As TrackBar
    Friend WithEvents TableLayoutPanel1 As TableLayoutPanel
    Friend WithEvents TableLayoutPanel2 As TableLayoutPanel

End Class
