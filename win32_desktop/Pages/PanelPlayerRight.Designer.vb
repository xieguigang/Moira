Imports Galaxy.Workbench.DockDocument

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PanelPlayerRight
    Inherits ToolWindow

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
        pnlSeries = New Panel()
        lblVoxelInfo = New Label()
        lblSeriesHint = New Label()
        picSlice = New PictureBox()
        Panel1 = New Panel()
        SplitContainer1 = New SplitContainer()
        Panel2 = New Panel()
        CType(picSlice, ComponentModel.ISupportInitialize).BeginInit()
        Panel1.SuspendLayout()
        CType(SplitContainer1, ComponentModel.ISupportInitialize).BeginInit()
        SplitContainer1.Panel1.SuspendLayout()
        SplitContainer1.Panel2.SuspendLayout()
        SplitContainer1.SuspendLayout()
        Panel2.SuspendLayout()
        SuspendLayout()
        ' 
        ' pnlSeries
        ' 
        pnlSeries.BackColor = Color.White
        pnlSeries.Dock = DockStyle.Fill
        pnlSeries.Location = New Point(0, 0)
        pnlSeries.Margin = New Padding(12, 0, 12, 0)
        pnlSeries.MinimumSize = New Size(300, 200)
        pnlSeries.Name = "pnlSeries"
        pnlSeries.Size = New Size(346, 204)
        pnlSeries.TabIndex = 1
        ' 
        ' lblVoxelInfo
        ' 
        lblVoxelInfo.BackColor = Color.FromArgb(CByte(192), CByte(255), CByte(255))
        lblVoxelInfo.Dock = DockStyle.Fill
        lblVoxelInfo.Location = New Point(0, 237)
        lblVoxelInfo.Name = "lblVoxelInfo"
        lblVoxelInfo.Padding = New Padding(16, 15, 15, 0)
        lblVoxelInfo.Size = New Size(346, 495)
        lblVoxelInfo.TabIndex = 0
        ' 
        ' lblSeriesHint
        ' 
        lblSeriesHint.Dock = DockStyle.Top
        lblSeriesHint.Location = New Point(0, 0)
        lblSeriesHint.Margin = New Padding(3, 10, 3, 10)
        lblSeriesHint.Name = "lblSeriesHint"
        lblSeriesHint.Padding = New Padding(16, 0, 0, 0)
        lblSeriesHint.Size = New Size(346, 33)
        lblSeriesHint.TabIndex = 2
        lblSeriesHint.Text = "时间序列图"
        lblSeriesHint.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' picSlice
        ' 
        picSlice.BackColor = Color.White
        picSlice.Dock = DockStyle.Fill
        picSlice.Location = New Point(0, 0)
        picSlice.Margin = New Padding(12, 0, 12, 0)
        picSlice.Name = "picSlice"
        picSlice.Size = New Size(346, 248)
        picSlice.TabIndex = 3
        picSlice.TabStop = False
        ' 
        ' Panel1
        ' 
        Panel1.Controls.Add(pnlSeries)
        Panel1.Dock = DockStyle.Fill
        Panel1.Location = New Point(0, 33)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(346, 204)
        Panel1.TabIndex = 5
        ' 
        ' SplitContainer1
        ' 
        SplitContainer1.Dock = DockStyle.Fill
        SplitContainer1.FixedPanel = FixedPanel.Panel2
        SplitContainer1.Location = New Point(0, 0)
        SplitContainer1.Name = "SplitContainer1"
        SplitContainer1.Orientation = Orientation.Horizontal
        ' 
        ' SplitContainer1.Panel1
        ' 
        SplitContainer1.Panel1.Controls.Add(lblVoxelInfo)
        SplitContainer1.Panel1.Controls.Add(Panel2)
        ' 
        ' SplitContainer1.Panel2
        ' 
        SplitContainer1.Panel2.Controls.Add(picSlice)
        SplitContainer1.Size = New Size(346, 984)
        SplitContainer1.SplitterDistance = 732
        SplitContainer1.TabIndex = 7
        ' 
        ' Panel2
        ' 
        Panel2.Controls.Add(Panel1)
        Panel2.Controls.Add(lblSeriesHint)
        Panel2.Dock = DockStyle.Top
        Panel2.Location = New Point(0, 0)
        Panel2.Name = "Panel2"
        Panel2.Size = New Size(346, 237)
        Panel2.TabIndex = 0
        ' 
        ' PanelPlayerRight
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(346, 984)
        Controls.Add(SplitContainer1)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Name = "PanelPlayerRight"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        Text = "CFD 信息面板"
        CType(picSlice, ComponentModel.ISupportInitialize).EndInit()
        Panel1.ResumeLayout(False)
        SplitContainer1.Panel1.ResumeLayout(False)
        SplitContainer1.Panel2.ResumeLayout(False)
        CType(SplitContainer1, ComponentModel.ISupportInitialize).EndInit()
        SplitContainer1.ResumeLayout(False)
        Panel2.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents pnlSeries As Panel
    Friend WithEvents lblVoxelInfo As Label
    Friend WithEvents lblSeriesHint As Label
    Friend WithEvents picSlice As PictureBox
    Friend WithEvents Panel1 As Panel
    Friend WithEvents SplitContainer1 As SplitContainer
    Friend WithEvents Panel2 As Panel
End Class
