﻿Imports System.Runtime.CompilerServices
Imports System.Text
Imports CFD
Imports CFD_form.RibbonLib.Controls
Imports Microsoft.VisualBasic.ComponentModel.Ranges.Model
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Linq
Imports RibbonLib
Imports WeifenLuo.WinFormsUI.Docking

Public Class FormMain

    Dim ribbon1 As New Ribbon
    Dim vsToolStripExtender1 As New VisualStudioToolStripExtender
    Dim vS2015LightTheme1 As New VS2015LightTheme
    Dim dockPanel As DockPanel

    ReadOnly _toolStripProfessionalRenderer As New ToolStripProfessionalRenderer()

    Sub New()

        ' 此调用是设计器所必需的。
        InitializeComponent()

        Me.Controls.Add(ribbon1)
        Me.Controls.Add(dockPanel)

        dockPanel.Dock = DockStyle.Fill

        vsToolStripExtender1.DefaultRenderer = _toolStripProfessionalRenderer

        ' 在 InitializeComponent() 调用之后添加任何初始化。
        ribbon1.ResourceName = $"{App.AssemblyName}.RibbonMarkup.ribbon"
        ribbon1.Dock = DockStyle.Top
        ribbon1.Height = 100
        ribbon1.SendToBack()
        ribbonItems = New RibbonItems(ribbon1)
    End Sub



    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        PropertyGrid1.SelectedObject = reader
        PropertyGrid1.Refresh()
    End Sub



    Private Sub PropertyGrid1_PropertyValueChanged(s As Object, e As PropertyValueChangedEventArgs) Handles PropertyGrid1.PropertyValueChanged
        Select Case e.ChangedItem.Label
            Case NameOf(CFDHelper.DrawFrameData)
                ' do nothing
            Case NameOf(CFDHelper.Colors), NameOf(CFDHelper.ColorLevels)
                Call UpdatePalette()
        End Select

        e.ChangedItem.Select()
    End Sub

End Class
