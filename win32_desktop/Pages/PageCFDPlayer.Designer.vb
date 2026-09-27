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
        components = New System.ComponentModel.Container()
        m_playTimer = New Timer(components)
        m_canvas = New CFDCanvas()
        bottomPanel = New Panel()
        btnPlay = New Button()
        lblSpeed = New Label()
        cboSpeed = New ComboBox()
        trackFrame = New TrackBar()
        lblFrame = New Label()
        bottomPanel.SuspendLayout()
        CType(trackFrame, System.ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()

        ' 
        ' m_playTimer
        ' 
        m_playTimer.Enabled = False
        m_playTimer.Name = "m_playTimer"

        ' 
        ' m_canvas（中央 3D 视口，占满剩余空间）
        ' 
        m_canvas.ArrowDensity = 2
        m_canvas.AutoPointSize = True
        m_canvas.AutoRange = True
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Field = "pressure"
        m_canvas.FrameIndex = -1
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
        m_canvas.SliceOnly = False
        m_canvas.TabIndex = 0
        m_canvas.Threshold = 0R
        m_canvas.TooltipFields = Nothing

        ' 
        ' bottomPanel（底部 CFD 仿真动画播放控制条）
        ' 
        bottomPanel.BackColor = Color.White
        bottomPanel.Controls.Add(btnPlay)
        bottomPanel.Controls.Add(lblSpeed)
        bottomPanel.Controls.Add(cboSpeed)
        bottomPanel.Controls.Add(trackFrame)
        bottomPanel.Controls.Add(lblFrame)
        bottomPanel.Dock = DockStyle.Bottom
        bottomPanel.Location = New Point(0, 757)
        bottomPanel.Name = "bottomPanel"
        bottomPanel.Size = New Size(1424, 64)
        bottomPanel.TabIndex = 3

        ' 
        ' btnPlay
        ' 
        btnPlay.BackColor = Color.FromArgb(37, 99, 235)
        btnPlay.Enabled = False
        btnPlay.FlatAppearance.BorderSize = 0
        btnPlay.FlatStyle = FlatStyle.Flat
        btnPlay.Font = New Font("Microsoft YaHei UI", 11F)
        btnPlay.ForeColor = Color.White
        btnPlay.Location = New Point(16, 12)
        btnPlay.Name = "btnPlay"
        btnPlay.Size = New Size(40, 40)
        btnPlay.TabIndex = 0
        btnPlay.Text = "▶"
        btnPlay.UseVisualStyleBackColor = False

        ' 
        ' lblSpeed
        ' 
        lblSpeed.AutoSize = True
        lblSpeed.ForeColor = Color.FromArgb(71, 85, 105)
        lblSpeed.Location = New Point(72, 22)
        lblSpeed.Name = "lblSpeed"
        lblSpeed.Size = New Size(32, 17)
        lblSpeed.TabIndex = 1
        lblSpeed.Text = "速度"

        ' 
        ' cboSpeed
        ' 
        cboSpeed.BackColor = Color.FromArgb(248, 250, 252)
        cboSpeed.DropDownStyle = ComboBoxStyle.DropDownList
        cboSpeed.FlatStyle = FlatStyle.Flat
        cboSpeed.Font = New Font("Microsoft YaHei UI", 9F)
        cboSpeed.Items.AddRange(New Object() {"2 fps", "5 fps", "10 fps", "20 fps", "30 fps"})
        cboSpeed.Location = New Point(112, 18)
        cboSpeed.Name = "cboSpeed"
        cboSpeed.Size = New Size(84, 25)
        cboSpeed.TabIndex = 2

        ' 
        ' trackFrame
        ' 
        trackFrame.Enabled = False
        trackFrame.Location = New Point(210, 20)
        trackFrame.Maximum = 0
        trackFrame.Name = "trackFrame"
        trackFrame.Size = New Size(400, 45)
        trackFrame.TabIndex = 3
        trackFrame.TickStyle = TickStyle.None

        ' 
        ' lblFrame
        ' 
        lblFrame.AutoSize = True
        lblFrame.ForeColor = Color.FromArgb(71, 85, 105)
        lblFrame.Location = New Point(0, 24)
        lblFrame.Name = "lblFrame"
        lblFrame.Size = New Size(45, 17)
        lblFrame.TabIndex = 4
        lblFrame.Text = "— · —"

        ' 
        ' PageCFDPlayer
        ' 
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = Color.FromArgb(244, 246, 250)
        ClientSize = New Size(1424, 821)
        Controls.Add(m_canvas)
        Controls.Add(bottomPanel)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or
                    Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or
                    Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or
                    Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or
                    Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or
                    Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Font = New Font("Microsoft YaHei UI", 9F)
        Name = "PageCFDPlayer"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        Text = "CFD Player"

        bottomPanel.ResumeLayout(False)
        CType(trackFrame, System.ComponentModel.ISupportInitialize).EndInit()
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
End Class
