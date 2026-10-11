' /********************************************************************************/
'
'   EulerianBoundaryField.vb
'
'   边界场容器 —— 场变量层中"逐 patch 的边界值 + 边界条件"的承载者。
'
'   结构（对应 docs\FVM_Eulerian.md §3.1 的 GeometricField 四段式中的
'   boundaryField 一段）：
'
'       EulerianField                     ← cell-centered internalField（cell 值）
'         └─ EulerianBoundaryField        ← 本文件：按 patch 聚合
'              └─ EulerianPatchField      ← 一个 patch 的边界值 + 边界条件对象
'
'   数值布局：与 EulerianField 一致采用"分量主序"
'       Values(component * FaceCount + i)
'   这样按分量遍历（求解 U/V/W 时逐方向）是连续内存访问。
'
'   混合精度契约：
'       internalField（EulerianField.Values）与 boundaryField 均为 double；
'       float32 只在 GPU 内迭代线性求解阶段出现。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

    ''' <summary>
    ''' 单个 patch 上的边界场：逐面边界值数组 + 关联的边界条件对象。
    ''' </summary>
    Public Class EulerianPatchField

        ''' <summary>所属的单元场。</summary>
        Public ReadOnly Property OwnerField As EulerianField

        ''' <summary>在 <see cref="EulerianFvMesh.Patches"/> 中的序号。</summary>
        Public ReadOnly Property PatchIndex As Integer

        ''' <summary>本 patch 的几何量（只读视图）。</summary>
        Public ReadOnly Property Geometry As EulerianPatchGeometry

        ''' <summary>
        ''' 边界面值，分量主序：Values(component * FaceCount + faceIndex)。
        ''' 由 <see cref="Condition"/> 负责填充。
        ''' </summary>
        Public Property Values As Double()

        ''' <summary>本 patch 上的边界条件（可为 Nothing：等价于"直接沿用内部值"）。</summary>
        Public Property Condition As EulerianBoundaryCondition

        Friend Sub New(ownerField As EulerianField, patchIndex As Integer)
            Me.OwnerField = ownerField
            Me.PatchIndex = patchIndex
            Me.Geometry = ownerField.Mesh.Patches(patchIndex)
            Me.Values = New Double(Geometry.FaceCount * std.Max(1, ownerField.Components) - 1) {}
        End Sub

        ''' <summary>Patch 名称。</summary>
        Public ReadOnly Property Name As String
            Get
                Return Geometry.Name
            End Get
        End Property

        ''' <summary>Patch 物理类型。</summary>
        Public ReadOnly Property PatchType As EulerianPatchType
            Get
                Return Geometry.PatchType
            End Get
        End Property

        ''' <summary>边界面数。</summary>
        Public ReadOnly Property FaceCount As Integer
            Get
                Return Geometry.FaceCount
            End Get
        End Property

        ''' <summary>分量数（与 OwnerField 一致）。</summary>
        Public ReadOnly Property Components As Integer
            Get
                Return std.Max(1, OwnerField.Components)
            End Get
        End Property

        ''' <summary>边界面 i 的分量 comp 的当前边界值。</summary>
        Public Function Value(faceIndex As Integer, Optional component As Integer = 0) As Double
            Return Values(component * FaceCount + faceIndex)
        End Function

        ''' <summary>写边界面 i 的分量 comp 的边界值。</summary>
        Public Sub SetValue(faceIndex As Integer, value As Double, Optional component As Integer = 0)
            Values(component * FaceCount + faceIndex) = value
        End Sub

        ''' <summary>边界面 i 的 Owner 单元编号。</summary>
        Public Function OwnerCell(faceIndex As Integer) As Integer
            Return Geometry.OwnerCells(faceIndex)
        End Function

        ''' <summary>边界面 i 的 Owner 单元内部值 φ_P。</summary>
        Public Function OwnerCellValue(faceIndex As Integer, Optional component As Integer = 0) As Double
            Return OwnerField.Value(Geometry.OwnerCells(faceIndex), component)
        End Function

        ''' <summary>本 patch 的全部边界面索引（指向底层网格面）。</summary>
        Public Function FaceIndices() As Integer()
            Return Geometry.FaceIndices
        End Function

        ''' <summary>把所有边界面值填为给定常数。</summary>
        Public Sub FillValue(value As Double)
            For i As Integer = 0 To Values.Length - 1
                Values(i) = value
            Next
        End Sub

        ''' <summary>把边界面值初始化为对应 Owner 单元的内部值（零阶外插）。</summary>
        Public Sub FillFromOwner()
            Dim n As Integer = FaceCount
            Dim c As Integer = Components

            For comp As Integer = 0 To c - 1
                For i As Integer = 0 To n - 1
                    Values(comp * n + i) = OwnerField.Value(Geometry.OwnerCells(i), comp)
                Next
            Next
        End Sub

        ''' <summary>
        ''' 刷新本 patch 的边界面值与系数缓存：
        ''' 先按零阶外插初始化（保证任何 BC 在首次读取边界值时都有合理数值），
        ''' 再交由 <see cref="Condition"/> 按物理类型修正。
        ''' </summary>
        Public Sub Evaluate()
            Call FillFromOwner()

            If Condition IsNot Nothing Then
                Condition.Update()
            End If
        End Sub

        ''' <summary>失效边界条件的系数缓存（几何或求解参数变化后调用）。</summary>
        Public Sub Invalidate()
            If Condition IsNot Nothing Then
                Condition.InvalidateCache()
            End If
        End Sub

        Public Overrides Function ToString() As String
            If Condition Is Nothing Then
                Return $"{Name}({PatchType.ToString()}, {FaceCount} faces, no condition)"
            End If

            Return $"{Name}({PatchType.ToString()}, {FaceCount} faces, {Condition.Kind.ToString()})"
        End Function

    End Class

    ''' <summary>
    ''' 一个场的全部 patch 边界场集合。
    ''' </summary>
    Public Class EulerianBoundaryField

        ''' <summary>所属单元场。</summary>
        Public ReadOnly Property Field As EulerianField

        ''' <summary>逐 patch 的边界场（顺序与 <see cref="EulerianFvMesh.Patches"/> 一致）。</summary>
        Public ReadOnly Property Items As List(Of EulerianPatchField)

        Friend Sub New(field As EulerianField)
            Me.Field = field

            Dim list As New List(Of EulerianPatchField)(field.Mesh.PatchCount)

            For p As Integer = 0 To field.Mesh.PatchCount - 1
                list.Add(New EulerianPatchField(field, p))
            Next

            Me.Items = list
        End Sub

        ''' <summary>Patch 数。</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return Items.Count
            End Get
        End Property

        ''' <summary>按 patch 序号取边界场。</summary>
        Default Public ReadOnly Property Item(patchIndex As Integer) As EulerianPatchField
            Get
                Return Items(patchIndex)
            End Get
        End Property

        ''' <summary>按 patch 名称取边界场（大小写不敏感）；未找到返回 Nothing。</summary>
        Public Function Patch(name As String) As EulerianPatchField
            Dim i As Integer = Field.Mesh.FindPatch(name)

            If i < 0 Then
                Return Nothing
            End If

            Return Items(i)
        End Function

        ''' <summary>取指定物理类型的全部边界场。</summary>
        Public Function OfType(kind As EulerianPatchType) As List(Of EulerianPatchField)
            Dim r As New List(Of EulerianPatchField)

            For Each pf As EulerianPatchField In Items
                If pf.PatchType = kind Then
                    r.Add(pf)
                End If
            Next

            Return r
        End Function

        ''' <summary>为指定 patch 绑定边界条件（返回该边界条件对象）。</summary>
        Public Function Bind(patchIndex As Integer, condition As EulerianBoundaryCondition) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = Items(patchIndex)
            pf.Condition = condition
            Call pf.Evaluate()
            Return condition
        End Function

        ''' <summary>为指定 patch 名称绑定边界条件（返回该边界条件对象）。</summary>
        Public Function Bind(patchName As String, condition As EulerianBoundaryCondition) As EulerianBoundaryCondition
            Dim i As Integer = Field.Mesh.FindPatch(patchName)

            If i < 0 Then
                Throw New KeyNotFoundException($"网格中不存在 patch '{patchName}'；现有 patch：{Field.Mesh.PatchNames()}")
            End If

            Return Bind(i, condition)
        End Function

        ''' <summary>
        ''' 全量刷新：按 patch 顺序更新边界面值与边界条件系数缓存。
        ''' 每次外迭代（内部场更新后）必须调用一次。
        ''' </summary>
        Public Sub UpdateAll()
            For Each pf As EulerianPatchField In Items
                Call pf.Evaluate()
            Next
        End Sub

        ''' <summary>失效全部 patch 的系数缓存。</summary>
        Public Sub InvalidateAll()
            For Each pf As EulerianPatchField In Items
                Call pf.Invalidate()
            Next
        End Sub

        ''' <summary>边界条件摘要（多行）。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(256)

                sb.AppendLine($"boundary field of '{Field.Name}' ({Field.Components} comp):")

                For Each pf As EulerianPatchField In Items
                    sb.AppendLine($"  {pf.ToString()}")
                Next

                Return sb.ToString()
            End Get
        End Property

    End Class

End Namespace
