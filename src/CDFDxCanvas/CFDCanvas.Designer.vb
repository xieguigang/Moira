<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class CFDCanvas
    Inherits System.Windows.Forms.UserControl

    'UserControl overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
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

    ''' <summary>DirectX 3D 场景画布。</summary>
    Private WithEvents m_sceneCanvas As Microsoft.VisualBasic.Drawing.DirectX.DxScene3DCanvas

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.m_sceneCanvas = New Microsoft.VisualBasic.Drawing.DirectX.DxScene3DCanvas()
        Me.SuspendLayout()
        '
        'm_sceneCanvas
        '
        Me.m_sceneCanvas.BackgroundColor = System.Drawing.Color.FromArgb(244, 246, 250)
        Me.m_sceneCanvas.Dock = System.Windows.Forms.DockStyle.Fill
        Me.m_sceneCanvas.Location = New System.Drawing.Point(0, 0)
        Me.m_sceneCanvas.Name = "m_sceneCanvas"
        Me.m_sceneCanvas.PointSize = 6
        Me.m_sceneCanvas.ShowConnections = True
        Me.m_sceneCanvas.ShowGround = False
        Me.m_sceneCanvas.Size = New System.Drawing.Size(792, 533)
        Me.m_sceneCanvas.TabIndex = 0
        Me.m_sceneCanvas.UseEmbeddedColor = True
        '
        'CFDCanvas
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 15.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.Controls.Add(Me.m_sceneCanvas)
        Me.Name = "CFDCanvas"
        Me.Size = New System.Drawing.Size(792, 533)
        Me.ResumeLayout(False)
    End Sub

End Class
