Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms
Imports Galaxy.Workbench.DockDocument
Imports CDFDxCanvas

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PanelPlayerRight
    Inherits ToolWindow

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
        propPanel = New Panel()
        lblProp = New Label()
        pgVoxel = New PropertyGrid()
        lblVoxelInfo = New Label()
        pnlSeries = New Panel()
        lblSeriesHint = New Label()
        picSlice = New PictureBox()
        Panel1 = New Panel()
        propPanel.SuspendLayout()
        CType(picSlice, ISupportInitialize).BeginInit()
        Panel1.SuspendLayout()
        SuspendLayout()
        ' 
        ' propPanel
        ' 
        propPanel.BackColor = Color.White
        propPanel.Controls.Add(lblProp)
        propPanel.Controls.Add(pgVoxel)
        propPanel.Dock = DockStyle.Bottom
        propPanel.Location = New Point(0, 539)
        propPanel.MinimumSize = New Size(50, 550)
        propPanel.Name = "propPanel"
        propPanel.Size = New Size(313, 550)
        propPanel.TabIndex = 4
        ' 
        ' lblProp
        ' 
        lblProp.Dock = DockStyle.Top
        lblProp.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblProp.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblProp.Location = New Point(0, 0)
        lblProp.Name = "lblProp"
        lblProp.Padding = New Padding(12, 0, 0, 0)
        lblProp.Size = New Size(313, 26)
        lblProp.TabIndex = 0
        lblProp.Text = "体素属性"
        lblProp.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' pgVoxel
        ' 
        pgVoxel.BackColor = Color.White
        pgVoxel.CategoryForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        pgVoxel.Dock = DockStyle.Fill
        pgVoxel.Font = New Font("Microsoft YaHei UI", 8.5F)
        pgVoxel.LineColor = Color.FromArgb(CByte(226), CByte(232), CByte(240))
        pgVoxel.Location = New Point(0, 0)
        pgVoxel.Name = "pgVoxel"
        pgVoxel.PropertySort = PropertySort.Alphabetical
        pgVoxel.Size = New Size(313, 550)
        pgVoxel.TabIndex = 1
        pgVoxel.ViewBackColor = Color.White
        ' 
        ' lblVoxelInfo
        ' 
        lblVoxelInfo.Dock = DockStyle.Bottom
        lblVoxelInfo.Location = New Point(0, 208)
        lblVoxelInfo.Name = "lblVoxelInfo"
        lblVoxelInfo.Padding = New Padding(16, 2, 8, 0)
        lblVoxelInfo.Size = New Size(313, 92)
        lblVoxelInfo.TabIndex = 0
        ' 
        ' pnlSeries
        ' 
        pnlSeries.Dock = DockStyle.Fill
        pnlSeries.Location = New Point(0, 18)
        pnlSeries.Margin = New Padding(12, 0, 12, 0)
        pnlSeries.Name = "pnlSeries"
        pnlSeries.Size = New Size(313, 190)
        pnlSeries.TabIndex = 1
        ' 
        ' lblSeriesHint
        ' 
        lblSeriesHint.Dock = DockStyle.Top
        lblSeriesHint.Location = New Point(0, 0)
        lblSeriesHint.Name = "lblSeriesHint"
        lblSeriesHint.Padding = New Padding(16, 0, 0, 0)
        lblSeriesHint.Size = New Size(313, 18)
        lblSeriesHint.TabIndex = 2
        lblSeriesHint.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' picSlice
        ' 
        picSlice.Dock = DockStyle.Fill
        picSlice.Location = New Point(0, 300)
        picSlice.Margin = New Padding(12, 0, 12, 0)
        picSlice.Name = "picSlice"
        picSlice.Size = New Size(313, 239)
        picSlice.TabIndex = 3
        picSlice.TabStop = False
        ' 
        ' Panel1
        ' 
        Panel1.Controls.Add(pnlSeries)
        Panel1.Controls.Add(lblVoxelInfo)
        Panel1.Controls.Add(lblSeriesHint)
        Panel1.Dock = DockStyle.Top
        Panel1.Location = New Point(0, 0)
        Panel1.MinimumSize = New Size(50, 300)
        Panel1.Name = "Panel1"
        Panel1.Size = New Size(313, 300)
        Panel1.TabIndex = 5
        ' 
        ' PanelPlayerRight
        ' 
        AutoScaleDimensions = New SizeF(96F, 96F)
        AutoScroll = True
        BackColor = Color.White
        ClientSize = New Size(313, 1089)
        Controls.Add(picSlice)
        Controls.Add(propPanel)
        Controls.Add(Panel1)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Font = New Font("Microsoft YaHei UI", 9F)
        Name = "PanelPlayerRight"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        Text = "CFD 信息面板"
        propPanel.ResumeLayout(False)
        CType(picSlice, ISupportInitialize).EndInit()
        Panel1.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents propPanel As Panel
    Friend WithEvents lblProp As Label
    Friend WithEvents pgVoxel As PropertyGrid
    Friend WithEvents lblVoxelInfo As Label
    Friend WithEvents pnlSeries As Panel
    Friend WithEvents lblSeriesHint As Label
    Friend WithEvents picSlice As PictureBox
    Friend WithEvents Panel1 As Panel
End Class
