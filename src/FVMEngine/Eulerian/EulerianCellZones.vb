' /********************************************************************************/
'
'   EulerianCellZones.vb
'
'   单元区域（cell zone）标记 —— 非结构 Eulerian 网格上的"区域"概念。
'
'   背景：
'       CFDEngine.Eulerian 的网格模型只有"面 → patch"（边界面分类）这一个分区机制，
'       没有 OpenFOAM 的 cellZone。而两项目标算例都强依赖单元区域：
'
'         · 发酵罐：MRF 旋转参考系区（搅拌桨所在圆柱区，施加科氏/离心源项）、
'                   液相区（VOF 初始 α 场：液面高度 = 罐高 2/3）；
'         · 风洞：源区（可选）、加密区的后处理统计区、探针区。
'
'   做法：
'       以"几何判据（盒 / 圆柱）→ 区域类型"的规则表在单元心上做标记，
'       产出一个紧凑的 <see cref="EulerianCellZones"/>：
'         · KindOfCell(cell)：每个单元的区域类型（默认 Fluid）；
'         · 各区域类型的单元索引清单（供源项、初始化、诊断按类型批量遍历）。
'
'   约定：
'       规则按声明顺序应用，后声明的规则覆盖先声明的（便于"先整体、后局部"）。
'       单元心取自 <see cref="EulerianFvMesh.CellCentre"/>（double）。
'
'   混合精度契约：本文件全部为 double（仅一次性几何标记，非热点）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports std = System.Math

Namespace Eulerian

    ''' <summary>单元区域类型。</summary>
    Public Enum EulerianCellZoneKind

        ''' <summary>普通流体区（默认）。</summary>
        Fluid = 0

        ''' <summary>MRF 旋转参考系区（搅拌桨 / 旋转机械区）。</summary>
        Mrf = 1

        ''' <summary>初始液相区（VOF 初始化用；只影响初场，不影响方程）。</summary>
        Liquid = 2

        ''' <summary>被排除区（不参与求解，例如内流域的实体内部或域外格点）。</summary>
        Excluded = 3
    End Enum

    ''' <summary>区域判据的几何形状。</summary>
    Public Enum EulerianZoneShape

        ''' <summary>轴对齐长方体。</summary>
        Box = 0

        ''' <summary>轴沿 Z 的圆柱（用于搅拌桨 / 旋转区）。</summary>
        CylinderZ = 1
    End Enum

    ''' <summary>
    ''' 一条"几何判据 → 单元区域类型"的规则。
    ''' </summary>
    Public Class EulerianCellZoneSpec

        ''' <summary>区域名称（用于日志与诊断）。</summary>
        Public Property Name As String = ""

        ''' <summary>区域类型。</summary>
        Public Property Kind As EulerianCellZoneKind = EulerianCellZoneKind.Fluid

        ''' <summary>判据几何形状。</summary>
        Public Property Shape As EulerianZoneShape = EulerianZoneShape.Box

        ' ---- Box ----

        Public Property MinX As Double = 0
        Public Property MinY As Double = 0
        Public Property MinZ As Double = 0
        Public Property MaxX As Double = 0
        Public Property MaxY As Double = 0
        Public Property MaxZ As Double = 0

        ' ---- CylinderZ（轴沿 Z，径向半径在 XY 平面）----

        Public Property CenterX As Double = 0
        Public Property CenterY As Double = 0
        Public Property Radius As Double = 0
        Public Property ZMin As Double = Double.NegativeInfinity
        Public Property ZMax As Double = Double.PositiveInfinity

        ''' <summary>判断单元心是否落在判据几何内部。</summary>
        Public Function Contains(x As Double, y As Double, z As Double) As Boolean
            If Shape = EulerianZoneShape.Box Then
                Return x >= MinX AndAlso x <= MaxX AndAlso y >= MinY AndAlso y <= MaxY AndAlso z >= MinZ AndAlso z <= MaxZ
            End If

            If z < ZMin OrElse z > ZMax Then
                Return False
            End If

            Dim dx As Double = x - CenterX
            Dim dy As Double = y - CenterY

            Return (dx * dx + dy * dy) <= (Radius * Radius)
        End Function

        ''' <summary>构造"液面以下（z ≤ level）"的液相区规则。</summary>
        Public Shared Function LiquidBelow(level As Double, Optional name As String = "liquid") As EulerianCellZoneSpec
            Return New EulerianCellZoneSpec With {
                .Name = name,
                .Kind = EulerianCellZoneKind.Liquid,
                .Shape = EulerianZoneShape.Box,
                .MinX = Double.NegativeInfinity,
                .MinY = Double.NegativeInfinity,
                .MinZ = Double.NegativeInfinity,
                .MaxX = Double.PositiveInfinity,
                .MaxY = Double.PositiveInfinity,
                .MaxZ = level}
        End Function

        ''' <summary>构造 Z 轴向圆柱区规则（搅拌桨 / MRF 区）。</summary>
        Public Shared Function Cylinder(centerX As Double, centerY As Double, radius As Double,
                                        zMin As Double, zMax As Double,
                                        Optional name As String = "cylinder",
                                        Optional kind As EulerianCellZoneKind = EulerianCellZoneKind.Mrf) As EulerianCellZoneSpec

            Return New EulerianCellZoneSpec With {
                .Name = name,
                .Kind = kind,
                .Shape = EulerianZoneShape.CylinderZ,
                .CenterX = centerX,
                .CenterY = centerY,
                .Radius = radius,
                .ZMin = zMin,
                .ZMax = zMax}
        End Function

        ''' <summary>构造轴对齐盒区规则。</summary>
        Public Shared Function Box(minX As Double, minY As Double, minZ As Double,
                                   maxX As Double, maxY As Double, maxZ As Double,
                                   Optional name As String = "box",
                                   Optional kind As EulerianCellZoneKind = EulerianCellZoneKind.Fluid) As EulerianCellZoneSpec

            Return New EulerianCellZoneSpec With {
                .Name = name,
                .Kind = kind,
                .Shape = EulerianZoneShape.Box,
                .MinX = minX,
                .MinY = minY,
                .MinZ = minZ,
                .MaxX = maxX,
                .MaxY = maxY,
                .MaxZ = maxZ}
        End Function

        Public Overrides Function ToString() As String
            If Shape = EulerianZoneShape.Box Then
                Return $"{Name}[{Kind.ToString()} box ({MinX:G4},{MinY:G4},{MinZ:G4})-({MaxX:G4},{MaxY:G4},{MaxZ:G4})]"
            End If

            Return $"{Name}[{Kind.ToString()} cylZ c=({CenterX:G4},{CenterY:G4}) r={Radius:G4} z=[{ZMin:G4},{ZMax:G4}]]"
        End Function
    End Class

    ''' <summary>一个已标记的区域：名称 + 类型 + 单元索引清单。</summary>
    Public Class EulerianCellZone

        Public Property Name As String = ""
        Public Property Kind As EulerianCellZoneKind = EulerianCellZoneKind.Fluid
        Public Property Cells As Integer() = New Integer(-1) {}

        Public ReadOnly Property CellCount As Integer
            Get
                Return Cells.Length
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name}({Kind.ToString()}, {CellCount} cells)"
        End Function
    End Class

    ''' <summary>
    ''' 单元区域标记结果：单元 → 区域类型 / 区域编号，以及按类型聚合的单元清单。
    ''' </summary>
    Public Class EulerianCellZones

        ''' <summary>所属网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>单元数。</summary>
        Public ReadOnly Property CellCount As Integer

        ''' <summary>单元 → 区域类型（默认 <see cref="EulerianCellZoneKind.Fluid"/>）。</summary>
        Public ReadOnly Property KindOfCell As EulerianCellZoneKind()

        ''' <summary>单元 → 规则表索引（−1 = 未被任何规则命中）。</summary>
        Public ReadOnly Property SpecOfCell As Integer()

        ''' <summary>已标记的规范区域集合（与规则表一一对应，只含命中单元数 &gt; 0 的规则）。</summary>
        Public ReadOnly Property Zones As List(Of EulerianCellZone)

        Private ReadOnly _byKind As Dictionary(Of EulerianCellZoneKind, Integer())

        Private Sub New(mesh As EulerianFvMesh,
                        kindOfCell As EulerianCellZoneKind(),
                        specOfCell As Integer(),
                        zones As List(Of EulerianCellZone),
                        byKind As Dictionary(Of EulerianCellZoneKind, Integer()))

            Me.Mesh = mesh
            Me.CellCount = kindOfCell.Length
            Me.KindOfCell = kindOfCell
            Me.SpecOfCell = specOfCell
            Me.Zones = zones
            _byKind = byKind
        End Sub

        ''' <summary>
        ''' 按规则表标记单元：规则按声明顺序应用，后声明者覆盖先声明者。
        ''' </summary>
        Public Shared Function Build(mesh As EulerianFvMesh,
                                     specs As IEnumerable(Of EulerianCellZoneSpec)) As EulerianCellZones

            If mesh Is Nothing Then
                Throw New ArgumentNullException(NameOf(mesh))
            End If

            Dim n As Integer = mesh.CellCount
            Dim kindOfCell(n - 1) As EulerianCellZoneKind
            Dim specOfCell = New Integer(n - 1) {}

            For c As Integer = 0 To n - 1
                specOfCell(c) = -1
            Next

            Dim list As New List(Of EulerianCellZoneSpec)

            If specs IsNot Nothing Then
                list.AddRange(specs)
            End If

            Dim buckets As New List(Of List(Of Integer))

            For s As Integer = 0 To list.Count - 1
                buckets.Add(New List(Of Integer))

                Dim spec As EulerianCellZoneSpec = list(s)

                For c As Integer = 0 To n - 1
                    Dim p As Moira.CFDEngine.Eulerian.EulerianVertex = mesh.CellCentre(c)

                    If spec.Contains(p.X, p.Y, p.Z) Then
                        kindOfCell(c) = spec.Kind
                        specOfCell(c) = s
                        buckets(s).Add(c)
                    End If
                Next
            Next

            Dim zones As New List(Of EulerianCellZone)

            For s As Integer = 0 To list.Count - 1
                zones.Add(New EulerianCellZone With {
                    .Name = list(s).Name,
                    .Kind = list(s).Kind,
                    .Cells = buckets(s).ToArray})
            Next

            ' 按类型聚合
            Dim kindBuckets As New Dictionary(Of EulerianCellZoneKind, List(Of Integer))

            For c As Integer = 0 To n - 1
                Dim k As EulerianCellZoneKind = kindOfCell(c)

                If Not kindBuckets.ContainsKey(k) Then
                    kindBuckets(k) = New List(Of Integer)
                End If

                kindBuckets(k).Add(c)
            Next

            Dim byKind As New Dictionary(Of EulerianCellZoneKind, Integer())

            For Each kv As KeyValuePair(Of EulerianCellZoneKind, List(Of Integer)) In kindBuckets
                byKind(kv.Key) = kv.Value.ToArray
            Next

            Return New EulerianCellZones(mesh, kindOfCell, specOfCell, zones, byKind)
        End Function

        ''' <summary>
        ''' 创建一个空标记（全部单元为 Fluid）。
        ''' </summary>
        Public Shared Function Empty(mesh As EulerianFvMesh) As EulerianCellZones
            Return Build(mesh, Nothing)
        End Function

        ''' <summary>取某一区域类型的全部单元索引（无该类型时返回空数组）。</summary>
        Public Function CellsOfKind(kind As EulerianCellZoneKind) As Integer()
            Dim r As Integer() = Nothing

            If _byKind.TryGetValue(kind, r) Then
                Return r
            End If

            Return New Integer(-1) {}
        End Function

        ''' <summary>MRF 旋转区单元。</summary>
        Public ReadOnly Property MrfCells As Integer()
            Get
                Return CellsOfKind(EulerianCellZoneKind.Mrf)
            End Get
        End Property

        ''' <summary>初始液相区单元。</summary>
        Public ReadOnly Property LiquidCells As Integer()
            Get
                Return CellsOfKind(EulerianCellZoneKind.Liquid)
            End Get
        End Property

        ''' <summary>被排除区单元。</summary>
        Public ReadOnly Property ExcludedCells As Integer()
            Get
                Return CellsOfKind(EulerianCellZoneKind.Excluded)
            End Get
        End Property

        ''' <summary>某单元是否为 MRF 旋转区。</summary>
        Public Function IsMrf(cell As Integer) As Boolean
            Return KindOfCell(cell) = EulerianCellZoneKind.Mrf
        End Function

        ''' <summary>按名称查找规范区域；未找到返回 Nothing。</summary>
        Public Function FindZone(name As String) As EulerianCellZone
            If String.IsNullOrEmpty(name) Then
                Return Nothing
            End If

            For Each z As EulerianCellZone In Zones
                If String.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase) Then
                    Return z
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>区域标记摘要（多行）。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(256)

                sb.AppendLine($"cell zones on {CellCount} cells:")

                For Each z As EulerianCellZone In Zones
                    sb.AppendLine($"  {z.ToString()}")
                Next

                sb.AppendLine($"  totals: fluid={CellsOfKind(EulerianCellZoneKind.Fluid).Length}, " &
                              $"mrf={MrfCells.Length}, liquid={LiquidCells.Length}, excluded={ExcludedCells.Length}")

                Return sb.ToString()
            End Get
        End Property

        ''' <summary>液相体积占比（相对网格总体积）。</summary>
        Public Function LiquidVolumeFraction() As Double
            If Mesh.TotalVolume <= 0 Then
                Return 0
            End If

            Dim v As Double = 0

            For Each c As Integer In LiquidCells
                v += Mesh.CellVolume(c)
            Next

            Return v / Mesh.TotalVolume
        End Function

        Public Overrides Function ToString() As String
            Return $"EulerianCellZones(cells={CellCount}, mrf={MrfCells.Length}, liquid={LiquidCells.Length}, " &
                   $"liquidFraction={LiquidVolumeFraction().ToString("F4", CultureInfo.InvariantCulture)})"
        End Function
    End Class

End Namespace
