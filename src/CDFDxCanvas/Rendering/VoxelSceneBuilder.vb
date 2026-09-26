' /********************************************************************************/
'
'   VoxelSceneBuilder.vb
'
'   体素场景构建器 —— 帧 + 视图状态 → 点云 / 线段
'
'   作用：
'       把一帧 CFD 体数据按当前视图状态（标量场、值域、阈值、横截面、
'       箭头、拾取高亮）转换为 DxScene3DCanvas 可渲染的点云与线段集合。
'       热图颜色由调用方传入的 LUT 查表得到（LUT 由 Designer.FromSchema 生成）。
'
'   质心补偿：
'       Scene.LoadPointCloud 每次都会把点云按其质心平移到原点。横截面与
'       阈值过滤会改变可见子集的质心，导致模型在屏幕上跳动。这里对传给
'       场景的坐标做补偿（+ C_full - C_visible），使传入集合的质心恒等于
'       全体活动体素的质心，从而渲染位置恒定为 世界坐标 - C_full。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports CDFDxCanvas.Data

Namespace Rendering

    ''' <summary>
    ''' 视图状态快照（渲染参数的只读载体）。
    ''' </summary>
    Public Class VoxelViewOptions

        Public Property Field As CfdField
        Public Property RangeMin As Double
        Public Property RangeMax As Double
        ''' <summary>透明阈值 0..1（归一化值 &lt; 阈值的体素被隐藏）。</summary>
        Public Property Threshold As Double

        Public Property SectionEnabled As Boolean
        Public Property SectionAxis As CfdAxis
        Public Property SectionPosition As Integer

        Public Property ShowArrows As Boolean
        ''' <summary>箭头采样密度（2/3/4，步长）。</summary>
        Public Property ArrowDensity As Integer

        ''' <summary>选中的体素索引（引擎序），-1 = 无。</summary>
        Public Property SelectedVoxel As Integer = -1

        ''' <summary>热图 LUT（由 Designer.FromSchema 生成，256 级）。</summary>
        Public Property Lut As Color()

    End Class

    ''' <summary>
    ''' 一次构建的结果：点云、点索引 → 体素索引映射、线段（箭头 + 高亮框）。
    ''' </summary>
    Public Class VoxelSceneResult

        Public Property Points As PointCloudPoint()
        ''' <summary>Points 数组下标 → 体素引擎索引（拾取映射用）。</summary>
        Public Property PointVoxelMap As Integer()
        Public Property Lines As LineSegment()

    End Class

    Public Class VoxelSceneBuilder

        ' 可复用的可见性缓冲（按需扩容）
        Shared m_visibility As Boolean()
        Shared m_visibleList As Integer()

        ''' <summary>
        ''' 构建当前帧的点云与线段。
        ''' </summary>
        ''' <param name="dataset">数据集（提供几何与活动体素表）</param>
        ''' <param name="frame">当前帧</param>
        ''' <param name="options">视图状态</param>
        Public Shared Function Build(dataset As CfdDataset, frame As VtiFrameData, options As VoxelViewOptions) As VoxelSceneResult
            Dim nx = dataset.Nx, ny = dataset.Ny, nz = dataset.Nz
            Dim sp = dataset.Spacing, org = dataset.Origin
            Dim active = dataset.ActiveIndices
            Dim centroid = dataset.FullCentroid

            Dim values As Single() = FieldArray(frame, options.Field)
            Dim mn As Double = options.RangeMin
            Dim mx As Double = Math.Max(options.RangeMax, mn + 1e-12)
            Dim denom As Double = mx - mn
            Dim threshold As Double = options.Threshold

            ' ---- 截面裁剪参数：保留 c <= cut 一侧（法向 -axis，与网页版一致）----
            Dim cut As Double = 0.0
            If options.SectionEnabled Then
                cut = SliceCut(dataset, options.SectionAxis, options.SectionPosition)
            End If

            ' ---- 第一遍：计算可见性 ----
            Dim n As Integer = active.Length
            If m_visibility Is Nothing OrElse m_visibility.Length < n Then
                m_visibility = New Boolean(n * 2 - 1) {}
            End If
            If m_visibleList Is Nothing OrElse m_visibleList.Length < n Then
                m_visibleList = New Integer(n * 2 - 1) {}
            End If

            Dim visibleCount As Integer = 0
            Dim sx As Double = 0.0, sy As Double = 0.0, sz As Double = 0.0

            For t As Integer = 0 To n - 1
                Dim idx As Integer = active(t)
                Dim visible As Boolean = True

                If options.SectionEnabled Then
                    visible = SliceCoord(dataset, options.SectionAxis, idx) <= cut
                End If

                If visible AndAlso threshold > 0 Then
                    Dim v As Double = values(idx)
                    Dim tc As Double = (v - mn) / denom
                    If tc < threshold Then
                        visible = False
                    End If
                End If

                m_visibility(t) = visible

                If visible Then
                    Dim cx As Double, cy As Double, cz As Double
                    Call VoxelCenter(dataset, idx, cx, cy, cz)
                    sx += cx : sy += cy : sz += cz
                    m_visibleList(visibleCount) = t
                    visibleCount += 1
                End If
            Next

            If visibleCount = 0 Then
                ' 全部被过滤（极端参数），退化为无点云
                Return New VoxelSceneResult With {
                    .Points = New PointCloudPoint() {},
                    .PointVoxelMap = New Integer() {},
                    .Lines = New LineSegment() {}
                }
            End If

            ' ---- 质心补偿量：使传入点集的质心恒为 C_full ----
            Dim fx As Double = sx / visibleCount
            Dim fy As Double = sy / visibleCount
            Dim fz As Double = sz / visibleCount
            Dim ox As Double = centroid(0) - fx
            Dim oy As Double = centroid(1) - fy
            Dim oz As Double = centroid(2) - fz

            ' ---- 第二遍：填点云 ----
            Dim points(visibleCount - 1) As PointCloudPoint
            Dim pointMap(visibleCount - 1) As Integer
            Dim lut = options.Lut
            Dim lutN As Integer = lut.Length

            For p As Integer = 0 To visibleCount - 1
                Dim t As Integer = m_visibleList(p)
                Dim idx As Integer = active(t)

                Dim cx As Double, cy As Double, cz As Double
                Call VoxelCenter(dataset, idx, cx, cy, cz)

                Dim v As Double = values(idx)
                Dim tc As Double = (v - mn) / denom

                If tc < 0.0 Then
                    tc = 0.0
                ElseIf tc > 1.0 Then
                    tc = 1.0
                End If

                Dim color As Color = lut(CInt(tc * (lutN - 1)))

                points(p) = New PointCloudPoint(cx + ox, cy + oy, cz + oz, tc, HtmlColor(color))
                pointMap(p) = idx
            Next

            ' ---- 线段：箭头 + 拾取高亮框 ----
            Dim lines As New List(Of LineSegment)

            If options.ShowArrows Then
                Call BuildArrows(dataset, frame, options, mn, mx, ox, oy, oz, lines)
            End If

            If options.SelectedVoxel >= 0 Then
                Dim t As Integer = IndexOfActive(active, options.SelectedVoxel)

                If t >= 0 AndAlso m_visibility(t) Then
                    Call BuildHighlight(dataset, options.SelectedVoxel, ox, oy, oz, lines)
                End If
            End If

            Return New VoxelSceneResult With {
                .Points = points,
                .PointVoxelMap = pointMap,
                .Lines = lines.ToArray()
            }
        End Function

        ''' <summary>
        ''' 速度矢量箭头（线段近似）：从体素中心沿速度方向画线段，
        ''' 长度 = speed/speedMax * maxLen，maxLen = spacing * 1.7，
        ''' 颜色按速度热图着色（与网页版行为一致）。
        ''' </summary>
        Private Shared Sub BuildArrows(dataset As CfdDataset, frame As VtiFrameData,
                                       options As VoxelViewOptions,
                                       mn As Double, mx As Double,
                                       ox As Double, oy As Double, oz As Double,
                                       lines As List(Of LineSegment))
            Dim nx = dataset.Nx, ny = dataset.Ny, nz = dataset.Nz
            Dim sp = dataset.Spacing, org = dataset.Origin
            Dim stride As Integer = Math.Max(1, options.ArrowDensity)

            ' 速度箭头固定按速度幅值着色与定长
            Dim speedMax As Double = dataset.GetRange(CfdField.Speed).Item2
            If speedMax <= 0 Then speedMax = 1.0

            Dim lut = options.Lut
            Dim lutN As Integer = lut.Length
            Dim maxLen As Double = sp(0) * 1.7
            Dim arrowColor As Color

            For i As Integer = 0 To nx - 1 Step stride
                For j As Integer = 0 To ny - 1 Step stride
                    For k As Integer = 0 To nz - 1 Step stride
                        Dim idx As Integer = dataset.IJKToIdx(i, j, k)

                        If Not frame.Mask(idx) Then
                            Continue For
                        End If

                        ' 箭头同样受截面裁剪约束
                        If options.SectionEnabled AndAlso
                            SliceCoord(dataset, options.SectionAxis, idx) > SliceCut(dataset, options.SectionAxis, options.SectionPosition) Then
                            Continue For
                        End If

                        Dim uu As Double = frame.U(idx)
                        Dim vv As Double = frame.V(idx)
                        Dim ww As Double = frame.W(idx)
                        Dim spd As Double = Math.Sqrt(uu * uu + vv * vv + ww * ww)

                        If spd < 1e-9 Then
                            Continue For
                        End If

                        Dim cx As Double = org(0) + (i + 0.5) * sp(0)
                        Dim cy As Double = org(1) + (j + 0.5) * sp(1)
                        Dim cz As Double = org(2) + (k + 0.5) * sp(2)

                        Dim len As Double = (spd / speedMax) * maxLen
                        Dim tx As Double = cx + (uu / spd) * len
                        Dim ty As Double = cy + (vv / spd) * len
                        Dim tz As Double = cz + (ww / spd) * len

                        Dim tc As Double = spd / speedMax
                        If tc > 1.0 Then tc = 1.0
                        arrowColor = lut(CInt(tc * (lutN - 1)))

                        Call lines.Add(New LineSegment(
                            New Point3D(cx + ox, cy + oy, cz + oz),
                            New Point3D(tx + ox, ty + oy, tz + oz),
                            Color.FromArgb(235, arrowColor)))
                    Next
                Next
            Next
        End Sub

        ''' <summary>选中体素的高亮线框盒（12 条棱，略大于体素）。</summary>
        Private Shared Sub BuildHighlight(dataset As CfdDataset, idx As Integer,
                                          ox As Double, oy As Double, oz As Double,
                                          lines As List(Of LineSegment))
            Dim i, j, k As Integer
            Call dataset.IdxToIJK(idx, i, j, k)

            Dim sp = dataset.Spacing, org = dataset.Origin
            Dim cx As Double = org(0) + (i + 0.5) * sp(0)
            Dim cy As Double = org(1) + (j + 0.5) * sp(1)
            Dim cz As Double = org(2) + (k + 0.5) * sp(2)

            Dim hx As Double = sp(0) * 0.56
            Dim hy As Double = sp(1) * 0.56
            Dim hz As Double = sp(2) * 0.56

            ' 8 个角点（补偿空间）
            Dim corners(7) As Point3D
            Dim n As Integer = 0

            For Each s As Integer In {0, 1}
                For Each t As Integer In {0, 1}
                    For Each r As Integer In {0, 1}
                        corners(n) = New Point3D(
                            cx + If(s = 0, -hx, hx) + ox,
                            cy + If(t = 0, -hy, hy) + oy,
                            cz + If(r = 0, -hz, hz) + oz)
                        n += 1
                    Next
                Next
            Next

            Dim color As Color = Color.FromArgb(255, 17, 24, 39)

            ' 底面 / 顶面 / 竖棱（角点顺序: r 最内层）
            ' corners: 0..3 = z-, 4..7 = z+
            Call lines.Add(New LineSegment(corners(0), corners(1), color))
            Call lines.Add(New LineSegment(corners(1), corners(3), color))
            Call lines.Add(New LineSegment(corners(3), corners(2), color))
            Call lines.Add(New LineSegment(corners(2), corners(0), color))

            Call lines.Add(New LineSegment(corners(4), corners(5), color))
            Call lines.Add(New LineSegment(corners(5), corners(7), color))
            Call lines.Add(New LineSegment(corners(7), corners(6), color))
            Call lines.Add(New LineSegment(corners(6), corners(4), color))

            Call lines.Add(New LineSegment(corners(0), corners(4), color))
            Call lines.Add(New LineSegment(corners(1), corners(5), color))
            Call lines.Add(New LineSegment(corners(3), corners(7), color))
            Call lines.Add(New LineSegment(corners(2), corners(6), color))
        End Sub

        ''' <summary>Color → "#RRGGBB"（DxCanvas 嵌入色路径按 6 位 hex 解析）。</summary>
        Private Shared Function HtmlColor(c As Color) As String
            Return $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        End Function

        ''' <summary>读取帧内某标量场数组。</summary>
        Public Shared Function FieldArray(frame As VtiFrameData, field As CfdField) As Single()
            Select Case field
                Case CfdField.Pressure : Return frame.Pressure
                Case CfdField.Density : Return frame.Density
                Case CfdField.Speed : Return frame.Speed
                Case Else : Return frame.Pressure
            End Select
        End Function

        ''' <summary>体素中心世界坐标。</summary>
        Public Shared Sub VoxelCenter(dataset As CfdDataset, idx As Integer,
                                      ByRef cx As Double, ByRef cy As Double, ByRef cz As Double)
            Dim i, j, k As Integer
            Call dataset.IdxToIJK(idx, i, j, k)

            Dim sp = dataset.Spacing, org = dataset.Origin
            cx = org(0) + (i + 0.5) * sp(0)
            cy = org(1) + (j + 0.5) * sp(1)
            cz = org(2) + (k + 0.5) * sp(2)
        End Sub

        ''' <summary>截面某轴上的切割坐标 cut = origin + (pos + 0.5) * spacing。</summary>
        Public Shared Function SliceCut(dataset As CfdDataset, axis As CfdAxis, pos As Integer) As Double
            Select Case axis
                Case CfdAxis.X : Return dataset.Origin(0) + (pos + 0.5) * dataset.Spacing(0)
                Case CfdAxis.Y : Return dataset.Origin(1) + (pos + 0.5) * dataset.Spacing(1)
                Case Else : Return dataset.Origin(2) + (pos + 0.5) * dataset.Spacing(2)
            End Select
        End Function

        ''' <summary>体素中心在某轴上的坐标。</summary>
        Public Shared Function SliceCoord(dataset As CfdDataset, axis As CfdAxis, idx As Integer) As Double
            Dim i, j, k As Integer
            Call dataset.IdxToIJK(idx, i, j, k)

            Select Case axis
                Case CfdAxis.X : Return dataset.Origin(0) + (i + 0.5) * dataset.Spacing(0)
                Case CfdAxis.Y : Return dataset.Origin(1) + (j + 0.5) * dataset.Spacing(1)
                Case Else : Return dataset.Origin(2) + (k + 0.5) * dataset.Spacing(2)
            End Select
        End Function

        ''' <summary>体素索引 → 活动表下标（二分查找；活动表按引擎序递增）。</summary>
        Private Shared Function IndexOfActive(active As Integer(), idx As Integer) As Integer
            Dim lo As Integer = 0, hi As Integer = active.Length - 1

            Do While lo <= hi
                Dim mid As Integer = (lo + hi) >> 1

                If active(mid) = idx Then
                    Return mid
                ElseIf active(mid) < idx Then
                    lo = mid + 1
                Else
                    hi = mid - 1
                End If
            Loop

            Return -1
        End Function

    End Class

End Namespace
