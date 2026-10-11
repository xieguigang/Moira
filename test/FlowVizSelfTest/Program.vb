' /********************************************************************************/
'
'   FlowVizSelfTest.vb
'
'   矢量图 / 流线图共享构建器的无头自测（临时工程，验证后可删除）：
'
'       1. 均匀流场 u=(1,0,0)：BuildArrows 方向 / 长度 / 颜色 / 头部几何
'       2. 同场 StreamlineTracer.Trace：折线沿 +x 穿越、预算截断
'       3. StreamlineAnimator：相位推进 + Pack 布局（8 float/粒子）
'
'   运行方式：把输出复制到 G:\Moira\assembly\bin 后执行（依赖同目录解析）。
'
' /********************************************************************************/

Imports System.Drawing
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Moira.CDFDxCanvas.Rendering

Module Program

    Private Failures As Integer = 0

    Sub Main()
        Call TestArrows()
        Call TestStreamlines()
        Call TestAnimator()
        Call TestStraightFilter()

        If Failures = 0 Then
            Console.WriteLine("ALL TESTS PASSED")
        Else
            Console.WriteLine($"{Failures} FAILURES")
            Environment.ExitCode = 1
        End If
    End Sub

    Private Sub Check(name As String, ok As Boolean, detail As String)
        If ok Then
            Console.WriteLine($"  [PASS] {name}")
        Else
            Failures += 1
            Console.WriteLine($"  [FAIL] {name} : {detail}")
        End If
    End Sub

    ''' <summary>16x16x16 波状流场 u=1, v=0.5·sin(2πx/16)：流线弯曲。</summary>
    Private Class WaveField : Implements IFlowFieldData

        Public ReadOnly Property Nx As Integer = 16 Implements IFlowFieldData.Nx
        Public ReadOnly Property Ny As Integer = 16 Implements IFlowFieldData.Ny
        Public ReadOnly Property Nz As Integer = 16 Implements IFlowFieldData.Nz

        Public ReadOnly Property OriginX As Single = 0.0F Implements IFlowFieldData.OriginX
        Public ReadOnly Property OriginY As Single = 0.0F Implements IFlowFieldData.OriginY
        Public ReadOnly Property OriginZ As Single = 0.0F Implements IFlowFieldData.OriginZ
        Public ReadOnly Property SpacingX As Single = 1.0F Implements IFlowFieldData.SpacingX
        Public ReadOnly Property SpacingY As Single = 1.0F Implements IFlowFieldData.SpacingY
        Public ReadOnly Property SpacingZ As Single = 1.0F Implements IFlowFieldData.SpacingZ
        Public ReadOnly Property CenterOffset As Single = 0.0F Implements IFlowFieldData.CenterOffset

        Public Function UxAt(idx As Integer) As Single Implements IFlowFieldData.UxAt
            Return 1.0F
        End Function

        Public Function UyAt(idx As Integer) As Single Implements IFlowFieldData.UyAt
            Dim i As Integer = idx \ (16 * 16)
            Return 0.5F * CSng(Math.Sin(2.0 * Math.PI * i / 16.0))
        End Function

        Public Function UzAt(idx As Integer) As Single Implements IFlowFieldData.UzAt
            Return 0.0F
        End Function

        Public Function IsActive(idx As Integer) As Boolean Implements IFlowFieldData.IsActive
            Return True
        End Function

        Public Function IsSeedable(idx As Integer) As Boolean Implements IFlowFieldData.IsSeedable
            Return True
        End Function

    End Class

    ''' <summary>16x16x16 均匀流场 u=(u,0,0)，全流体。</summary>
    Private Class UniformField : Implements IFlowFieldData

        Private ReadOnly m_u As Single

        Public Sub New(u As Single)
            m_u = u
        End Sub

        Public ReadOnly Property Nx As Integer = 16 Implements IFlowFieldData.Nx
        Public ReadOnly Property Ny As Integer = 16 Implements IFlowFieldData.Ny
        Public ReadOnly Property Nz As Integer = 16 Implements IFlowFieldData.Nz

        Public ReadOnly Property OriginX As Single = 0.0F Implements IFlowFieldData.OriginX
        Public ReadOnly Property OriginY As Single = 0.0F Implements IFlowFieldData.OriginY
        Public ReadOnly Property OriginZ As Single = 0.0F Implements IFlowFieldData.OriginZ
        Public ReadOnly Property SpacingX As Single = 1.0F Implements IFlowFieldData.SpacingX
        Public ReadOnly Property SpacingY As Single = 1.0F Implements IFlowFieldData.SpacingY
        Public ReadOnly Property SpacingZ As Single = 1.0F Implements IFlowFieldData.SpacingZ
        Public ReadOnly Property CenterOffset As Single = 0.0F Implements IFlowFieldData.CenterOffset

        Public Function UxAt(idx As Integer) As Single Implements IFlowFieldData.UxAt
            Return m_u
        End Function

        Public Function UyAt(idx As Integer) As Single Implements IFlowFieldData.UyAt
            Return 0.0F
        End Function

        Public Function UzAt(idx As Integer) As Single Implements IFlowFieldData.UzAt
            Return 0.0F
        End Function

        Public Function IsActive(idx As Integer) As Boolean Implements IFlowFieldData.IsActive
            Return True
        End Function

        Public Function IsSeedable(idx As Integer) As Boolean Implements IFlowFieldData.IsSeedable
            Return True
        End Function

    End Class

    Private Sub TestArrows()
        Console.WriteLine("TestArrows:")

        Dim field As New UniformField(1.0F)
        Dim lut As Color() = Designer.FromSchema(ScalerPalette.Jet, 256)
        Dim options As New VectorArrowOptions With {.Stride = 4, .SpeedMax = 1.0, .Lut = lut}
        Dim lines As LineSegment() = FlowVectors.BuildArrows(field, options)

        ' 采样点 = ceil(16/4)^3 = 4^3 = 64 矢量，每矢量 3 段
        Check("arrow count", lines.Length = 64 * 3, $"expect 192, got {lines.Length}")

        Dim okDir As Boolean = True, okLen As Boolean = True, okColor As Boolean = True

        For t As Integer = 0 To lines.Length - 3 Step 3
            Dim shaft As LineSegment = lines(t)
            Dim w1 As LineSegment = lines(t + 1)
            Dim w2 As LineSegment = lines(t + 2)

            ' 方向：主干终点 x > 起点 x，y/z 不变
            If shaft.B.X <= shaft.A.X OrElse Math.Abs(shaft.B.Y - shaft.A.Y) > 0.0001 OrElse
                Math.Abs(shaft.B.Z - shaft.A.Z) > 0.0001 Then
                okDir = False
            End If

            ' 长度：tc = 1.0 → len = 1.7 格
            If Math.Abs((shaft.B.X - shaft.A.X) - 1.7) > 0.0001 Then
                okLen = False
            End If

            ' 头部：两翼从尖端出发，向 -x 回折
            If w1.A.X <> shaft.B.X OrElse w2.A.X <> shaft.B.X OrElse
                w1.B.X >= w1.A.X OrElse w2.B.X >= w2.A.X Then
                okDir = False
            End If

            ' 颜色：tc = 1.0 → LUT 顶端色
            ' （注：Designer.FromSchema(palette, 256) 实际返回 257 项，
            '   构建器按 lut(lut.Length-1) 取顶端色，与热图着色路径一致）
            If shaft.Color.ToArgb() <> Color.FromArgb(235, lut(lut.Length - 1)).ToArgb() Then
                okColor = False
            End If
        Next

        Check("arrow direction & head", okDir, "direction/head geometry wrong")
        Check("arrow length (tc=1 -> 1.7 cells)", okLen, "length mismatch")
        Check("arrow color (LUT top)", okColor, "color mismatch")

        ' 低速过滤：u=0 场应无输出
        Dim still As New UniformField(0.0F)
        Dim empty As LineSegment() = FlowVectors.BuildArrows(still, options)

        Check("zero-speed field no arrows", empty.Length = 0, $"got {empty.Length}")
    End Sub

    Private Sub TestStreamlines()
        Console.WriteLine("TestStreamlines:")

        Dim field As New UniformField(1.0F)
        Dim options As New StreamlineOptions With {
            .SeedStride = 4, .StepLength = 0.5F, .MaxSteps = 200, .MaxSegments = 100000}
        Dim set1 As StreamlineSet = StreamlineTracer.Trace(field, options)

        ' 种子层 i=0，种子 = 16x16/4x4 = 16 条
        Check("seed count", set1.SeedCount = 16, $"got {set1.SeedCount}")
        Check("streamline count", set1.Polylines.Length = 16, $"got {set1.Polylines.Length}")

        ' 均匀场：流线沿 +x 前进，在域边界（x=15）附近出域终止，
        ' 总弧长 ≈ 15.5 格（MaxSteps=200 允许 100 格，但 16 格深的域先出界）
        Dim okShape As Boolean = True

        For Each pl As StreamlinePolyline In set1.Polylines
            If pl.VertexCount < 2 Then okShape = False
            If pl.TotalLength < 14.0 OrElse pl.TotalLength > 16.5 Then okShape = False

            ' x 单调递增（+x 流动）
            For s As Integer = 1 To pl.VertexCount - 1
                If pl.Xs(s) <= pl.Xs(s - 1) Then okShape = False : Exit For
            Next

            ' y/z 恒定（均匀场不偏转）
            If Math.Abs(pl.Ys(pl.VertexCount - 1) - pl.Ys(0)) > 0.0001 OrElse
                Math.Abs(pl.Zs(pl.VertexCount - 1) - pl.Zs(0)) > 0.0001 Then
                okShape = False
            End If
        Next

        Check("streamline shape (straight +x, exits at x~15)", okShape, "shape/length wrong")

        ' 预算截断
        Dim tight As New StreamlineOptions With {
            .SeedStride = 4, .StepLength = 0.5F, .MaxSteps = 200, .MaxSegments = 100}
        Dim set2 As StreamlineSet = StreamlineTracer.Trace(field, tight)

        Check("budget truncation", set2.Truncated AndAlso set2.SegmentCount <= 100,
              $"truncated={set2.Truncated}, segs={set2.SegmentCount}")

        ' 折线 → 线段
        Dim lines As LineSegment() = set1.BuildLines(Nothing, 210, 1.0, 2.0, 3.0)

        Check("BuildLines count", lines.Length = set1.SegmentCount,
              $"lines={lines.Length}, segs={set1.SegmentCount}")

        ' 平移生效（无 LUT 用默认着色路径，只验证几何）
        If lines.Length > 0 Then
            Check("BuildLines offset", Math.Abs(lines(0).A.X - 1.0) < 0.001,
                  $"A.X={lines(0).A.X}")
        End If
    End Sub

    Private Sub TestAnimator()
        Console.WriteLine("TestAnimator:")

        Dim field As New UniformField(1.0F)
        Dim set1 As StreamlineSet = StreamlineTracer.Trace(field, New StreamlineOptions With {
            .SeedStride = 8, .StepLength = 0.5F, .MaxSteps = 40})
        Dim animator As New StreamlineAnimator(set1, 2)

        ' 种子层 i=0，stride 8 → 2x2 = 4 条流线，每条 2 粒子 = 8 粒子
        Check("particle count", animator.ParticleCount = 8, $"got {animator.ParticleCount}")

        Dim buf(8 * animator.ParticleCount - 1) As Single
        Dim n As Integer = animator.Pack(buf, 0, 0, 0)

        Check("pack count", n = 8, $"got {n}")

        Dim okLayout As Boolean = True

        For i As Integer = 0 To n - 1
            Dim o As Integer = i * 8

            ' 位置在网格范围内，尺寸系数 = 1，heat = 1
            If buf(o) < 0 OrElse buf(o) > 16 OrElse buf(o + 1) < 0 OrElse buf(o + 1) > 16 Then
                okLayout = False
            End If

            If Math.Abs(buf(o + 3) - 1.0F) > 0.0001 OrElse Math.Abs(buf(o + 6) - 1.0F) > 0.0001 Then
                okLayout = False
            End If
        Next

        Check("pack layout (pos/size/heat)", okLayout, "layout wrong")

        ' 推进后 x 前进
        Dim x0 As Single = buf(0)

        Call animator.Advance(1.0)

        n = animator.Pack(buf, 0, 0, 0)

        Check("advance moves particle", buf(0) > x0, $"x0={x0}, x1={buf(0)}")

        ' 偏移平移：粒子坐标 = 世界坐标 + (10, 20, 30)，均匀场粒子 y/z >= 0
        Dim xBefore As Single = buf(0)

        n = animator.Pack(buf, 10, 20, 30)

        Check("pack offset", Math.Abs(buf(0) - (xBefore + 10.0F)) < 0.001 AndAlso
              buf(1) >= 20.0F - 0.001F AndAlso buf(2) >= 30.0F - 0.001F, "offset wrong")
    End Sub

    Private Sub TestStraightFilter()
        Console.WriteLine("TestStraightFilter:")

        ' ---- 均匀场：全部流线为直线，启用过滤后不显示、无粒子 ----
        Dim uniform As New UniformField(1.0F)
        Dim uSet As StreamlineSet = StreamlineTracer.Trace(uniform, New StreamlineOptions With {
            .SeedStride = 8, .StepLength = 0.5F, .MaxSteps = 40})

        Check("straightness of uniform flow ~ 0", uSet.Polylines(0).Straightness < 0.001,
              $"got {uSet.Polylines(0).Straightness}")

        uSet.MinCurvature = StreamlineSet.DefaultStraightness

        Dim uLines As LineSegment() = uSet.BuildLines(Nothing, 210, 0, 0, 0)

        Check("straight streamlines hidden", uLines.Length = 0, $"got {uLines.Length}")

        Dim uAnim As New StreamlineAnimator(uSet, 2)

        Check("no particles on straight lines", uAnim.ParticleCount = 0, $"got {uAnim.ParticleCount}")

        ' MinCurvature = 0 时全部显示（兼容旧行为）
        uSet.MinCurvature = 0.0F

        Dim uLinesAll As LineSegment() = uSet.BuildLines(Nothing, 210, 0, 0, 0)

        Check("no filter shows all", uLinesAll.Length = uSet.SegmentCount,
              $"lines={uLinesAll.Length}, segs={uSet.SegmentCount}")

        ' ---- 波状场：流线弯曲，启用过滤后仍然显示 ----
        Dim wave As New WaveField
        Dim wSet As StreamlineSet = StreamlineTracer.Trace(wave, New StreamlineOptions With {
            .SeedStride = 8, .StepLength = 0.5F, .MaxSteps = 60})

        Dim sMax As Double = 0.0

        For Each pl As StreamlinePolyline In wSet.Polylines
            If pl IsNot Nothing AndAlso pl.Straightness > sMax Then
                sMax = pl.Straightness
            End If
        Next

        Check("curved streamlines detected", sMax > StreamlineSet.DefaultStraightness,
              $"max straightness = {sMax}")

        wSet.MinCurvature = StreamlineSet.DefaultStraightness

        Dim wLines As LineSegment() = wSet.BuildLines(Nothing, 210, 0, 0, 0)
        Dim wAnim As New StreamlineAnimator(wSet, 2)

        Check("curved streamlines kept", wLines.Length > 0 AndAlso wAnim.ParticleCount > 0,
              $"lines={wLines.Length}, particles={wAnim.ParticleCount}")
    End Sub

End Module
