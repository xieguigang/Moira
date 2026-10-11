' /********************************************************************************/
'
'   EulerianFermentationTankCase.vb
'
'   发酵罐搅拌算例：几何生成（自包含 ASCII STL 写出器）→ 内流 Eulerian 网格 →
'   MRF 桨盘 + 桨叶运动壁面 + VOF 自由液面（液面高度 = 罐高 2/3）→ SIMPLE/PIMPLE 求解。
'
'   几何（Rushton 标准构型，几何比例沿用 FVMEngine 既有的 FvmTank）：
'       容器        立式圆柱罐，内径 D，高 H = 1.5·D，平底 + 敞口顶（顶面为自由液面/顶空）
'       挡板        4 块，宽 D/10，厚 5 mm，从罐底到 0.9H
'       搅拌桨      Rushton 六直叶涡轮：桨径 Di = D/3，桨叶高 Di/5，厚 4 mm，
'                   安装高度 z = Di
'       搅拌轴      半径 5 mm，从桨中心到罐顶
'
'   液相：水（ρ=998, ν=1.0e-6）；气相：空气（ρ=1.2, ν=1.5e-5）
'   初始液位：z ≤ 2/3·H 为液相（等截面圆柱罐中，体积分数恰等于高度比 → 2/3 体积）
'
'   网格生成策略：
'       容器 + 次级实体分别写成**独立闭合 STL**，交给 GeometryPreprocessor 的
'       Internal 模式（容器内部为流体、次级实体内部为固体），
'       再由 OctreeCutCellGenerator（cut-cells）生成贴体多面体网格。
'       边界面的 patch 归属用"区域盒规则"区分（桨叶 / 轴 / 罐壁）。
'
'   ⚠ 单位与量级：本文件全部使用 SI（m, s, rad/s）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

#Region "自包含 ASCII STL 写出器"

    ''' <summary>
    ''' 极简 ASCII STL 写出器（不引入 Landscape 依赖，便于在 FVMEngine 内独立生成搅拌罐几何）。
    ''' 面片绕序遵循右手定则（法向自实体指向外部），Landscape 体素化据此判定实体内外。
    ''' </summary>
    Public Class AsciiStlWriter

        Private ReadOnly _sb As New StringBuilder(4096)
        Private ReadOnly _name As String
        Private ReadOnly _inv As Double
        Private _facets As Integer

        ''' <param name="name">solid 名称。</param>
        ''' <param name="scale">坐标缩放（模型单位 → 米）；1.0 表示已是米。</param>
        Public Sub New(name As String, Optional scale As Double = 1.0)
            _name = If(name, "solid")
            _inv = 1.0 / scale

            _sb.AppendLine($"solid {_name}")
        End Sub

        ''' <summary>已写出的三角面片数。</summary>
        Public ReadOnly Property FacetCount As Integer
            Get
                Return _facets
            End Get
        End Property

        ''' <summary>写入一个三角面片（法向由绕序的右手定则自动计算）。</summary>
        Public Sub AddTriangle(ax As Double, ay As Double, az As Double,
                               bx As Double, by As Double, bz As Double,
                               cx As Double, cy As Double, cz As Double)

            ax *= _inv : ay *= _inv : az *= _inv
            bx *= _inv : by *= _inv : bz *= _inv
            cx *= _inv : cy *= _inv : cz *= _inv

            Dim e1x As Double = bx - ax, e1y As Double = by - ay, e1z As Double = bz - az
            Dim e2x As Double = cx - ax, e2y As Double = cy - ay, e2z As Double = cz - az

            Dim nx As Double = e1y * e2z - e1z * e2y
            Dim ny As Double = e1z * e2x - e1x * e2z
            Dim nz As Double = e1x * e2y - e1y * e2x
            Dim len As Double = std.Sqrt(nx * nx + ny * ny + nz * nz)

            If len <= 1.0E-30 Then Return

            nx /= len : ny /= len : nz /= len

            _sb.AppendLine(FormattableString.Invariant($"  facet normal {nx:G9} {ny:G9} {nz:G9}"))
            _sb.AppendLine("    outer loop")
            _sb.AppendLine(FormattableString.Invariant($"      vertex {ax:G9} {ay:G9} {az:G9}"))
            _sb.AppendLine(FormattableString.Invariant($"      vertex {bx:G9} {by:G9} {bz:G9}"))
            _sb.AppendLine(FormattableString.Invariant($"      vertex {cx:G9} {cy:G9} {cz:G9}"))
            _sb.AppendLine("    endloop")
            _sb.AppendLine("  endfacet")

            _facets += 1
        End Sub

        ''' <summary>写入一个四边形（自动三角剖分）。</summary>
        Public Sub AddQuad(ax As Double, ay As Double, az As Double,
                           bx As Double, by As Double, bz As Double,
                           cx As Double, cy As Double, cz As Double,
                           dx As Double, dy As Double, dz As Double)

            Call AddTriangle(ax, ay, az, bx, by, bz, cx, cy, cz)
            Call AddTriangle(ax, ay, az, cx, cy, cz, dx, dy, dz)
        End Sub

        ''' <summary>
        ''' 写入轴对齐长方体（8 角点布局与 Eulerian 六面体一致）。
        ''' </summary>
        Public Sub AddBox(minX As Double, minY As Double, minZ As Double,
                          maxX As Double, maxY As Double, maxZ As Double)

            Dim x(1) As Double
            Dim y(1) As Double
            Dim z(1) As Double

            x(0) = minX : x(1) = maxX
            y(0) = minY : y(1) = maxY
            z(0) = minZ : z(1) = maxZ

            Dim c As Double()() = {
                New Double() {x(0), y(0), z(0)}, New Double() {x(1), y(0), z(0)},
                New Double() {x(1), y(1), z(0)}, New Double() {x(0), y(1), z(0)},
                New Double() {x(0), y(0), z(1)}, New Double() {x(1), y(0), z(1)},
                New Double() {x(1), y(1), z(1)}, New Double() {x(0), y(1), z(1)}}

            Call AddHexCorners(c)
        End Sub

        ''' <summary>由 8 个角点写入六面体（角点布局同 <see cref="AddBox"/>）。</summary>
        Public Sub AddHexCorners(c As Double()())
            Dim faces As Integer()() = {
                New Integer() {0, 3, 2, 1}, New Integer() {4, 5, 6, 7},
                New Integer() {0, 4, 7, 3}, New Integer() {1, 2, 6, 5},
                New Integer() {0, 1, 5, 4}, New Integer() {3, 7, 6, 2}}

            For Each f As Integer() In faces
                Dim a As Double() = c(f(0))
                Dim b As Double() = c(f(1))
                Dim d As Double() = c(f(2))
                Dim e As Double() = c(f(3))

                Call AddQuad(a(0), a(1), a(2), b(0), b(1), b(2), d(0), d(1), d(2), e(0), e(1), e(2))
            Next
        End Sub

        ''' <summary>
        ''' 写入闭合圆柱（侧面 + 上下端盖），法向朝外。
        ''' </summary>
        ''' <param name="segments">周向分段数（&gt;= 8）。</param>
        Public Sub AddClosedCylinder(centerX As Double, centerY As Double,
                                     z0 As Double, z1 As Double, radius As Double,
                                     Optional segments As Integer = 64)

            Dim n As Integer = std.Max(8, segments)
            Dim cx As Double = centerX
            Dim cy As Double = centerY
            Dim zLo As Double = std.Min(z0, z1)
            Dim zHi As Double = std.Max(z0, z1)

            For i As Integer = 0 To n - 1
                Dim t0 As Double = 2.0 * std.PI * i / n
                Dim t1 As Double = 2.0 * std.PI * (i + 1) / n

                Dim b0x As Double = cx + radius * std.Cos(t0)
                Dim b0y As Double = cy + radius * std.Sin(t0)
                Dim b1x As Double = cx + radius * std.Cos(t1)
                Dim b1y As Double = cy + radius * std.Sin(t1)

                ' 侧面（外法向）：b_i → b_{i+1} → t_{i+1} → t_i
                Call AddQuad(b0x, b0y, zLo, b1x, b1y, zLo, b1x, b1y, zHi, b0x, b0y, zHi)

                ' 底面（外法向 −Z）：center → b_{i+1} → b_i
                Call AddTriangle(cx, cy, zLo, b1x, b1y, zLo, b0x, b0y, zLo)

                ' 顶面（外法向 +Z）：center → t_i → t_{i+1}
                Call AddTriangle(cx, cy, zHi, b0x, b0y, zHi, b1x, b1y, zHi)
            Next
        End Sub

        ''' <summary>写出文件（父目录会自动创建）。</summary>
        Public Sub Save(filePath As String)
            _sb.AppendLine($"endsolid {_name}")

            Dim dir As String = IO.Path.GetDirectoryName(IO.Path.GetFullPath(filePath))

            If Not String.IsNullOrEmpty(dir) Then
                Call IO.Directory.CreateDirectory(dir)
            End If

            IO.File.WriteAllText(filePath, _sb.ToString(), New UTF8Encoding(False))
        End Sub

    End Class

#End Region

#Region "算例配置"

    ''' <summary>
    ''' 轴对齐几何区域盒（用于把"整体壁面"细分为桨叶 / 轴 / 罐壁等 patch）。
    ''' 说明：FVMEngine 不引用 ZMesh，因此这里只提供纯数据；
    ''' 由持有 ZMesh 的调用方转换为 <c>PatchRule.Region</c>。
    ''' </summary>
    Public Class EulerianRegionBox

        Public Property Name As String = ""
        Public Property MinX As Double = 0
        Public Property MinY As Double = 0
        Public Property MinZ As Double = 0
        Public Property MaxX As Double = 0
        Public Property MaxY As Double = 0
        Public Property MaxZ As Double = 0

        Public Function Contains(x As Double, y As Double, z As Double, Optional tol As Double = 0) As Boolean
            Return x >= MinX - tol AndAlso x <= MaxX + tol AndAlso
                   y >= MinY - tol AndAlso y <= MaxY + tol AndAlso
                   z >= MinZ - tol AndAlso z <= MaxZ + tol
        End Function

        Public Overrides Function ToString() As String
            Return $"{Name}[({MinX:G4},{MinY:G4},{MinZ:G4})-({MaxX:G4},{MaxY:G4},{MaxZ:G4})]"
        End Function
    End Class

    ''' <summary>发酵罐几何与工艺参数（SI 单位）。</summary>
    Public Class FermentationTankGeometry

        ''' <summary>罐内径 D（m）。</summary>
        Public Property TankDiameter As Double = 0.30

        ''' <summary>罐高 H（m）。默认 H = 1.5·D。</summary>
        Public Property TankHeight As Double = 0.45

        ''' <summary>液面高度（m）。默认 2/3·H —— 等截面圆柱罐中即 2/3 液体积。</summary>
        Public Property LiquidLevel As Double = 0.30

        ''' <summary>挡板数。</summary>
        Public Property BaffleCount As Integer = 4

        ''' <summary>挡板宽度（m）。默认 D/10。</summary>
        Public Property BaffleWidth As Double = 0.03

        ''' <summary>挡板厚度（m）。</summary>
        Public Property BaffleThickness As Double = 0.005

        ''' <summary>搅拌转速（rpm）。</summary>
        Public Property Rpm As Double = 300.0

        ''' <summary>桨径（m）。默认 D/3。</summary>
        Public Property ImpellerDiameter As Double = 0.10

        ''' <summary>桨叶高度（m）。默认 Di/5。</summary>
        Public Property BladeHeight As Double = 0.02

        ''' <summary>桨叶厚度（m）。</summary>
        Public Property BladeThickness As Double = 0.004

        ''' <summary>桨叶数。</summary>
        Public Property BladeCount As Integer = 6

        ''' <summary>桨安装高度（m）。默认 Di。</summary>
        Public Property ImpellerHeight As Double = 0.10

        ''' <summary>轮毂半径（m）。默认 Di/5。</summary>
        Public Property HubRadius As Double = 0.02

        ''' <summary>轴半径（m）。</summary>
        Public Property ShaftRadius As Double = 0.005

        ''' <summary>角速度 ω（rad/s）。</summary>
        Public ReadOnly Property Omega As Double
            Get
                Return Rpm * 2.0 * std.PI / 60.0
            End Get
        End Property

        ''' <summary>转速（rev/s）。</summary>
        Public ReadOnly Property RevolutionsPerSecond As Double
            Get
                Return Rpm / 60.0
            End Get
        End Property

        ''' <summary>周向分段数（按罐径自动取值）。</summary>
        Public ReadOnly Property Segments As Integer
            Get
                Return std.Max(24, CInt(std.Min(256, TankDiameter * 400)))
            End Get
        End Property

        Public Sub New()
        End Sub

        ''' <summary>依 D 推导默认几何（H=1.5D、液位=2/3H、Di=D/3、挡板 4 块宽 D/10）。</summary>
        Public Shared Function CreateDefault(diameter As Double, Optional rpm As Double = 300.0) As FermentationTankGeometry
            Dim g As New FermentationTankGeometry()

            g.TankDiameter = diameter
            g.TankHeight = 1.5 * diameter
            g.LiquidLevel = 2.0 / 3.0 * g.TankHeight
            g.BaffleWidth = diameter / 10.0
            g.BaffleThickness = 0.005
            g.ImpellerDiameter = diameter / 3.0
            g.BladeHeight = g.ImpellerDiameter / 5.0
            g.BladeThickness = 0.004
            g.BladeCount = 6
            g.ImpellerHeight = g.ImpellerDiameter
            g.HubRadius = g.ImpellerDiameter / 5.0
            g.ShaftRadius = 0.005
            g.Rpm = rpm

            Return g
        End Function

        ''' <summary>罐中心 X（原点居中）。</summary>
        Public ReadOnly Property CenterX As Double
            Get
                Return 0.0
            End Get
        End Property

        ''' <summary>罐中心 Y（原点居中）。</summary>
        Public ReadOnly Property CenterY As Double
            Get
                Return 0.0
            End Get
        End Property

        ''' <summary>罐内半径。</summary>
        Public ReadOnly Property TankRadius As Double
            Get
                Return TankDiameter * 0.5
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"tank D={TankDiameter:F3}m H={TankHeight:F3}m, liquid={LiquidLevel:F3}m " &
                   $"({(LiquidLevel / TankHeight * 100.0):F1}% height), Di={ImpellerDiameter:F3}m, {Rpm:F0} rpm"
        End Function

    End Class

    ''' <summary>发酵罐算例的求解配置。</summary>
    Public Class FermentationTankOptions

        ''' <summary>
        ''' 网格相对分辨率（相对罐体对角线长度）。
        ''' 八叉树的单元尺寸被量化到 2 的幂，因此该值只需落在目标深度区间内：
        ''' 0.04 ≈ 32³ 总单元（约 20 单元跨越罐径，自测/快速回归用）；
        ''' 0.015 ≈ 64³ 总单元（约 40 单元跨越罐径，正式计算用）。
        ''' </summary>
        Public Property Resolution As Double = 0.04

        ''' <summary>是否做切割单元（贴体多面体）。False = 阶梯状 IBM 网格。</summary>
        Public Property CutCells As Boolean = True

        ''' <summary>是否瞬态推进（True = PIMPLE + VOF 真实液面演化）。</summary>
        Public Property Transient As Boolean = True

        ''' <summary>时间步数。</summary>
        Public Property Steps As Integer = 200

        ''' <summary>时间步内 PIMPLE 外循环次数。</summary>
        Public Property PimpleOuterIterations As Integer = 1

        ''' <summary>PISO 压力修正次数。</summary>
        Public Property PisoCorrectors As Integer = 2

        ''' <summary>目标最大 Courant 数（用于自动时间步长）。</summary>
        Public Property TargetCourant As Double = 1.0

        ''' <summary>湍流模型（默认 k-ε）。</summary>
        Public Property Turbulence As WindTunnelTurbulence = WindTunnelTurbulence.KEpsilon

        ''' <summary>VOF 表面张力系数（N/m；水-空气 ≈ 0.072）。</summary>
        Public Property SurfaceTension As Double = 0.072

        ''' <summary>虚拟桨盘驱动强度（相对 ω 的倍数；0 = 仅依赖桨叶壁面驱动）。</summary>
        Public Property DriveStrengthRatio As Double = 0.0

        ''' <summary>是否使用 GPU float32 后端。</summary>
        Public Property UseGpu As Boolean = True

        ''' <summary>快照采样间隔（步）。</summary>
        Public Property SnapshotInterval As Integer = 10

        ''' <summary>几何生成目录（STL 文件写出位置）。</summary>
        Public Property GeometryDirectory As String = ""

    End Class

#End Region

#Region "算例"

    ''' <summary>发酵罐搅拌算例：几何生成 + 网格 + MRF/VOF 求解装配。</summary>
    Public Class EulerianFermentationTankCase

        Public ReadOnly Property Geometry As FermentationTankGeometry
        Public ReadOnly Property Options As FermentationTankOptions

        ''' <summary>网格（由 <see cref="Configure"/> 或 <see cref="AttachMesh"/> 绑定）。</summary>
        Public Property Mesh As EulerianFvMesh

        Public Property Fields As EulerianFieldSet

        ''' <summary>MRF 旋转区。</summary>
        Public Property Mrf As EulerianMrfZone

        ''' <summary>VOF 两相模型。</summary>
        Public Property Vof As EulerianVofModel

        ''' <summary>求解器。</summary>
        Public Property Solver As IncompressibleSolver

        ''' <summary>本算例中随桨旋转的壁面 patch 名。</summary>
        Public ReadOnly Property RotatingPatches As New List(Of String)()

        Public Sub New(geometry As FermentationTankGeometry, options As FermentationTankOptions)
            Me.Geometry = geometry
            Me.Options = options
        End Sub

#Region "几何生成"

        ''' <summary>
        ''' 生成 4 个闭合 STL：container（容器外壳）/ baffles（挡板）/ impeller（桨叶+轮毂）/ shaft（轴）。
        ''' 返回 (container, secondarySolids())。
        ''' </summary>
        Public Function WriteGeometry(Optional targetDir As String = Nothing) As (container As String, secondary As String())
            Dim dir As String = If(String.IsNullOrEmpty(targetDir), Options.GeometryDirectory, targetDir)

            If String.IsNullOrEmpty(dir) Then
                dir = IO.Path.Combine(IO.Path.GetTempPath(), "moira_tank_geometry")
            End If

            Call IO.Directory.CreateDirectory(dir)

            Dim g = Geometry
            Dim containerPath As String = IO.Path.Combine(dir, "tank_container.stl")
            Dim bafflePath As String = IO.Path.Combine(dir, "tank_baffles.stl")
            Dim impellerPath As String = IO.Path.Combine(dir, "tank_impeller.stl")
            Dim shaftPath As String = IO.Path.Combine(dir, "tank_shaft.stl")

            ' ---- 1. 容器外壳（闭合圆柱）----
            Dim container As New AsciiStlWriter("tank_container")

            Call container.AddClosedCylinder(g.CenterX, g.CenterY, 0.0, g.TankHeight, g.TankRadius, g.Segments)
            Call container.Save(containerPath)

            ' ---- 2. 挡板（4 块贴壁薄板）----
            Dim baffles As New AsciiStlWriter("tank_baffles")
            Dim wallGap As Double = 0.0    ' 挡板与罐壁贴紧（无间隙，体素场自动合并）

            For i As Integer = 0 To g.BaffleCount - 1
                Dim theta As Double = 2.0 * std.PI * i / g.BaffleCount

                ' 挡板中心位于罐壁内侧 r = R − 厚度/2
                Dim rc As Double = g.TankRadius - g.BaffleThickness * 0.5 - wallGap
                Dim cxp As Double = g.CenterX + rc * std.Cos(theta)
                Dim cyp As Double = g.CenterY + rc * std.Sin(theta)

                ' 局部坐标（径向 = 厚度，切向 = 宽度），绕 Z 旋转 theta
                Dim radial As Double = g.BaffleThickness * 0.5
                Dim tangential As Double = g.BaffleWidth * 0.5
                Dim cs As Double = std.Cos(theta)
                Dim sn As Double = std.Sin(theta)
                Dim corners As Double()() = New Double(7)() {}

                For s As Integer = 0 To 7
                    Dim lr As Double = If((s And 1) = 0, -radial, radial)
                    Dim lt As Double = If((s And 2) = 0, -tangential, tangential)
                    Dim lz As Double = If((s And 4) = 0, 0.0, 0.9 * g.TankHeight)

                    ' 角点布局：(x最小/最大 = 径向, y = 切向, z = 底/顶)
                    corners(s) = New Double() {
                        cxp + lr * cs - lt * sn,
                        cyp + lr * sn + lt * cs,
                        lz}
                Next

                ' AddBox 的角点顺序是 (x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,z1)...
                ' 这里直接按 AddHexCorners 的期望顺序重排
                Dim ordered As Double()() = {
                    corners(0), corners(1), corners(3), corners(2),
                    corners(4), corners(5), corners(7), corners(6)}

                Call baffles.AddHexCorners(ordered)
            Next

            Call baffles.Save(bafflePath)

            ' ---- 3. 搅拌桨（轮毂 + 6 直叶）----
            Dim impeller As New AsciiStlWriter("tank_impeller")
            Dim zc As Double = g.ImpellerHeight
            Dim halfB As Double = g.BladeHeight * 0.5

            ' 轮毂（薄圆盘）
            Call impeller.AddClosedCylinder(g.CenterX, g.CenterY, zc - halfB, zc + halfB,
                                            g.HubRadius, std.Max(16, g.Segments \ 2))

            ' 6 片直叶
            For i As Integer = 0 To g.BladeCount - 1
                Dim theta As Double = 2.0 * std.PI * i / g.BladeCount
                Dim cs As Double = std.Cos(theta)
                Dim sn As Double = std.Sin(theta)

                Dim r0 As Double = g.HubRadius
                Dim r1 As Double = g.ImpellerDiameter * 0.5
                Dim halfT As Double = g.BladeThickness * 0.5

                Dim corners As Double()() = New Double(7)() {}

                For s As Integer = 0 To 7
                    Dim lr As Double = If((s And 1) = 0, r0, r1)
                    Dim lt As Double = If((s And 2) = 0, -halfT, halfT)
                    Dim lz As Double = zc + If((s And 4) = 0, -halfB, halfB)

                    corners(s) = New Double() {
                        g.CenterX + lr * cs - lt * sn,
                        g.CenterY + lr * sn + lt * cs,
                        lz}
                Next

                Dim ordered As Double()() = {
                    corners(0), corners(1), corners(3), corners(2),
                    corners(4), corners(5), corners(7), corners(6)}

                Call impeller.AddHexCorners(ordered)
            Next

            Call impeller.Save(impellerPath)

            ' ---- 4. 搅拌轴 ----
            Dim shaft As New AsciiStlWriter("tank_shaft")

            Call shaft.AddClosedCylinder(g.CenterX, g.CenterY, zc - halfB, g.TankHeight,
                                         g.ShaftRadius, std.Max(12, g.Segments \ 3))
            Call shaft.Save(shaftPath)

            Return (containerPath, New String() {bafflePath, impellerPath, shaftPath})
        End Function

#End Region

#Region "网格区域与网格装配"

        ''' <summary>
        ''' 需要从"整体壁面"中区分出来的边界区域（区域盒）。
        ''' 调用方（持有 ZMesh 的 CLI / 自测工程）据此构造 <c>PatchRule.Region</c>：
        '''     impeller —— 桨叶 + 轮毂（随 MRF 旋转）
        '''     shaft    —— 搅拌轴（随 MRF 旋转）
        '''     其余边界面   —— 罐壁 / 挡板（静止固壁）
        ''' </summary>
        Public Function PatchRegions() As List(Of EulerianRegionBox)
            Dim g = Geometry
            Dim margin As Double = 0.25 * g.ImpellerDiameter
            Dim impellerHalfZ As Double = 0.6 * g.BladeHeight + margin
            Dim rOuter As Double = g.ImpellerDiameter * 0.5 + margin

            Return New List(Of EulerianRegionBox) From {
                New EulerianRegionBox With {
                    .Name = "impeller",
                    .MinX = g.CenterX - rOuter, .MinY = g.CenterY - rOuter, .MinZ = g.ImpellerHeight - impellerHalfZ,
                    .MaxX = g.CenterX + rOuter, .MaxY = g.CenterY + rOuter, .MaxZ = g.ImpellerHeight + impellerHalfZ},
                New EulerianRegionBox With {
                    .Name = "shaft",
                    .MinX = g.CenterX - 2.0 * g.ShaftRadius, .MinY = g.CenterY - 2.0 * g.ShaftRadius,
                    .MinZ = g.ImpellerHeight,
                    .MaxX = g.CenterX + 2.0 * g.ShaftRadius, .MaxY = g.CenterY + 2.0 * g.ShaftRadius,
                    .MaxZ = g.TankHeight}}
        End Function

        ''' <summary>绑定已生成好的内流网格（由 CLI / 自测工程通过 ZMesh 流水线生成）。</summary>
        Public Sub AttachMesh(mesh As EulerianFvMesh)
            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

            Me.Mesh = mesh
        End Sub

#End Region

#Region "求解装配"

        ''' <summary>构建 MRF 区、VOF 模型与求解器（网格就绪后调用）。</summary>
        Public Sub Configure(Optional mesh As EulerianFvMesh = Nothing)
            If mesh IsNot Nothing Then
                Me.Mesh = mesh
            End If

            If Me.Mesh Is Nothing Then
                Throw New InvalidOperationException("请先调用 BuildMesh（或传入网格）")
            End If

            Dim g = Geometry
            Dim mesh0 As EulerianFvMesh = Me.Mesh

            Me.Fields = New EulerianFieldSet(mesh0)

            ' ---- MRF 旋转区（桨叶所在圆柱）----
            Dim mrfRadius As Double = g.ImpellerDiameter * 0.55
            Dim mrfHalfZ As Double = 0.6 * g.BladeHeight

            Me.Mrf = EulerianMrfZone.CreateCylinder(
                mesh0, g.CenterX, g.CenterY, mrfRadius,
                g.ImpellerHeight - mrfHalfZ, g.ImpellerHeight + mrfHalfZ,
                g.Omega, g.Omega * Options.DriveStrengthRatio)

            ' ---- 随桨旋转的壁面 patch ----
            RotatingPatches.Clear()

            For pi As Integer = 0 To mesh0.PatchCount - 1
                Dim nm As String = mesh0.Patches(pi).Name

                If String.Equals(nm, "impeller", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(nm, "shaft", StringComparison.OrdinalIgnoreCase) Then

                    RotatingPatches.Add(nm)
                End If
            Next

            ' ---- VOF ----
            Me.Vof = New EulerianVofModel(mesh0, Fields) With {
                .Rho1 = 998.0,
                .Rho2 = 1.2,
                .Nu1 = 1.0E-6,
                .Nu2 = 1.5E-5,
                .SurfaceTension = Options.SurfaceTension,
                .CompressionFactor = 1.0}

            ' 初始化 α 场：液面高度 = 罐高 2/3
            Call Vof.InitializeLevel(g.LiquidLevel, phase1Below:=True, sharp:=False)
            Call Vof.UpdateProperties(Fields)

            ' ---- 湍流 ----
            Dim turb As EulerianTurbulenceModel

            If Options.Turbulence = WindTunnelTurbulence.Laminar Then
                turb = New LaminarTurbulenceModel(mesh0)
            ElseIf Options.Turbulence = WindTunnelTurbulence.KOmegaSST Then
                turb = New KOmegaSSTModel(mesh0, EulerianWallDistance.Build(mesh0))
            Else
                turb = New KEpsilonModel(mesh0, EulerianWallDistance.Build(mesh0))
            End If

            ' ---- 求解器 ----
            Dim dt As Double = EstimateTimeStep(mesh0, g)

            Dim opts As New IncompressibleSolverOptions With {
                .Rho = 998.0,
                .Nu = 1.0E-6,
                .Transient = Options.Transient,
                .DeltaT = dt,
                .Scheme = EulerianConvectionScheme.Upwind,
                .RelaxU = 0.7,
                .RelaxP = 1.0,
                .MomentumSweeps = 2,
                .PressureMaxIterations = 150,
                .PseudoTransientCourant = 0.5,
                .PimpleOuterIterations = Options.PimpleOuterIterations,
                .PisoCorrectors = Options.PisoCorrectors,
                .UseGpu = Options.UseGpu,
                .Turbulence = turb,
                .Mrf = Me.Mrf,
                .Vof = Me.Vof,
                .Gravity = 9.81,
                .GravityDirection = New Moira.CFDEngine.Eulerian.EulerianVertex(0, 0, -1),
                .TurbulenceIntensity = 0.02,
                .TurbulenceLengthScale = 0.05 * g.ImpellerDiameter}

            opts.TurbulenceContext.Rho = 998.0
            opts.TurbulenceContext.Nu = 1.0E-6
            opts.TurbulenceContext.DeltaT = If(Options.Transient, dt, 0)

            ' 内流罐：所有 patch 都是壁面 → 用固定参考压力单元
            opts.ReferencePressureCell = EulerianDiagnostics.NearestCell(mesh0, g.TankRadius * 0.5, 0, g.TankHeight * 0.5)
            opts.ReferencePressure = 101325.0

            Me.Solver = New IncompressibleSolver(mesh0, Fields, opts)

            ' Initialize 内部完成：默认边界装配 + 物性 + 初始面通量 + 湍流量初值
            Call Solver.Initialize()
        End Sub

        ''' <summary>按叶尖速度与最小单元尺度估计时间步长。</summary>
        Private Function EstimateTimeStep(mesh As EulerianFvMesh, g As FermentationTankGeometry) As Double
            Dim hmin As Double = std.Pow(std.Max(mesh.TotalVolume, 1.0E-300) / std.Max(1, mesh.CellCount), 1.0 / 3.0)
            Dim tip As Double = EulerianDiagnostics.TipSpeed(g.Omega, g.ImpellerDiameter * 0.5)
            Dim v As Double = std.Max(tip, 0.05)

            Dim dt As Double = Options.TargetCourant * hmin / v

            ' 限制单步转角不超过 ~2°（保证 VOF 与 MRF 的界面分辨率）
            Dim maxRot As Double = std.Max(std.Abs(g.Omega), 1.0E-6)
            Dim dtRot As Double = (2.0 * std.PI / 180.0) / maxRot

            Return std.Max(std.Min(dt, dtRot), 1.0E-6)
        End Function

#End Region

#Region "推进与诊断"

        ''' <summary>推进若干时间步（瞬态）。</summary>
        Public Sub Run(steps As Integer, Optional onStep As Action(Of Integer, Double) = Nothing)
            If Solver Is Nothing Then Throw New InvalidOperationException("请先调用 Configure")

            For i As Integer = 1 To std.Max(0, steps)
                Call Solver.Iterate()

                If onStep IsNot Nothing Then
                    Dim res As Double = If(Solver.ResidualHistory.Count > 0,
                                           Solver.ResidualHistory(Solver.ResidualHistory.Count - 1), 0)

                    Call onStep(Solver.IterationCount, res)
                End If
            Next
        End Sub

        ''' <summary>搅拌功率（W）与功率数 Np。</summary>
        public Function Power() As (power As Double, np As Double)
            If Solver Is Nothing OrElse Mesh Is Nothing Then Return (0, 0)

            Dim g = Geometry

            Dim p As Double = EulerianDiagnostics.ImpellerPower(
                Mesh, Fields, Solver.NuEffFace, Solver.RhoFaceArray, 1.0E-6, RotatingPatches)

            Dim np As Double = EulerianDiagnostics.PowerNumber(p, 998.0, g.RevolutionsPerSecond, g.ImpellerDiameter)

            Return (p, np)
        End Function

        ''' <summary>本次模拟的累积转角（圈数）。</summary>
        Public ReadOnly Property Revolutions As Double
            Get
                If Solver Is Nothing Then Return 0

                Return Solver.Time * Geometry.Omega / (2.0 * std.PI)
            End Get
        End Property

        ''' <summary>诊断摘要。</summary>
        Public ReadOnly Property Report As String
            Get
                Dim sb As New StringBuilder(512)
                Dim g = Geometry

                sb.AppendLine($"fermentation tank case: {g.ToString()}")
                sb.AppendLine($"  omega = {g.Omega.ToString("F4", CultureInfo.InvariantCulture)} rad/s, " &
                              $"tip speed = {EulerianDiagnostics.TipSpeed(g.Omega, g.ImpellerDiameter * 0.5).ToString("F4", CultureInfo.InvariantCulture)} m/s")
                sb.AppendLine($"  mesh = {Mesh.ToString()}")

                If Mrf IsNot Nothing Then sb.AppendLine($"  {Mrf.Diagnostics}")

                If Vof IsNot Nothing Then
                    sb.AppendLine($"  VOF liquid fraction = {Vof.LiquidFraction().ToString("F5", CultureInfo.InvariantCulture)} " &
                                  $"(target = {(g.LiquidLevel / g.TankHeight).ToString("F5", CultureInfo.InvariantCulture)})")
                End If

                If Solver IsNot Nothing Then
                    sb.Append(Solver.Diagnostics)

                    Dim pw = Power()
                    sb.AppendLine($"  impeller power = {pw.power.ToString("G6", CultureInfo.InvariantCulture)} W, " &
                                  $"Np = {pw.np.ToString("F3", CultureInfo.InvariantCulture)}")
                    sb.AppendLine($"  revolutions = {Revolutions.ToString("F3", CultureInfo.InvariantCulture)}")
                End If

                Return sb.ToString()
            End Get
        End Property

#End Region

    End Class

#End Region

End Namespace
