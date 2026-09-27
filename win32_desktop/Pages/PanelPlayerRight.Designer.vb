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
        components = New System.ComponentModel.Container()
        propPanel = New Panel()
        lblProp = New Label()
        pgVoxel = New PropertyGrid()
        lblVoxelInfo = New Label()
        pnlSeries = New Panel()
        lblSeriesHint = New Label()
        picSlice = New PictureBox()
        CType(picSlice, System.ComponentModel.ISupportInitialize).BeginInit()
        propPanel.SuspendLayout()
        SuspendLayout()

        ' 
        ' PanelPlayerRight（右侧信息面板）
        ' 
        AutoScroll = True
        BackColor = Color.White
        ClientSize = New Size(330, 757)
        Controls.Add(lblVoxelInfo)
        Controls.Add(pnlSeries)
        Controls.Add(lblSeriesHint)
        Controls.Add(picSlice)
        Controls.Add(propPanel)
        Font = New Font("Microsoft YaHei UI", 9F)
        Name = "PanelPlayerRight"
        Text = "CFD 信息面板"

        ' 
        ' propPanel
        ' 
        propPanel.BackColor = Color.White
        propPanel.Controls.Add(lblProp)
        propPanel.Controls.Add(pgVoxel)
        propPanel.Dock = DockStyle.Bottom
        propPanel.Location = New Point(0, 496)
        propPanel.Name = "propPanel"
        propPanel.Size = New Size(330, 316)
        propPanel.TabIndex = 4

        ' 
        ' lblProp
        ' 
        lblProp.Dock = DockStyle.Top
        lblProp.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblProp.ForeColor = Color.FromArgb(30, 41, 59)
        lblProp.Location = New Point(0, 0)
        lblProp.Name = "lblProp"
        lblProp.Padding = New Padding(12, 0, 0, 0)
        lblProp.Size = New Size(330, 26)
        lblProp.TabIndex = 0
        lblProp.Text = "体素属性"
        lblProp.TextAlign = ContentAlignment.MiddleLeft

        ' 
        ' pgVoxel
        ' 
        pgVoxel.BackColor = Color.White
        pgVoxel.CategoryForeColor = Color.FromArgb(30, 41, 59)
        pgVoxel.Dock = DockStyle.Fill
        pgVoxel.Font = New Font("Microsoft YaHei UI", 8.5F)
        pgVoxel.LineColor = Color.FromArgb(226, 232, 240)
        pgVoxel.Location = New Point(0, 0)
        pgVoxel.Name = "pgVoxel"
        pgVoxel.PropertySort = PropertySort.Alphabetical
        pgVoxel.Size = New Size(330, 316)
        pgVoxel.TabIndex = 1
        pgVoxel.ViewBackColor = Color.White

        ' 
        ' lblVoxelInfo
        ' 
        lblVoxelInfo.Dock = DockStyle.Top
        lblVoxelInfo.Location = New Point(0, 404)
        lblVoxelInfo.Name = "lblVoxelInfo"
        lblVoxelInfo.Padding = New Padding(16, 2, 8, 0)
        lblVoxelInfo.Size = New Size(330, 92)
        lblVoxelInfo.TabIndex = 0

        ' 
        ' pnlSeries
        ' 
        pnlSeries.Dock = DockStyle.Top
        pnlSeries.Location = New Point(0, 232)
        pnlSeries.Margin = New Padding(12, 0, 12, 0)
        pnlSeries.Name = "pnlSeries"
        pnlSeries.Size = New Size(330, 172)
        pnlSeries.TabIndex = 1

        ' 
        ' lblSeriesHint
        ' 
        lblSeriesHint.Dock = DockStyle.Top
        lblSeriesHint.Location = New Point(0, 214)
        lblSeriesHint.Name = "lblSeriesHint"
        lblSeriesHint.Padding = New Padding(16, 0, 0, 0)
        lblSeriesHint.Size = New Size(330, 18)
        lblSeriesHint.TabIndex = 2
        lblSeriesHint.TextAlign = ContentAlignment.MiddleLeft

        ' 
        ' picSlice
        ' 
        picSlice.Dock = DockStyle.Top
        picSlice.Location = New Point(0, 0)
        picSlice.Margin = New Padding(12, 0, 12, 0)
        picSlice.Name = "picSlice"
        picSlice.Size = New Size(330, 214)
        picSlice.TabIndex = 3
        picSlice.TabStop = False

        CType(picSlice, System.ComponentModel.ISupportInitialize).EndInit()
        propPanel.ResumeLayout(False)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents propPanel As Panel
    Friend WithEvents lblProp As Label
    Friend WithEvents pgVoxel As PropertyGrid
    Friend WithEvents lblVoxelInfo As Label
    Friend WithEvents pnlSeries As Panel
    Friend WithEvents lblSeriesHint As Label
    Friend WithEvents picSlice As PictureBox
End Class
