' /********************************************************************************/
'
'   FlowVectors.vb
'
'   矢量图构建器 —— 速度场 → 带箭头头部的线段矢量（LINELIST 通道）
'
'   作用：
'       把一个 IFlowFieldData 速度场按步长采样，为每个活动格点生成
'       主干线段 + V 形箭头头部（每矢量 3 段折线）：
'           - 箭头方向 = 局部速度方向
'           - 箭头长度 ∝ 速度大小（相对 SpeedMax 归一化）
'           - 箭头颜色 = 调色板 LUT 按速度着色（与热图色标一致）
'       适合分析涡旋、剪切层等复杂流动结构。
'
'   坐标约定：
'       世界坐标 = Origin + (i + CenterOffset) * Spacing。
'       实时风洞（格子坐标系）Origin=0, Spacing=1, CenterOffset=0；
'       离线体素数据集（体素中心坐标）CenterOffset=0.5。
'       返回的线段处于原始世界坐标，调用方按需做质心补偿平移。
'
'   共享逻辑：
'       本类型与 StreamlineTracer 同属共享构建器，实时程序
'       （WindTunnelCanvas）与离线查看器（VoxelSceneBuilder）通过
'       IFlowFieldData 适配器复用同一实现。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Imaging.Drawing3D

Namespace Rendering

    ''' <summary>
    ''' 速度场数据契约：矢量图与流线追踪共用的最小只读视图。
    ''' </summary>
    ''' <remarks>
    ''' 索引布局与两个调用方的宿主数组一致：idx = i * Ny * Nz + j * Nz + k。
    ''' </remarks>
    Public Interface IFlowFieldData

        ReadOnly Property Nx As Integer
        ReadOnly Property Ny As Integer
        ReadOnly Property Nz As Integer

        ''' <summary>世界坐标原点（实时风洞为 0，离线为 CfdDataset.Origin）。</summary>
        ReadOnly Property OriginX As Single
        ReadOnly Property OriginY As Single
        ReadOnly Property OriginZ As Single

        ''' <summary>各轴格距（实时风洞为 1，离线为 CfdDataset.Spacing）。</summary>
        ReadOnly Property SpacingX As Single
        ReadOnly Property SpacingY As Single
        ReadOnly Property SpacingZ As Single

        ''' <summary>
        ''' 体素中心的格内偏移：实时风洞格子坐标系为 0，
        ''' 离线体素中心坐标为 0.5。
        ''' </summary>
        ReadOnly Property CenterOffset As Single

        Function UxAt(idx As Integer) As Single
        Function UyAt(idx As Integer) As Single
        Function UzAt(idx As Integer) As Single

        ''' <summary>
        ''' 该格点是否参与矢量/点云渲染（实时风洞 = 流体格点；
        ''' 离线 = 活动体素掩膜）。
        ''' </summary>
        Function IsActive(idx As Integer) As Boolean

        ''' <summary>
        ''' 该格点是否可作为流线种子（实时风洞 = 流体或入口；
        ''' 离线 = 活动体素掩膜）。默认与 <see cref="IsActive"/> 相同。
        ''' </summary>
        Function IsSeedable(idx As Integer) As Boolean

    End Interface

    ''' <summary>
    ''' 矢量图渲染参数。
    ''' </summary>
    Public Class VectorArrowOptions

        ''' <summary>采样步长（每轴，格点）。</summary>
        Public Property Stride As Integer = 3

        ''' <summary>最大箭头长度（相对最大格距的倍数）。</summary>
        Public Property MaxLengthFactor As Single = 1.7F

        ''' <summary>箭头头部总长相对箭头长度的比例上限系数。</summary>
        Public Property HeadFactor As Single = 0.45F

        ''' <summary>速度归一化上限；&lt;= 0 时自动扫描活动格点求最大速度。</summary>
        Public Property SpeedMax As Double = 0.0

        ''' <summary>热图 LUT（Designer.FromSchema 生成，256 级）；Nothing 时用默认灰。</summary>
        Public Property Lut As Color()

        ''' <summary>线段透明度（0-255）。</summary>
        Public Property Alpha As Integer = 235

        ''' <summary>低于该速度的格点不画矢量。</summary>
        Public Property MinSpeed As Double = 0.000000001

        ''' <summary>
        ''' 采样点过滤器（返回 False 则跳过该格点），用于横截面裁剪、
        ''' 显示阈值等调用方特有的过滤规则；Nothing 表示不过滤。
        ''' </summary>
        Public Property IncludePoint As Func(Of Integer, Integer, Integer, Boolean)

    End Class

    ''' <summary>
    ''' 矢量图（Vectors）构建器：速度场 → 线段箭头集合。
    ''' </summary>
    Public NotInheritable Class FlowVectors

        Private Sub New()
        End Sub

        ''' <summary>
        ''' 扫描活动格点估计速度上限（<see cref="VectorArrowOptions.SpeedMax"/>
        ''' 未指定时的缺省估计；只扫描活动格点）。
        ''' </summary>
        Public Shared Function EstimateSpeedMax(field As IFlowFieldData) As Double
            Dim max2 As Double = 0.0

            For i As Integer = 0 To field.Nx - 1
                Dim baseX As Integer = i * field.Ny * field.Nz

                For j As Integer = 0 To field.Ny - 1
                    Dim baseY As Integer = baseX + j * field.Nz

                    For k As Integer = 0 To field.Nz - 1
                        Dim idx As Integer = baseY + k

                        If Not field.IsActive(idx) Then
                            Continue For
                        End If

                        Dim u As Double = field.UxAt(idx)
                        Dim v As Double = field.UyAt(idx)
                        Dim w As Double = field.UzAt(idx)
                        Dim s2 As Double = u * u + v * v + w * w

                        If s2 > max2 Then
                            max2 = s2
                        End If
                    Next
                Next
            Next

            Return Math.Sqrt(max2)
        End Function

        ''' <summary>
        ''' 构建矢量图线段集合（每矢量 3 段：主干 + V 形头部）。
        ''' </summary>
        ''' <returns>
        ''' 原始世界坐标下的线段数组；调用方如需质心补偿请自行平移。
        ''' </returns>
        Public Shared Function BuildArrows(field As IFlowFieldData,
                                           options As VectorArrowOptions) As LineSegment()

            If field Is Nothing OrElse options Is Nothing Then
                Return New LineSegment() {}
            End If

            Dim stride As Integer = Math.Max(1, options.Stride)
            Dim speedMax As Double = options.SpeedMax

            If speedMax <= 0 Then
                speedMax = EstimateSpeedMax(field)
            End If

            If speedMax <= 0 Then
                speedMax = 1.0
            End If

            Dim maxLen As Double = options.MaxLengthFactor *
                Math.Max(field.SpacingX, Math.Max(field.SpacingY, field.SpacingZ))
            Dim lut As Color() = options.Lut
            Dim lutN As Integer = If(lut IsNot Nothing, lut.Length, 0)
            Dim alpha As Integer = Math.Max(0, Math.Min(255, options.Alpha))
            Dim minSpeed As Double = Math.Max(0.0, options.MinSpeed)
            Dim include As Func(Of Integer, Integer, Integer, Boolean) = options.IncludePoint
            Dim off As Single = field.CenterOffset
            Dim lines As New List(Of LineSegment)

            For i As Integer = 0 To field.Nx - 1 Step stride
                Dim baseX As Integer = i * field.Ny * field.Nz

                For j As Integer = 0 To field.Ny - 1 Step stride
                    Dim baseY As Integer = baseX + j * field.Nz

                    For k As Integer = 0 To field.Nz - 1 Step stride
                        Dim idx As Integer = baseY + k

                        If Not field.IsActive(idx) Then
                            Continue For
                        End If

                        If include IsNot Nothing AndAlso Not include(i, j, k) Then
                            Continue For
                        End If

                        Dim u As Double = field.UxAt(idx)
                        Dim v As Double = field.UyAt(idx)
                        Dim w As Double = field.UzAt(idx)
                        Dim spd As Double = Math.Sqrt(u * u + v * v + w * w)

                        If spd < minSpeed Then
                            Continue For
                        End If

                        Dim tc As Double = spd / speedMax

                        If tc > 1.0 Then
                            tc = 1.0
                        End If

                        Dim cx As Double = field.OriginX + (i + off) * field.SpacingX
                        Dim cy As Double = field.OriginY + (j + off) * field.SpacingY
                        Dim cz As Double = field.OriginZ + (k + off) * field.SpacingZ

                        Dim len As Double = tc * maxLen
                        Dim tx As Double = cx + (u / spd) * len
                        Dim ty As Double = cy + (v / spd) * len
                        Dim tz As Double = cz + (w / spd) * len

                        Dim color As Color

                        If lutN > 0 Then
                            color = lut(CInt(tc * (lutN - 1)))
                        Else
                            color = Color.Gray
                        End If

                        color = Color.FromArgb(alpha, color)

                        ' ---- 主干 ----
                        Call lines.Add(New LineSegment(
                            New Point3D(cx, cy, cz),
                            New Point3D(tx, ty, tz), color))

                        ' ---- V 形箭头头部（单平面双翼）----
                        Dim headLen As Double = Math.Min(len * options.HeadFactor, maxLen * 0.35)

                        If headLen > 0.0001 Then
                            ' 单位方向
                            Dim dx As Double = u / spd
                            Dim dy As Double = v / spd
                            Dim dz As Double = w / spd

                            ' 与方向正交的单位向量：取与方向最不对齐的坐标轴做叉积，
                            ' 避免方向与轴平行时退化
                            Dim ax As Double = Math.Abs(dx)
                            Dim ay As Double = Math.Abs(dy)
                            Dim az As Double = Math.Abs(dz)
                            Dim px As Double, py As Double, pz As Double

                            If ax <= ay AndAlso ax <= az Then
                                ' 叉积 dir × (1,0,0)
                                px = 0.0 : py = dz : pz = -dy
                            ElseIf ay <= az Then
                                ' 叉积 dir × (0,1,0)
                                px = -dz : py = 0.0 : pz = dx
                            Else
                                ' 叉积 dir × (0,0,1)
                                px = dy : py = -dx : pz = 0.0
                            End If

                            Dim plen As Double = Math.Sqrt(px * px + py * py + pz * pz)

                            If plen > 0.0000001 Then
                                px /= plen : py /= plen : pz /= plen
                                Dim wing As Double = headLen * 0.5
                                Dim bx As Double = tx - dx * headLen
                                Dim by As Double = ty - dy * headLen
                                Dim bz As Double = tz - dz * headLen

                                Call lines.Add(New LineSegment(
                                    New Point3D(tx, ty, tz),
                                    New Point3D(bx + px * wing, by + py * wing, bz + pz * wing), color))

                                Call lines.Add(New LineSegment(
                                    New Point3D(tx, ty, tz),
                                    New Point3D(bx - px * wing, by - py * wing, bz - pz * wing), color))
                            End If
                        End If
                    Next
                Next
            Next

            Return lines.ToArray()
        End Function

    End Class

End Namespace
