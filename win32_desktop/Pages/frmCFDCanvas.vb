Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Text
Imports CFD
Imports CFD_clr
Imports Microsoft.VisualBasic.ComponentModel.Ranges.Model
Imports Microsoft.VisualBasic.Imaging
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Linq
Imports Microsoft.VisualBasic.Math.Distributions
Imports Microsoft.VisualBasic.Parallel.Tasks
Imports RibbonLib.Interop
Imports WeifenLuo.WinFormsUI.Docking
Imports bitmap = System.Drawing.Bitmap
Imports brushes = System.Drawing.Brushes
Imports image = System.Drawing.Image
Imports pen = System.Drawing.Pen
Imports solidbrush = System.Drawing.SolidBrush
Imports std = System.Math

Public Class frmCFDCanvas

    Friend CFD As CFDTcpProtocols
    Friend setup As SetupParameters

    Dim colors As SolidBrush()
    Dim offset As New DoubleRange(0, 255)
    Dim drawLine As Boolean = False
    Dim model As image = Nothing

    ReadOnly grays As solidbrush() = Designer _
        .GetColors(ScalerPalette.Gray.Description, 30) _
        .Select(Function(c) New solidbrush(c)) _
        .ToArray
    ReadOnly grayOffset As New DoubleRange(0, 29)

    ''' <summary>
    ''' the animation rendering thread
    ''' </summary>
    Friend ReadOnly timer1 As New UpdateThread(1000 / 30, Sub() Call Timer1_Tick())

    Private Sub Timer1_Tick()
        If CFD IsNot Nothing AndAlso CFD.ready Then
            Dim bitmap As bitmap = Render(frame:=CFD.getFrameData(toolkit.pars.DrawFrameData)) ' Await GetRenderBitmap()

            If Not bitmap Is Nothing Then
                Call Me.Invoke(Sub() PictureBox1.BackgroundImage = bitmap)
            End If
        Else
            Call Me.Invoke(Sub() PictureBox1.BackgroundImage = model)
        End If
    End Sub

    'Private Function GetRenderBitmap() As Task(Of bitmap)
    '    Return Task(Of bitmap).Run(
    '        Function()
    '            Return Render(frame:=CFD.getFrameData(toolkit.pars.DrawFrameData))
    '        End Function)
    'End Function

    Private Function Render(frame As Double()()) As bitmap
        Dim bitmap As New bitmap(CFD.pars.dims(0), CFD.pars.dims(1))
        Dim g As Graphics = Graphics.FromImage(bitmap)

        If frame.IsNullOrEmpty Then
            Call Globals.Message("invalid frame data!")
            Return Nothing
        End If

        Dim range As DoubleRange = frame.AsParallel _
            .Select(Function(a) {a.Min, a.Max}) _
            .IteratesALL _
            .Range
        Dim v As Double
        Dim index As Integer
        Dim cut As Double = Double.MaxValue
        Dim enableTrIQ As Boolean = toolkit.pars.enableTrIQ

        If enableTrIQ Then
            cut = frame.IteratesALL.FindThreshold(toolkit.pars.TrIQ)
            range = New DoubleRange(range.Min, cut)
        End If

        g.CompositingQuality = CompositingQuality.HighSpeed
        g.TextRenderingHint = TextRenderingHint.SystemDefault
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None

        If range Is Nothing OrElse range.Min.IsNaNImaginary OrElse range.Max.IsNaNImaginary Then
            Return Nothing
        End If

        Dim colors = Me.colors.ToArray

        For i As Integer = 0 To frame.Length - 1
            Dim row = frame(i)

            For j As Integer = 0 To row.Length - 1
                v = row(j)

                If enableTrIQ AndAlso v > cut Then
                    v = cut
                End If

                If range.Length = 0.0 Then
                    index = 0
                Else
                    index = CInt(range.ScaleMapping(v, offset))
                End If

                If index < 0 Then
                    index = 0
                ElseIf index >= colors.Length Then
                    index = colors.Length - 1
                End If

                If colors.Length = 0 Then
                    Exit For
                End If

                g.FillRectangle(colors(index), New Rectangle(i, j, 1, 1))
            Next
        Next

        Dim showTracer As Boolean = Invoke(Function() ribbonItems.CheckShowTracer.BooleanValue)
        Dim showFlowline As Boolean = Invoke(Function() ribbonItems.CheckShowFlowLine.BooleanValue)

        If Not model Is Nothing Then
            Call g.DrawImage(model, New Rectangle(New Point, bitmap.Size))
        End If

        If showTracer Then
            For Each pt As PointF In CFD.moveTracers(toolkit.pars.TracerSpeedLevel)
                Call g.FillRectangle(Brushes.Black, New RectangleF(pt, New Size(1, 1)))
            Next
        End If
        If showFlowline Then
            Call drawFlowlines(g)
        End If

        Return bitmap
    End Function

    Private Sub drawFlowlines(g As Graphics)
        Dim xyDims As Size = CFD.pars.getDims
        Dim xdim = xyDims.Width
        Dim ydim = xyDims.Height
        Dim lenX As Single = 13
        Dim lenY As Single = 8
        Dim xLines = xdim / lenX
        Dim yLines = ydim / lenY

        Dim ux = CFD.getFrameData(FrameTypes.XVel)
        Dim uy = CFD.getFrameData(FrameTypes.YVel)
        Dim speeds As New List(Of Double)

        For yCount As Integer = 0 To yLines - 1
            For xCount As Integer = 0 To xLines - 1
                Dim x = xCount * lenX
                Dim y = yCount * lenY
                Dim vx = ux(x)(y)
                Dim vy = uy(x)(y)
                Dim speed As Double = std.Sqrt(vx ^ 2 + vy ^ 2)

                speeds.Add(speed)
            Next
        Next

        Dim speedRange As New DoubleRange(speeds)

        For yCount As Integer = 0 To yLines - 1
            For xCount As Integer = 0 To xLines - 1
                Dim x = xCount * lenX
                Dim y = yCount * lenY
                Dim vx = ux(x)(y)
                Dim vy = uy(x)(y)
                Dim speed As Double = std.Sqrt(vx ^ 2 + vy ^ 2)

                If speed > 0.0001 Then
                    Dim scale = 300 * speed
                    Dim p0 As New PointF(x - vx * scale, y + vy * scale)
                    Dim p1 As New PointF(x + vx * scale, y - vy * scale)
                    Dim offset As Integer = speedRange.ScaleMapping(speed, grayOffset)
                    Dim color As SolidBrush = grays(offset)
                    Dim line As New Pen(color, 0.85)

                    g.DrawLine(line, p0, p1)
                End If
            Next
        Next
    End Sub

    Private Sub resetCFD()
        If CFD IsNot Nothing AndAlso CFD.ready Then
            Call CFD.reset()
        End If
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Friend Sub UpdatePalette()
        If toolkit Is Nothing OrElse toolkit.pars Is Nothing Then
            Return
        End If

        offset = New DoubleRange(0, toolkit.pars.ColorLevels)
        colors = GetColors(toolkit.pars.Colors.Description, toolkit.pars.ColorLevels + 1) _
            .Select(Function(c) New SolidBrush(c)) _
            .ToArray
    End Sub

    Private Sub PictureBox1_MouseUp(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseUp
        drawLine = False
    End Sub

    Private Sub PictureBox1_MouseDown(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseDown
        If e.Button = MouseButtons.Left Then
            drawLine = CheckDrawBarrier()
        End If
    End Sub

    Private Function CheckDrawBarrier() As Boolean
        Return ribbonItems.CheckDrawBarrier.BooleanValue
    End Function

    Private Sub PictureBox1_MouseClick(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseClick
        If e.Button = MouseButtons.Left AndAlso CheckDrawBarrier() AndAlso CFD IsNot Nothing AndAlso CFD.ready Then
            ' Call reader.SetBarrierPoint(GetCFDPosition, 1)
        End If
    End Sub

    Private Function GetCFDPosition() As Point
        Dim xy As Point = PictureBox1.PointToClient(Cursor.Position)
        Dim sizeView As Size = PictureBox1.Size
        Dim dims As Size = CFD.pars.getDims
        Dim ratio As New SizeF(sizeView.Width / dims.Width, sizeView.Height / dims.Height)
        Dim x As Integer = xy.X / ratio.Width, y As Integer = xy.Y / ratio.Height

        If x < 0 Then x = 0
        If y < 0 Then y = 0
        If x >= dims.Width Then x = dims.Width - 1
        If y >= dims.Height Then y = dims.Height - 1

        Return New Point(x, y)
    End Function

    Private Sub ShowPointInformation()
        Dim xy As Point = GetCFDPosition()
        Dim tooltip As New StringBuilder

        Call Message($"[{xy.X},{xy.Y}]")

        Dim speed As Double = CFD.GetSpeed(xy)
        Dim density As Double = CFD.GetDensity(xy)
        Dim xvel As Double = CFD.GetXVel(xy)
        Dim yvel As Double = CFD.GetYVel(xy)

        tooltip.AppendLine($"point xy: ({xy.X},{xy.Y})")
        tooltip.AppendLine($"speed: {speed}")
        tooltip.AppendLine($"density: {density}")
        tooltip.AppendLine($"velocity: [{xvel},{yvel}]")

        'If reader.GetBarrier(xy) Then
        '    tooltip.AppendLine("current location is a barrier site")
        'End If

        ToolTip1.SetToolTip(PictureBox1, tooltip.ToString)
    End Sub

    Private Sub PictureBox1_MouseMove(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseMove
        If drawLine AndAlso CheckDrawBarrier() Then
            Call CFD.SetBarrierPoint(GetCFDPosition())
        End If

        If CFD IsNot Nothing AndAlso CFD.ready Then
            Call ShowPointInformation()
        End If
    End Sub

    Private Sub frmCFDCanvas_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        CFD = Globals.CreateService
        CFD.config(setup)

        AddHandler ribbonItems.ButtonReset.ExecuteEvent, Sub() resetCFD()
        AddHandler ribbonItems.ButtonClearBarrier.ExecuteEvent, Sub()
                                                                    ' CFD.clearBarrier()
                                                                End Sub

        toolkit.Show(DockPanel)
        toolkit.DockState = DockState.DockLeft
        toolkit.SetTarget(callback:=Me)

        TabText = $"CFD Project - {Now.Year}{Now.Month.ToString.PadLeft(1, "0"c)}{Now.Day.ToString.PadLeft(1, "0"c)}-{App.ElapsedMilliseconds}"

        Call SetCurrent()
        Call main.EnableVSRenderer(ContextMenuStrip1)
        Call timer1.Start()

        If setup.modelfile.FileExists Then
            Using s As Stream = setup.modelfile.Open(FileMode.Open, doClear:=False, [readOnly]:=True)
                model = image.FromStream(s)
            End Using
        End If
    End Sub

    Private Sub frmCFDCanvas_Activated(sender As Object, e As EventArgs) Handles MyBase.Activated
        SetCurrent()
    End Sub

    Private Sub frmCFDCanvas_LostFocus(sender As Object, e As EventArgs) Handles MyBase.LostFocus
        ' ribbonItems.TabSimulationPage.ContextAvailable = ContextAvailability.NotAvailable
    End Sub

    Private Sub frmCFDCanvas_GotFocus(sender As Object, e As EventArgs) Handles MyBase.GotFocus
        SetCurrent()
    End Sub

    Private Sub SetCurrent()
        Globals.current = CFD
        toolkit.SetTarget(callback:=Me)
        ribbonItems.TabSimulationPage.ContextAvailable = ContextAvailability.Active

        Call UpdatePalette()
    End Sub

    Private Sub frmCFDCanvas_Closed(sender As Object, e As EventArgs) Handles MyBase.Closed
        timer1.Stop()
    End Sub
End Class