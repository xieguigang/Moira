' /********************************************************************************/
'
'   Streamlines.vb
'
'   流线图（Pathlines）构建器 —— 入口面种子 + RK2 积分 + 动画光点粒子
'
'   作用：
'       在稳态速度场上从入口层（最小 i 的可种子层）均匀采样种子点，
'       用 RK2 中点法 + 三线性插值正向积分出流线折线：
'           - 折线拆成 LineSegment（速度 LUT 着色）走 LINELIST 通道
'           - 折线同时记录累计弧长，供 StreamlineAnimator 让光点粒子
'             沿线运动（粒子等速光点，不映射速度颜色）
'       段数设预算上限（超限截断），防止 LINELIST 全量重建通道过载。
'
'   坐标约定：
'       与 FlowVectors 一致：世界坐标 = Origin + (i + CenterOffset) * Spacing，
'       返回的折线/粒子均处于原始世界坐标，调用方按需做质心补偿平移
'       （粒子打包时由 <see cref="StreamlineAnimator.Pack"/> 的偏移参数完成）。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Imaging.Drawing3D

Namespace Rendering

    ''' <summary>
    ''' 流线追踪参数。
    ''' </summary>
    Public Class StreamlineOptions

        ''' <summary>种子层采样步长（j/k 方向，格点）。</summary>
        Public Property SeedStride As Integer = 4

        ''' <summary>积分弧长步长（格子单位；RK2 中点法）。</summary>
        Public Property StepLength As Single = 0.7F

        ''' <summary>每条流线的最大积分步数。</summary>
        Public Property MaxSteps As Integer = 3000

        ''' <summary>速度低于该值（格子单位）视为停滞，终止追踪。</summary>
        Public Property MinSpeed As Single = 0.0005F

        ''' <summary>全部流线的折线段总数预算（超限截断）。</summary>
        Public Property MaxSegments As Integer = 400000

        ''' <summary>
        ''' 折线着色的速度归一化上限；&lt;= 0 时自动扫描活动格点求最大速度。
        ''' </summary>
        Public Property SpeedMax As Double = 0.0

    End Class

    ''' <summary>
    ''' 一条流线折线：顶点序列 + 累计弧长 + 逐段归一化速度（着色用）。
    ''' </summary>
    ''' <remarks>
    ''' 顶点/弧长/着色数组按构建器内部约定由 <see cref="StreamlineTracer"/>
    ''' 填充，外部只读使用（折线构建后单调递增、长度一致）。
    ''' </remarks>
    Public Class StreamlinePolyline

        Public Xs As New List(Of Single)
        Public Ys As New List(Of Single)
        Public Zs As New List(Of Single)
        ''' <summary>累计弧长（格点 0 起，单调递增，长度 = 顶点数）。</summary>
        Public Cum As New List(Of Double)
        ''' <summary>每段的归一化速度（0..1），段数 = 顶点数 - 1。</summary>
        Public Tc As New List(Of Single)

        ''' <summary>折线总弧长（世界单位）。</summary>
        Public ReadOnly Property TotalLength As Double
            Get
                Return If(Cum.Count = 0, 0.0, Cum(Cum.Count - 1))
            End Get
        End Property

        ''' <summary>顶点数。</summary>
        Public ReadOnly Property VertexCount As Integer
            Get
                Return Xs.Count
            End Get
        End Property

    End Class

    ''' <summary>
    ''' 一次流线追踪的结果：折线集合 + 可直接提交的线段缓存。
    ''' </summary>
    Public Class StreamlineSet

        ''' <summary>流线折线（原始世界坐标）。</summary>
        Public Property Polylines As StreamlinePolyline()

        ''' <summary>实际生成的折线段总数（预算截断后的值）。</summary>
        Public Property SegmentCount As Integer

        ''' <summary>种子点数（含未产出有效流线的种子）。</summary>
        Public Property SeedCount As Integer

        ''' <summary>是否因段数预算被截断。</summary>
        Public Property Truncated As Boolean

        ''' <summary>
        ''' 把折线拆成线段（LINELIST 通道），逐段按归一化速度 LUT 着色，
        ''' 并整体平移 (dx, dy, dz)（质心补偿由调用方提供）。
        ''' </summary>
        Public Function BuildLines(lut As Color(), alpha As Integer,
                                   dx As Double, dy As Double, dz As Double) As LineSegment()
            Dim lutN As Integer = If(lut IsNot Nothing, lut.Length, 0)
            Dim lines As New List(Of LineSegment)(If(SegmentCount > 0, SegmentCount, 1024))

            For Each pl As StreamlinePolyline In Polylines
                If pl Is Nothing OrElse pl.VertexCount < 2 Then
                    Continue For
                End If

                Dim xs = pl.Xs, ys = pl.Ys, zs = pl.Zs

                For s As Integer = 0 To pl.VertexCount - 2
                    Dim color As Color

                    If lutN > 0 Then
                        Dim t As Single = If(s < pl.Tc.Count, pl.Tc(s), 0.0F)

                        If t < 0.0F Then t = 0.0F
                        If t > 1.0F Then t = 1.0F

                        color = lut(CInt(t * (lutN - 1)))
                    Else
                        color = Color.FromArgb(200, 56, 89, 119)
                    End If

                    Call lines.Add(New LineSegment(
                        New Point3D(xs(s) + dx, ys(s) + dy, zs(s) + dz),
                        New Point3D(xs(s + 1) + dx, ys(s + 1) + dy, zs(s + 1) + dz),
                        Color.FromArgb(alpha, color)))
                Next
            Next

            Return lines.ToArray()
        End Function

    End Class

    ''' <summary>
    ''' 流线动画驱动：让光点粒子沿各自流线按弧长匀速前进（循环）。
    ''' 粒子等速光点，不映射速度颜色（heat 固定 1.0 = 调色板顶端色）。
    ''' </summary>
    Public Class StreamlineAnimator

        ''' <summary>每条流线布置的粒子数。</summary>
        Public Property ParticlesPerLine As Integer = 2

        ''' <summary>每秒沿流线走过的弧长比例（相对折线全长，1 = 每秒一圈）。</summary>
        Public Property CyclesPerSecond As Single = 0.12F

        ReadOnly m_set As StreamlineSet
        ReadOnly m_phase As Double()
        ReadOnly m_lengths As Double()

        ''' <summary>参与动画的流线（总弧长 &gt; 0）。</summary>
        ReadOnly m_lines As StreamlinePolyline()

        Public Sub New(streamSet As StreamlineSet, Optional particlesPerLine As Integer = 2)
            m_set = streamSet

            If particlesPerLine < 1 Then particlesPerLine = 1

            m_lines = If(streamSet?.Polylines, New StreamlinePolyline() {}) _
                .Where(Function(pl) pl IsNot Nothing AndAlso pl.TotalLength > 0.0) _
                .ToArray()

            m_lengths = New Double(m_lines.Length - 1) {}
            m_phase = New Double(m_lines.Length * particlesPerLine - 1) {}

            ' 各粒子相位错开半个周期的倍数，避免同线粒子重叠
            For l As Integer = 0 To m_lines.Length - 1
                m_lengths(l) = m_lines(l).TotalLength

                For p As Integer = 0 To particlesPerLine - 1
                    m_phase(l * particlesPerLine + p) = m_lengths(l) * p / particlesPerLine
                Next
            Next
        End Sub

        ''' <summary>粒子总数。</summary>
        Public ReadOnly Property ParticleCount As Integer
            Get
                Return m_phase.Length
            End Get
        End Property

        ''' <summary>重置全部粒子相位到线首（错峰布局保持不变）。</summary>
        Public Sub Reset()
            For l As Integer = 0 To m_lines.Length - 1
                For p As Integer = 0 To ParticlesPerLine - 1
                    m_phase(l * ParticlesPerLine + p) = m_lengths(l) * p / ParticlesPerLine
                Next
            Next
        End Sub

        ''' <summary>推进动画（dt 单位：秒）。</summary>
        Public Sub Advance(dt As Double)
            If dt <= 0 Then Return

            Dim rate As Double = Math.Max(0.0001, CyclesPerSecond) * dt

            For l As Integer = 0 To m_lines.Length - 1
                Dim len As Double = m_lengths(l)
                Dim stepLen As Double = len * rate

                For p As Integer = 0 To ParticlesPerLine - 1
                    Dim i As Integer = l * ParticlesPerLine + p
                    m_phase(i) += stepLen

                    If m_phase(i) >= len Then
                        m_phase(i) -= len * Math.Floor(m_phase(i) / len)
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' 把当前粒子位置打包成外部实例缓冲的 Single()（每粒子 8 个 float：
        ''' x, y, z, 尺寸系数 1.0, 0, 0, heat 1.0, 0），整体平移
        ''' (dx, dy, dz)（质心补偿）。返回有效粒子数。
        ''' </summary>
        Public Function Pack(target As Single(),
                             dx As Double, dy As Double, dz As Double) As Integer
            If target Is Nothing OrElse target.Length < 8 Then
                Return 0
            End If

            Dim capacity As Integer = target.Length \ 8
            Dim count As Integer = Math.Min(capacity, m_phase.Length)
            Dim pPerLine As Integer = ParticlesPerLine

            For n As Integer = 0 To count - 1
                Dim l As Integer = n \ pPerLine
                Dim pl As StreamlinePolyline = m_lines(l)
                Dim s As Double = m_phase(n)
                Dim o As Integer = n * 8

                ' 弧长 → 顶点区间（累计弧长单调递增，二分查找）
                Dim lo As Integer = 0, hi As Integer = pl.Cum.Count - 1

                While lo < hi - 1
                    Dim mid As Integer = (lo + hi) >> 1

                    If pl.Cum(mid) <= s Then
                        lo = mid
                    Else
                        hi = mid
                    End If
                End While

                Dim segLen As Double = pl.Cum(hi) - pl.Cum(lo)

                If segLen < 0.0000001 Then
                    hi = lo + 1

                    If hi >= pl.Cum.Count Then
                        hi = lo
                    End If

                    segLen = 1.0
                End If

                Dim t As Double = (s - pl.Cum(lo)) / segLen

                If t < 0.0 Then t = 0.0
                If t > 1.0 Then t = 1.0

                target(o) = CSng(pl.Xs(lo) + (pl.Xs(hi) - pl.Xs(lo)) * t + dx)
                target(o + 1) = CSng(pl.Ys(lo) + (pl.Ys(hi) - pl.Ys(lo)) * t + dy)
                target(o + 2) = CSng(pl.Zs(lo) + (pl.Zs(hi) - pl.Zs(lo)) * t + dz)

                ' 法线槽的 x 分量是逐点尺寸系数；heat = 1.0 取调色板顶端色
                target(o + 3) = 1.0F
                target(o + 4) = 0.0F
                target(o + 5) = 0.0F
                target(o + 6) = 1.0F
                target(o + 7) = 0.0F
            Next

            Return count
        End Function

    End Class

    ''' <summary>
    ''' 流线（Pathlines）追踪器：入口层种子 + RK2 中点法 + 三线性速度插值。
    ''' </summary>
    Public NotInheritable Class StreamlineTracer

        Private Sub New()
        End Sub

        ''' <summary>
        ''' 在速度场上追踪流线。种子取自最小 i 的可种子层
        ''' （<see cref="IFlowFieldData.IsSeedable"/>），j/k 方向按
        ''' <see cref="StreamlineOptions.SeedStride"/> 均匀采样。
        ''' </summary>
        Public Shared Function Trace(field As IFlowFieldData,
                                     options As StreamlineOptions) As StreamlineSet

            If field Is Nothing OrElse options Is Nothing Then
                Return New StreamlineSet With {.Polylines = New StreamlinePolyline() {}}
            End If

            Dim stride As Integer = Math.Max(1, options.SeedStride)
            Dim seeds As New List(Of Integer)

            ' ---- 种子层：最小 i 的可种子层（风洞物理直觉 = 入口面注入）----
            Dim seedI As Integer = -1

            For i As Integer = 0 To field.Nx - 1
                Dim baseX As Integer = i * field.Ny * field.Nz
                Dim found As Boolean = False

                For j As Integer = 0 To field.Ny - 1
                    Dim baseY As Integer = baseX + j * field.Nz

                    For k As Integer = 0 To field.Nz - 1
                        If field.IsSeedable(baseY + k) Then
                            found = True
                            Exit For
                        End If
                    Next

                    If found Then Exit For
                Next

                If found Then
                    seedI = i
                    Exit For
                End If
            Next

            If seedI < 0 Then
                Return New StreamlineSet With {.Polylines = New StreamlinePolyline() {}}
            End If

            Dim baseSeed As Integer = seedI * field.Ny * field.Nz

            For j As Integer = 0 To field.Ny - 1 Step stride
                Dim baseY As Integer = baseSeed + j * field.Nz

                For k As Integer = 0 To field.Nz - 1 Step stride
                    If field.IsSeedable(baseY + k) Then
                        seeds.Add(baseY + k)
                    End If
                Next
            Next

            ' ---- 并行积分（注意全限定，避免解析到 Microsoft.VisualBasic.Parallel）----
            Dim results(seeds.Count - 1) As StreamlinePolyline
            Dim pack As New System.Threading.Tasks.ParallelOptions With {
                .MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            }

            System.Threading.Tasks.Parallel.For(0, seeds.Count, pack,
                Sub(s As Integer)
                    results(s) = Integrate(field, options, seeds(s))
                End Sub)

            ' ---- 按种子顺序合并，超预算截断 ----
            Dim budget As Integer = Math.Max(1, options.MaxSegments)
            Dim kept As New List(Of StreamlinePolyline)(seeds.Count)
            Dim segments As Integer = 0
            Dim truncated As Boolean = False

            For Each pl As StreamlinePolyline In results
                If pl Is Nothing OrElse pl.VertexCount < 2 Then
                    Continue For
                End If

                Dim segs As Integer = pl.VertexCount - 1

                If segments + segs > budget Then
                    truncated = True
                    Exit For
                End If

                segments += segs
                kept.Add(pl)
            Next

            Return New StreamlineSet With {
                .Polylines = kept.ToArray(),
                .SegmentCount = segments,
                .SeedCount = seeds.Count,
                .Truncated = truncated
            }
        End Function

        ''' <summary>单条流线的 RK2 积分。</summary>
        Private Shared Function Integrate(field As IFlowFieldData,
                                          options As StreamlineOptions,
                                          seedIdx As Integer) As StreamlinePolyline

            Dim plane As Integer = field.Ny * field.Nz
            Dim i As Integer = seedIdx \ plane
            Dim rem1 As Integer = seedIdx - i * plane
            Dim j As Integer = rem1 \ field.Nz
            Dim k As Integer = rem1 - j * field.Nz

            Dim x As Double = i
            Dim y As Double = j
            Dim z As Double = k

            Dim h As Double = Math.Max(0.05, options.StepLength)
            Dim minSpeed As Double = options.MinSpeed
            Dim pl As New StreamlinePolyline
            Dim u As Double, v As Double, w As Double

            ' 种子点速度无效则放弃该种子
            If Not Sample(field, x, y, z, u, v, w) Then Return Nothing

            Dim spd As Double = Math.Sqrt(u * u + v * v + w * w)

            If spd < minSpeed Then Return Nothing

            Call pl.Xs.Add(CSng(x)) : Call pl.Ys.Add(CSng(y)) : Call pl.Zs.Add(CSng(z))
            Call pl.Cum.Add(0.0)

            For step1 As Integer = 1 To options.MaxSteps
                ' ---- RK2 中点法：沿归一化方向走固定弧长 ----
                Dim mx As Double = x + (u / spd) * h * 0.5
                Dim my As Double = y + (v / spd) * h * 0.5
                Dim mz As Double = z + (w / spd) * h * 0.5

                Dim mu As Double, mv As Double, mw As Double

                If Not Sample(field, mx, my, mz, mu, mv, mw) Then Exit For

                Dim mspd As Double = Math.Sqrt(mu * mu + mv * mv + mw * mw)

                If mspd < minSpeed Then Exit For

                x += (mu / mspd) * h
                y += (mv / mspd) * h
                z += (mw / mspd) * h

                Dim u2 As Double, v2 As Double, w2 As Double

                If Not Sample(field, x, y, z, u2, v2, w2) Then Exit For

                Dim spd2 As Double = Math.Sqrt(u2 * u2 + v2 * v2 + w2 * w2)

                If spd2 < minSpeed Then Exit For

                Dim last As Integer = pl.Cum.Count - 1
                Dim dx As Double = x - pl.Xs(last)
                Dim dy As Double = y - pl.Ys(last)
                Dim dz As Double = z - pl.Zs(last)

                Call pl.Cum.Add(pl.Cum(last) + Math.Sqrt(dx * dx + dy * dy + dz * dz))
                Call pl.Xs.Add(CSng(x)) : Call pl.Ys.Add(CSng(y)) : Call pl.Zs.Add(CSng(z))
                Call pl.Tc.Add(CSng(Math.Min(1.0, mspd)))

                u = u2 : v = v2 : w = w2 : spd = spd2
            Next

            If pl.VertexCount < 2 Then Return Nothing

            Return pl
        End Function

        ''' <summary>
        ''' 三线性速度插值（连续格点坐标）。任一角点越界或插值失败返回 False。
        ''' </summary>
        Private Shared Function Sample(field As IFlowFieldData,
                                       x As Double, y As Double, z As Double,
                                       ByRef u As Double, ByRef v As Double, ByRef w As Double) As Boolean

            If x < 0 OrElse y < 0 OrElse z < 0 OrElse
               x > field.Nx - 1 OrElse y > field.Ny - 1 OrElse z > field.Nz - 1 Then
                Return False
            End If

            Dim i0 As Integer = CInt(Math.Floor(x))
            Dim j0 As Integer = CInt(Math.Floor(y))
            Dim k0 As Integer = CInt(Math.Floor(z))
            Dim i1 As Integer = Math.Min(i0 + 1, field.Nx - 1)
            Dim j1 As Integer = Math.Min(j0 + 1, field.Ny - 1)
            Dim k1 As Integer = Math.Min(k0 + 1, field.Nz - 1)

            Dim fx As Double = x - i0
            Dim fy As Double = y - j0
            Dim fz As Double = z - k0

            Dim plane As Integer = field.Ny * field.Nz
            Dim iOff As Integer = i0 * plane
            Dim iOff1 As Integer = i1 * plane
            Dim jOff As Integer = j0 * field.Nz
            Dim jOff1 As Integer = j1 * field.Nz

            ' 8 个角点的索引
            Dim idx000 As Integer = iOff + jOff + k0
            Dim idx001 As Integer = iOff + jOff + k1
            Dim idx010 As Integer = iOff + jOff1 + k0
            Dim idx011 As Integer = iOff + jOff1 + k1
            Dim idx100 As Integer = iOff1 + jOff + k0
            Dim idx101 As Integer = iOff1 + jOff + k1
            Dim idx110 As Integer = iOff1 + jOff1 + k0
            Dim idx111 As Integer = iOff1 + jOff1 + k1

            u = Trilerp(AddressOf field.UxAt, idx000, idx001, idx010, idx011, idx100, idx101, idx110, idx111,
                        fx, fy, fz)
            v = Trilerp(AddressOf field.UyAt, idx000, idx001, idx010, idx011, idx100, idx101, idx110, idx111,
                        fx, fy, fz)
            w = Trilerp(AddressOf field.UzAt, idx000, idx001, idx010, idx011, idx100, idx101, idx110, idx111,
                        fx, fy, fz)

            Return Not (Double.IsNaN(u) OrElse Double.IsNaN(v) OrElse Double.IsNaN(w))
        End Function

        Private Delegate Function ComponentAt(idx As Integer) As Single

        Private Shared Function Trilerp(at As ComponentAt,
                                        i000 As Integer, i001 As Integer,
                                        i010 As Integer, i011 As Integer,
                                        i100 As Integer, i101 As Integer,
                                        i110 As Integer, i111 As Integer,
                                        fx As Double, fy As Double, fz As Double) As Double
            Dim c00 As Double = at(i000) * (1.0 - fz) + at(i001) * fz
            Dim c01 As Double = at(i010) * (1.0 - fz) + at(i011) * fz
            Dim c10 As Double = at(i100) * (1.0 - fz) + at(i101) * fz
            Dim c11 As Double = at(i110) * (1.0 - fz) + at(i111) * fz

            Dim c0 As Double = c00 * (1.0 - fy) + c01 * fy
            Dim c1 As Double = c10 * (1.0 - fy) + c11 * fy

            Return c0 * (1.0 - fx) + c1 * fx
        End Function

    End Class

End Namespace
