Imports System.Drawing
Imports System.Windows.Forms

Partial Public Class DemoTestForm
    Inherits Form

    Sub New()
        Call InitializeComponent()
    End Sub

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

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Text = "CFD 可视化 · CFDCanvas 控件测试"
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(1440, 860)
        MinimumSize = New Size(1100, 640)
        BackColor = Color.FromArgb(244, 246, 250)
        Font = New Font("Microsoft YaHei UI", 9.0F)
    End Sub
End Class