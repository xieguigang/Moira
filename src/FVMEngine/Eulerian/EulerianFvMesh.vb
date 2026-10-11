' /********************************************************************************/
'
'   EulerianFvMesh.vb
'
'   FVM 网格层 —— 把 CFDEngine.Eulerian 的几何网格包装为可直接支撑
'   有限体积离散的"面基"网格（OpenFOAM fvMesh 的等价物）。
'
'   作用：
'       在 Moira.CFDEngine.Eulerian.EulerianMesh（Points / Faces(Owner,Neighbour) /
'       CellFaces / Patches + 预计算的面法向·面积·面心·单元体积·单元形心）之上，
'       补齐 FVM 离散必需的派生量：
'
'         1. 内部面索引表（Owner / Neighbour 单元对）与"面 → patch"归属映射；
'         2. 面插值权重 w_f：φ_f = w_f·φ_P + (1-w_f)·φ_N（docs\FVM_Eulerian.md §2.2）；
'         3. 正交扩散系数 Δ_f = |S_f|² / (S_f · d)，
'            非正交修正向量 k_f = S_f − Δ_f·d（网格正交时 k_f ≡ 0），
'            对应 docs\FVM_Eulerian.md §4.2 的"正交隐式 + 非正交显式"拆分；
'         4. 单元–单元 CSR 邻接（对角元固定为行首），供 CPU SIMD 与 GPU 稀疏
'            迭代共用：结构一次构建、每轮只重填 values；
'         5. 网格质量诊断（体积下限校验、最大非正交角、单元体积统计），
'            避免散度定理体积退化（接近 0）导致的静默错值。
'
'   混合精度契约（本工程全局）：
'       本文件全部为 double —— 网格几何属于"非热点"，不进 GPU。
'       热点（CSR SpMV / Jacobi / GS / axpy / dot）在 EulerianFvmCompute 中以
'       float32 执行。
'
'   面法向约定（继承自 EulerianMesh.ComputeGeometry）：
'       内部面 n̂ 恒由 Owner 指向 Neighbour；边界面 n̂ 由 Owner 指向单元外部。
'       因此 S_f = n̂·A 在 Owner 单元的面通量里天然是"出流正"。
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

    ''' <summary>
    ''' 边界面片（Patch）的 FVM 预计算几何量：供边界条件实现与壁面函数共享。
    ''' </summary>
    Public Class EulerianPatchGeometry

        ''' <summary>Patch 名称（与 <see cref="EulerianPatch.Name"/> 一致）。</summary>
        Public ReadOnly Property Name As String

        ''' <summary>Patch 物理类型。</summary>
        Public ReadOnly Property PatchType As EulerianPatchType

        ''' <summary>本 Patch 的边界面索引（指向 <see cref="EulerianMesh.Faces"/>）。</summary>
        Public ReadOnly Property FaceIndices As Integer()

        ''' <summary>与 <see cref="FaceIndices"/> 平行的 Owner 单元索引。</summary>
        Public ReadOnly Property OwnerCells As Integer()

        ''' <summary>面面积 |S_f|。</summary>
        Public ReadOnly Property MagSf As Double()

        ''' <summary>面面积向量 S_f（自 Owner 指向单元外部）。</summary>
        Public ReadOnly Property Sf As EulerianVertex()

        ''' <summary>面心 x_f。</summary>
        Public ReadOnly Property Cf As EulerianVertex()

        ''' <summary>d = x_f − x_P（单元心指向面心）。</summary>
        Public ReadOnly Property Delta As EulerianVertex()

        ''' <summary>S_f · d（正交分解的分母）。</summary>
        Public ReadOnly Property SfDotDelta As Double()

        ''' <summary>正交扩散系数 Δ_f = |S_f|²/(S_f·d)。</summary>
        Public ReadOnly Property DeltaCoeff As Double()

        ''' <summary>非正交修正向量 k_f = S_f − Δ_f·d（与 <see cref="Sf"/> 同量纲）。</summary>
        Public ReadOnly Property NonOrthCorrection As EulerianVertex()

        Friend Sub New(name As String,
                       patchType As EulerianPatchType,
                       faceIndices As Integer(),
                       ownerCells As Integer(),
                       magSf As Double(),
                       sf As EulerianVertex(),
                       cf As EulerianVertex(),
                       delta As EulerianVertex(),
                       sfDotDelta As Double(),
                       deltaCoeff As Double(),
                       nonOrth As EulerianVertex())

            Me.Name = name
            Me.PatchType = patchType
            Me.FaceIndices = faceIndices
            Me.OwnerCells = ownerCells
            Me.MagSf = magSf
            Me.Sf = sf
            Me.Cf = cf
            Me.Delta = delta
            Me.SfDotDelta = sfDotDelta
            Me.DeltaCoeff = deltaCoeff
            Me.NonOrthCorrection = nonOrth
        End Sub

        ''' <summary>本 Patch 的边界面数。</summary>
        Public ReadOnly Property FaceCount As Integer
            Get
                Return FaceIndices.Length
            End Get
        End Property

        ''' <summary>总面积。</summary>
        Public ReadOnly Property TotalArea As Double
            Get
                Dim a As Double = 0

                For i As Integer = 0 To MagSf.Length - 1
                    a += MagSf(i)
                Next

                Return a
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name}({PatchType.ToString}, {FaceCount} faces, A={TotalArea.ToString("G6", CultureInfo.InvariantCulture)})"
        End Function
    End Class

    ''' <summary>
    ''' FVM 网格：<see cref="EulerianMesh"/> 的有限体积视图。
    '''
    ''' 不修改底层 <see cref="EulerianMesh"/> 的任何数据，只在其外侧建立索引与派生几何量。
    ''' </summary>
    Public Class EulerianFvMesh

#Region "构造"

        ''' <summary>底层几何网格。</summary>
        Public ReadOnly Property Mesh As EulerianMesh

        ''' <summary>
        ''' 构造 FVM 网格视图。若底层网格尚未调用 <see cref="EulerianMesh.ComputeGeometry"/>
        ''' 会自动补算。
        ''' </summary>
        ''' <param name="mesh">几何网格（非 Nothing）</param>
        ''' <param name="patchTypeOverride">可选的 patch 名称 → 类型覆盖表（大小写不敏感）。</param>
        Public Sub New(mesh As EulerianMesh, Optional patchTypeOverride As IDictionary(Of String, EulerianPatchType) = Nothing)
            If mesh Is Nothing Then
                Throw New ArgumentNullException(NameOf(mesh))
            End If

            Me.Mesh = mesh

            If mesh.Faces.Count = 0 OrElse mesh.CellFaces.Count = 0 Then
                Throw New InvalidDataException("Eulerian 网格为空（无面或无单元）")
            End If

            If mesh.FaceAreas Is Nothing OrElse mesh.CellVolumes Is Nothing OrElse
               mesh.FaceAreas.Length <> mesh.Faces.Count OrElse mesh.CellVolumes.Length <> mesh.CellFaces.Count Then

                Call mesh.ComputeGeometry()
            End If

            _faceCount = mesh.Faces.Count
            _cellCount = mesh.CellFaces.Count

            Call BuildFaceGeometry()
            Call BuildInternalFaces()
            Call BuildPatchGeometry(patchTypeOverride)
            Call BuildInterpolationAndDiffusion()
            Call BuildCsrAdjacency()
            Call ComputeDiagnostics()
        End Sub

#End Region

#Region "基础尺寸"

        Private ReadOnly _cellCount As Integer
        Private ReadOnly _faceCount As Integer

        ''' <summary>单元数。</summary>
        Public ReadOnly Property CellCount As Integer
            Get
                Return _cellCount
            End Get
        End Property

        ''' <summary>面数（含边界面）。</summary>
        Public ReadOnly Property FaceCount As Integer
            Get
                Return _faceCount
            End Get
        End Property

#End Region

#Region "全局面几何（与 Mesh.Faces 平行）"

        ''' <summary>面面积向量 S_f（自 Owner 指向 Neighbour / 外部）。</summary>
        Public ReadOnly Property Sf As EulerianVertex()
            Get
                Return _sf
            End Get
        End Property

        ''' <summary>面面积 |S_f|。</summary>
        Public ReadOnly Property MagSf As Double()
            Get
                Return _magSf
            End Get
        End Property

        ''' <summary>面单位法向 n̂。</summary>
        Public ReadOnly Property Nf As EulerianVertex()
            Get
                Return _nf
            End Get
        End Property

        ''' <summary>面心 x_f。</summary>
        Public ReadOnly Property Cf As EulerianVertex()
            Get
                Return _cf
            End Get
        End Property

        ''' <summary>单元心 x_P。</summary>
        Public ReadOnly Property CellCentre As EulerianVertex()
            Get
                Return _cellCentre
            End Get
        End Property

        ''' <summary>单元体积 V_P。</summary>
        Public ReadOnly Property CellVolume As Double()
            Get
                Return _cellVolume
            End Get
        End Property

        Private Sub BuildFaceGeometry()
            Dim sf(_faceCount - 1) As EulerianVertex
            Dim mag(_faceCount - 1) As Double
            Dim nf(_faceCount - 1) As EulerianVertex
            Dim cf(_faceCount - 1) As EulerianVertex

            For f As Integer = 0 To _faceCount - 1
                Dim n As EulerianVertex = Me.Mesh.FaceNormals(f)
                Dim a As Double = Me.Mesh.FaceAreas(f)

                mag(f) = a
                nf(f) = n
                sf(f) = New EulerianVertex(n.X * a, n.Y * a, n.Z * a)
                cf(f) = Me.Mesh.FaceCentroids(f)
            Next

            Dim cc(_cellCount - 1) As EulerianVertex

            For c As Integer = 0 To _cellCount - 1
                cc(c) = Me.Mesh.CellCentroids(c)
            Next

            _sf = sf
            _magSf = mag
            _nf = nf
            _cf = cf
            _cellCentre = cc
            _cellVolume = Me.Mesh.CellVolumes
        End Sub

        Private _sf As EulerianVertex()
        Private _magSf As Double()
        Private _nf As EulerianVertex()
        Private _cf As EulerianVertex()
        Private _cellCentre As EulerianVertex()
        Private _cellVolume As Double()

#End Region

#Region "内部面与面→Patch 映射"

        Private _internalFaces As Integer()
        Private _internalOwner As Integer()
        Private _internalNeighbour As Integer()
        Private _facePatch As Integer()
        Private _faceIsBoundary As Boolean()

        ''' <summary>内部面索引（Neighbour ≥ 0 且 ≠ Owner），升序。</summary>
        Public ReadOnly Property InternalFaces As Integer()
            Get
                Return _internalFaces
            End Get
        End Property

        ''' <summary>内部面数。</summary>
        Public ReadOnly Property InternalFaceCount As Integer
            Get
                Return _internalFaces.Length
            End Get
        End Property

        ''' <summary>与 <see cref="InternalFaces"/> 平行的 Owner 单元。</summary>
        Public ReadOnly Property InternalOwner As Integer()
            Get
                Return _internalOwner
            End Get
        End Property

        ''' <summary>与 <see cref="InternalFaces"/> 平行的 Neighbour 单元。</summary>
        Public ReadOnly Property InternalNeighbour As Integer()
            Get
                Return _internalNeighbour
            End Get
        End Property

        ''' <summary>面 → Patch 序号（−1 = 内部面）。</summary>
        Public ReadOnly Property FacePatch As Integer()
            Get
                Return _facePatch
            End Get
        End Property

        ''' <summary>面序号 → 是否边界面。</summary>
        Public ReadOnly Property FaceIsBoundary As Boolean()
            Get
                Return _faceIsBoundary
            End Get
        End Property

        Private Sub BuildInternalFaces()
            Dim faces As New List(Of Integer)
            Dim owners As New List(Of Integer)
            Dim neighbours As New List(Of Integer)

            _faceIsBoundary = New Boolean(_faceCount - 1) {}
            _facePatch = New Integer(_faceCount - 1) {}

            For f As Integer = 0 To _faceCount - 1
                Dim face As EulerianFace = Me.Mesh.Faces(f)
                Dim nb As Integer = face.Neighbour

                If nb >= 0 AndAlso nb < _cellCount AndAlso nb <> face.Owner Then
                    faces.Add(f)
                    owners.Add(face.Owner)
                    neighbours.Add(nb)
                    _faceIsBoundary(f) = False
                Else
                    ' Neighbour 越界或自环：按边界面处理（Owner 侧独占）
                    _faceIsBoundary(f) = True
                End If

                _facePatch(f) = -1
            Next

            _internalFaces = faces.ToArray
            _internalOwner = owners.ToArray
            _internalNeighbour = neighbours.ToArray
        End Sub

#End Region

#Region "Patch 几何"

        Private _patchGeometries As List(Of EulerianPatchGeometry)
        Private _unassignedBoundaryFaceCount As Integer

        ''' <summary>Patch 几何表（与底层 <see cref="EulerianMesh.Patches"/> 顺序一致；
        ''' 若存在未归属任何 Patch 的边界面，末尾追加合成 patch <c>defaultFaces</c>）。</summary>
        Public ReadOnly Property Patches As List(Of EulerianPatchGeometry)
            Get
                Return _patchGeometries
            End Get
        End Property

        ''' <summary>Patch 数。</summary>
        Public ReadOnly Property PatchCount As Integer
            Get
                Return _patchGeometries.Count
            End Get
        End Property

        ''' <summary>未被任何 Patch 声明归属的边界面数量（已归入合成 patch <c>defaultFaces</c>）。</summary>
        Public ReadOnly Property UnassignedBoundaryFaceCount As Integer
            Get
                Return _unassignedBoundaryFaceCount
            End Get
        End Property

        Private Sub BuildPatchGeometry(override_ As IDictionary(Of String, EulerianPatchType))
            _patchGeometries = New List(Of EulerianPatchGeometry)
            _unassignedBoundaryFaceCount = 0

            For p As Integer = 0 To Me.Mesh.Patches.Count - 1
                Dim src As EulerianPatch = Me.Mesh.Patches(p)
                Dim type_ As EulerianPatchType = src.Type

                If override_ IsNot Nothing AndAlso Not String.IsNullOrEmpty(src.Name) Then
                    Dim found As EulerianPatchType

                    If TryLookupPatchType(override_, src.Name, found) Then
                        type_ = found
                    End If
                End If

                Dim geo As EulerianPatchGeometry = MakePatchGeometry(src.Name, type_, src.FaceIndices)

                For i As Integer = 0 To geo.FaceIndices.Length - 1
                    _facePatch(geo.FaceIndices(i)) = _patchGeometries.Count
                Next

                _patchGeometries.Add(geo)
            Next

            ' 收集未归属的边界面
            Dim leftover As New List(Of Integer)

            For f As Integer = 0 To _faceCount - 1
                If _faceIsBoundary(f) AndAlso _facePatch(f) < 0 Then
                    leftover.Add(f)
                End If
            Next

            _unassignedBoundaryFaceCount = leftover.Count

            If leftover.Count > 0 Then
                Dim geo As EulerianPatchGeometry = MakePatchGeometry("defaultFaces", EulerianPatchType.Wall, leftover)

                For i As Integer = 0 To geo.FaceIndices.Length - 1
                    _facePatch(geo.FaceIndices(i)) = _patchGeometries.Count
                Next

                _patchGeometries.Add(geo)
            End If

            ' 一致性校验：所有边界面都必须归属某个 patch
            For f As Integer = 0 To _faceCount - 1
                If _faceIsBoundary(f) AndAlso _facePatch(f) < 0 Then
                    Throw New InvalidDataException($"边界面 #{f} 未能归属任何 patch（内部一致性错误）")
                End If
            Next
        End Sub

        Private Shared Function TryLookupPatchType(table As IDictionary(Of String, EulerianPatchType),
                                                  name As String,
                                                  ByRef result As EulerianPatchType) As Boolean

            If table.ContainsKey(name) Then
                result = table(name)
                Return True
            End If

            For Each kv As KeyValuePair(Of String, EulerianPatchType) In table
                If String.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) Then
                    result = kv.Value
                    Return True
                End If
            Next

            Return False
        End Function

        Private Function MakePatchGeometry(name As String,
                                           type_ As EulerianPatchType,
                                           faceIndices As IList(Of Integer)) As EulerianPatchGeometry

            Dim n As Integer = faceIndices.Count
            Dim fi(n - 1) As Integer
            Dim owner(n - 1) As Integer
            Dim mag(n - 1) As Double
            Dim sf(n - 1) As EulerianVertex
            Dim cf(n - 1) As EulerianVertex
            Dim delta(n - 1) As EulerianVertex
            Dim sfd(n - 1) As Double
            Dim dcoef(n - 1) As Double
            Dim korr(n - 1) As EulerianVertex

            For i As Integer = 0 To n - 1
                Dim f As Integer = faceIndices(i)
                Dim P As Integer = Me.Mesh.Faces(f).Owner

                fi(i) = f
                owner(i) = P
                mag(i) = _magSf(f)
                sf(i) = _sf(f)
                cf(i) = _cf(f)

                Dim d As EulerianVertex = _cf(f) - _cellCentre(P)

                delta(i) = d
                sfd(i) = EulerianVertex.Dot(_sf(f), d)
                dcoef(i) = OrthogonalDeltaCoeff(_magSf(f), sfd(i), d.Length)
                korr(i) = New EulerianVertex(
                    _sf(f).X - dcoef(i) * d.X,
                    _sf(f).Y - dcoef(i) * d.Y,
                    _sf(f).Z - dcoef(i) * d.Z)
            Next

            Return New EulerianPatchGeometry(name, type_, fi, owner, mag, sf, cf, delta, sfd, dcoef, korr)
        End Function

        ''' <summary>
        ''' 正交扩散系数 Δ_f = |S_f|²/(S_f·d)；
        ''' 当 (S_f·d) 非正（退化 / 强非正交）时退化为 |S_f|/|d| 以提高稳健性。
        ''' </summary>
        Private Shared Function OrthogonalDeltaCoeff(magSf As Double, sfDotDelta As Double, magDelta As Double) As Double
            If magSf <= 0 Then
                Return 0
            End If

            Dim denom As Double = sfDotDelta
            Dim guard As Double = 1.0E-12 * magSf * std.Max(magDelta, 1.0E-30)

            If denom > guard Then
                Return (magSf * magSf) / denom
            End If

            If magDelta > 1.0E-30 Then
                Return magSf / magDelta
            End If

            Return 0
        End Function

        ''' <summary>按名称查找 Patch（大小写不敏感）；未找到返回 −1。</summary>
        Public Function FindPatch(name As String) As Integer
            If String.IsNullOrEmpty(name) Then
                Return -1
            End If

            For i As Integer = 0 To _patchGeometries.Count - 1
                If String.Equals(_patchGeometries(i).Name, name, StringComparison.OrdinalIgnoreCase) Then
                    Return i
                End If
            Next

            Return -1
        End Function

        ''' <summary>按名称取 Patch 几何（未找到抛 <see cref="KeyNotFoundException"/>）。</summary>
        Public Function PatchGeometry(name As String) As EulerianPatchGeometry
            Dim i As Integer = FindPatch(name)

            If i < 0 Then
                Throw New KeyNotFoundException($"网格中不存在名为 '{name}' 的 patch；现有 patch：" & PatchNames())
            End If

            Return _patchGeometries(i)
        End Function

        ''' <summary>全部 patch 名称（逗号分隔）。</summary>
        Public Function PatchNames() As String
            Dim parts As New List(Of String)

            For Each g As EulerianPatchGeometry In _patchGeometries
                parts.Add(g.Name)
            Next

            Return String.Join(", ", parts)
        End Function

#End Region

#Region "面插值权重与非正交修正（内部面）"

        Private _interpWeight As Double()
        Private _delta As EulerianVertex()
        Private _deltaCoeff As Double()
        Private _nonOrthCorrection As EulerianVertex()
        Private _orthogonalityAngle As Double()

        ''' <summary>内部面插值权重 w_f（φ_f = w_f·φ_P + (1−w_f)·φ_N），已钳制在 [0,1]。</summary>
        Public ReadOnly Property InterpWeight As Double()
            Get
                Return _interpWeight
            End Get
        End Property

        ''' <summary>内部面 d = x_N − x_P。</summary>
        Public ReadOnly Property Delta As EulerianVertex()
            Get
                Return _delta
            End Get
        End Property

        ''' <summary>内部面正交扩散系数 Δ_f = |S_f|²/(S_f·d)。</summary>
        Public ReadOnly Property DeltaCoeff As Double()
            Get
                Return _deltaCoeff
            End Get
        End Property

        ''' <summary>内部面非正交修正向量 k_f = S_f − Δ_f·d。</summary>
        Public ReadOnly Property NonOrthCorrection As EulerianVertex()
            Get
                Return _nonOrthCorrection
            End Get
        End Property

        ''' <summary>内部面 → 非正交角（度）。</summary>
        Public ReadOnly Property OrthogonalityAngle As Double()
            Get
                Return _orthogonalityAngle
            End Get
        End Property

        Private Sub BuildInterpolationAndDiffusion()
            Dim nf As Integer = _internalFaces.Length

            _interpWeight = New Double(nf - 1) {}
            _delta = New EulerianVertex(nf - 1) {}
            _deltaCoeff = New Double(nf - 1) {}
            _nonOrthCorrection = New EulerianVertex(nf - 1) {}
            _orthogonalityAngle = New Double(nf - 1) {}

            For i As Integer = 0 To nf - 1
                Dim f As Integer = _internalFaces(i)
                Dim P As Integer = _internalOwner(i)
                Dim Q As Integer = _internalNeighbour(i)

                Dim d As EulerianVertex = _cellCentre(Q) - _cellCentre(P)
                _delta(i) = d

                ' 线性插值权重：w = |x_f − x_N| / (|x_f − x_P| + |x_f − x_N|)
                Dim dp As Double = (_cf(f) - _cellCentre(P)).Length
                Dim dn As Double = (_cf(f) - _cellCentre(Q)).Length
                Dim total As Double = dp + dn

                If total > 1.0E-300 Then
                    _interpWeight(i) = Clamp01(dn / total)
                Else
                    _interpWeight(i) = 0.5
                End If

                Dim sfd As Double = EulerianVertex.Dot(_sf(f), d)
                _deltaCoeff(i) = OrthogonalDeltaCoeff(_magSf(f), sfd, d.Length)

                _nonOrthCorrection(i) = New EulerianVertex(
                    _sf(f).X - _deltaCoeff(i) * d.X,
                    _sf(f).Y - _deltaCoeff(i) * d.Y,
                    _sf(f).Z - _deltaCoeff(i) * d.Z)

                ' 非正交角 = angle(n̂_f, x_P→x_N)
                Dim dl As Double = d.Length

                If dl > 1.0E-300 AndAlso _magSf(f) > 0 Then
                    Dim cosT As Double = sfd / (_magSf(f) * dl)
                    cosT = std.Max(-1.0, std.Min(1.0, cosT))
                    _orthogonalityAngle(i) = std.Acos(cosT) * 180.0 / std.PI
                Else
                    _orthogonalityAngle(i) = 90.0
                End If
            Next
        End Sub

        Private Shared Function Clamp01(v As Double) As Double
            If v < 0 Then Return 0
            If v > 1 Then Return 1
            Return v
        End Function

#End Region

#Region "单元–单元 CSR 邻接"

        Private _rowPtr As Integer()
        Private _colIdx As Integer()
        Private _entryFaceIndex As Integer()
        Private _diagEntry As Integer()
        Private _ownerEntry As Integer()
        Private _neighbourEntry As Integer()

        ''' <summary>CSR 行指针，长度 = CellCount + 1；每行首元素固定为对角元。</summary>
        Public ReadOnly Property RowPtr As Integer()
            Get
                Return _rowPtr
            End Get
        End Property

        ''' <summary>CSR 列索引（= 单元编号）。</summary>
        Public ReadOnly Property ColIdx As Integer()
            Get
                Return _colIdx
            End Get
        End Property

        ''' <summary>CSR 元素 → 对应网格面索引；对角元为 −1。</summary>
        Public ReadOnly Property EntryFaceIndex As Integer()
            Get
                Return _entryFaceIndex
            End Get
        End Property

        ''' <summary>单元 → 其对角元在 <see cref="ColIdx"/> 中的位置。</summary>
        Public ReadOnly Property DiagEntry As Integer()
            Get
                Return _diagEntry
            End Get
        End Property

        ''' <summary>内部面 → 该面在 Owner 行中的非对角元位置。</summary>
        Public ReadOnly Property OwnerEntry As Integer()
            Get
                Return _ownerEntry
            End Get
        End Property

        ''' <summary>内部面 → 该面在 Neighbour 行中的非对角元位置。</summary>
        Public ReadOnly Property NeighbourEntry As Integer()
            Get
                Return _neighbourEntry
            End Get
        End Property

        ''' <summary>非零元总数（含对角元）。</summary>
        Public ReadOnly Property NonZeroCount As Integer
            Get
                Return _rowPtr(_cellCount)
            End Get
        End Property

        Private Sub BuildCsrAdjacency()
            ' 1. 每个单元的内部面清单（行长度很小，通常 4~10）
            Dim adj(_cellCount - 1) As List(Of Integer)

            For c As Integer = 0 To _cellCount - 1
                adj(c) = New List(Of Integer)
            Next

            For i As Integer = 0 To _internalFaces.Length - 1
                Dim f As Integer = _internalFaces(i)

                adj(_internalOwner(i)).Add(f)
                adj(_internalNeighbour(i)).Add(f)
            Next

            ' 2. 行内按邻元编号插入排序（无分配、无闭包）
            For c As Integer = 0 To _cellCount - 1
                Dim row As List(Of Integer) = adj(c)
                Dim f As Integer
                Dim nb As Integer
                Dim j As Integer

                For k As Integer = 1 To row.Count - 1
                    f = row(k)

                    If Me.Mesh.Faces(f).Owner = c Then
                        nb = Me.Mesh.Faces(f).Neighbour
                    Else
                        nb = Me.Mesh.Faces(f).Owner
                    End If

                    j = k - 1

                    While j >= 0
                        Dim fj As Integer = row(j)
                        Dim nbj As Integer = If(Me.Mesh.Faces(fj).Owner = c, Me.Mesh.Faces(fj).Neighbour, Me.Mesh.Faces(fj).Owner)

                        If nbj <= nb Then
                            Exit While
                        End If

                        row(j + 1) = fj
                        j -= 1
                    End While

                    row(j + 1) = f
                Next
            Next

            ' 3. 展开 CSR
            _rowPtr = New Integer(_cellCount) {}

            For c As Integer = 0 To _cellCount - 1
                _rowPtr(c + 1) = _rowPtr(c) + 1 + adj(c).Count
            Next

            Dim nnz As Integer = _rowPtr(_cellCount)

            _colIdx = New Integer(nnz - 1) {}
            _entryFaceIndex = New Integer(nnz - 1) {}
            _diagEntry = New Integer(_cellCount - 1) {}
            _ownerEntry = InitMinusOne(_faceCount)
            _neighbourEntry = InitMinusOne(_faceCount)

            For c As Integer = 0 To _cellCount - 1
                Dim p As Integer = _rowPtr(c)

                _colIdx(p) = c
                _entryFaceIndex(p) = -1
                _diagEntry(c) = p

                Dim row As List(Of Integer) = adj(c)

                For k As Integer = 0 To row.Count - 1
                    Dim f As Integer = row(k)
                    Dim nb As Integer = If(Me.Mesh.Faces(f).Owner = c, Me.Mesh.Faces(f).Neighbour, Me.Mesh.Faces(f).Owner)

                    p += 1
                    _colIdx(p) = nb
                    _entryFaceIndex(p) = f

                    If Me.Mesh.Faces(f).Owner = c Then
                        _ownerEntry(f) = p
                    Else
                        _neighbourEntry(f) = p
                    End If
                Next
            Next
        End Sub

        Private Shared Function InitMinusOne(n As Integer) As Integer()
            Dim a(n - 1) As Integer

            For i As Integer = 0 To n - 1
                a(i) = -1
            Next

            Return a
        End Function

        ''' <summary>把 CSR 数值数组清零（保留结构）。</summary>
        Public Sub ClearCoefficients(values As Double())
            If values Is Nothing OrElse values.Length <> NonZeroCount Then
                Throw New ArgumentException($"系数数组长度必须等于 NonZeroCount = {NonZeroCount}", NameOf(values))
            End If

            Array.Clear(values, 0, values.Length)
        End Sub

        ''' <summary>按单元求 CSR 行和（用于诊断与检查）。</summary>
        Public Function RowSum(values As Double(), cell As Integer) As Double
            Dim s As Double = 0

            For e As Integer = _rowPtr(cell) To _rowPtr(cell + 1) - 1
                s += values(e)
            Next

            Return s
        End Function

#End Region

#Region "网格质量诊断"

        Private _minCellVolume As Double
        Private _maxCellVolume As Double
        Private _totalVolume As Double
        Private _maxNonOrthogonality As Double
        Private _avgNonOrthogonality As Double
        Private _nonPositiveVolumeCount As Integer
        Private _nonOrthAbove70Count As Integer

        ''' <summary>最小单元体积。</summary>
        Public ReadOnly Property MinCellVolume As Double
            Get
                Return _minCellVolume
            End Get
        End Property

        ''' <summary>最大单元体积。</summary>
        Public ReadOnly Property MaxCellVolume As Double
            Get
                Return _maxCellVolume
            End Get
        End Property

        ''' <summary>域总体积（Σ V_P）。</summary>
        Public ReadOnly Property TotalVolume As Double
            Get
                Return _totalVolume
            End Get
        End Property

        ''' <summary>最大非正交角（度）。</summary>
        Public ReadOnly Property MaxNonOrthogonality As Double
            Get
                Return _maxNonOrthogonality
            End Get
        End Property

        ''' <summary>平均非正交角（度）。</summary>
        Public ReadOnly Property AverageNonOrthogonality As Double
            Get
                Return _avgNonOrthogonality
            End Get
        End Property

        ''' <summary>体积非正的单元数（应为 0；&gt; 0 时构造阶段会抛异常）。</summary>
        Public ReadOnly Property NonPositiveVolumeCount As Integer
            Get
                Return _nonPositiveVolumeCount
            End Get
        End Property

        ''' <summary>非正交角超过 70°（OpenFOAM 建议上限）的内部面数。</summary>
        Public ReadOnly Property HighNonOrthogonalityFaceCount As Integer
            Get
                Return _nonOrthAbove70Count
            End Get
        End Property

        ''' <summary>本单元是否是"零体积 / 退化"单元。</summary>
        Public Function IsDegenerateCell(cell As Integer) As Boolean
            Return _cellVolume(cell) <= 0 OrElse Double.IsNaN(_cellVolume(cell))
        End Function

        Private Sub ComputeDiagnostics()
            Dim vmin As Double = Double.MaxValue
            Dim vmax As Double = Double.MinValue
            Dim vtot As Double = 0
            Dim bad As Integer = 0
            Dim badList As Integer = 0
            Dim firstBad As Integer = -1

            For c As Integer = 0 To _cellCount - 1
                Dim v As Double = _cellVolume(c)

                If Double.IsNaN(v) OrElse v <= 0 Then
                    bad += 1

                    If firstBad < 0 Then
                        firstBad = c
                    End If
                End If

                If v < vmin Then vmin = v
                If v > vmax Then vmax = v
                vtot += v
            Next

            _nonPositiveVolumeCount = bad
            _minCellVolume = If(bad = _cellCount, 0, vmin)
            _maxCellVolume = If(_cellCount = 0, 0, vmax)
            _totalVolume = vtot

            Dim amax As Double = 0
            Dim asum As Double = 0
            Dim n70 As Integer = 0
            Dim n As Integer = _orthogonalityAngle.Length

            For i As Integer = 0 To n - 1
                Dim a As Double = _orthogonalityAngle(i)

                If a > amax Then amax = a
                If a > 70.0 Then n70 += 1
                asum += a
            Next

            _maxNonOrthogonality = amax
            _avgNonOrthogonality = If(n = 0, 0, asum / n)
            _nonOrthAbove70Count = n70

            If bad > 0 Then
                Throw New InvalidDataException(
                    $"网格存在 {bad} 个体积非正（退化）单元，首个为单元 #{firstBad}（V={_cellVolume(firstBad).ToString("G6", CultureInfo.InvariantCulture)}）。" &
                    "请检查网格生成参数（resolution / cut-cells）或重新生成网格。")
            End If

            If vtot <= 0 Then
                Throw New InvalidDataException("网格总体积为 0（散度定理体积计算退化），无法用于有限体积求解")
            End If
        End Sub

        ''' <summary>网格质量与规模摘要（多行，可用于日志输出）。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(512)

                sb.AppendLine($"Eulerian FVM mesh: cells={_cellCount}, faces={_faceCount} " &
                              $"(internal={_internalFaces.Length}, boundary={_faceCount - _internalFaces.Length}), patches={_patchGeometries.Count}")
                sb.AppendLine($"  volume: total={_totalVolume.ToString("G6", CultureInfo.InvariantCulture)}, " &
                              $"min={_minCellVolume.ToString("G6", CultureInfo.InvariantCulture)}, " &
                              $"max={_maxCellVolume.ToString("G6", CultureInfo.InvariantCulture)}, " &
                              $"ratio={(If(_minCellVolume > 0, _maxCellVolume / _minCellVolume, Double.PositiveInfinity)).ToString("G6", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  csr: nnz={NonZeroCount} (avg {(NonZeroCount / std.Max(1.0, _cellCount)).ToString("F2", CultureInfo.InvariantCulture)} / cell)")
                sb.AppendLine($"  non-orthogonality: avg={_avgNonOrthogonality.ToString("F2", CultureInfo.InvariantCulture)} deg, " &
                              $"max={_maxNonOrthogonality.ToString("F2", CultureInfo.InvariantCulture)} deg, " &
                              $"faces>70deg={_nonOrthAbove70Count}")
                sb.AppendLine($"  patches: {PatchNames()}")

                If _unassignedBoundaryFaceCount > 0 Then
                    sb.AppendLine($"  warning: {_unassignedBoundaryFaceCount} boundary faces were not declared by any patch " &
                                  "and were collected into the synthesized 'defaultFaces' wall patch")
                End If

                Return sb.ToString()
            End Get
        End Property

#End Region

#Region "便捷访问"

        ''' <summary>单元 → 面索引表（直接转发底层网格）。</summary>
        Public Function CellFaces(cell As Integer) As Integer()
            Return Me.Mesh.CellFaces(cell)
        End Function

        ''' <summary>单元体积数组（只读引用，长度 = <see cref="CellCount"/>）。</summary>
        Public Function CellVolumeArray() As Double()
            Return _cellVolume
        End Function

        ''' <summary>网格包围盒。</summary>
        Public Sub GetBounds(ByRef min As EulerianVertex, ByRef max As EulerianVertex)
            Call Me.Mesh.GetBounds(min, max)
        End Sub

#End Region

#Region "加载"

        ''' <summary>
        ''' 从 <c>EulerianMeshPackage</c> 导出的 zip 网格文件加载并构建 FVM 视图。
        ''' </summary>
        ''' <param name="zipPath">eulerian 网格 zip（由 src\EulerianMesh CLI 或 ZMesh.EulerianPipeline 产出）</param>
        ''' <param name="patchTypeOverride">可选 patch 名称 → 类型覆盖表。</param>
        Public Shared Function FromFile(zipPath As String,
                                        Optional patchTypeOverride As IDictionary(Of String, EulerianPatchType) = Nothing) As EulerianFvMesh

            If String.IsNullOrEmpty(zipPath) Then
                Throw New ArgumentException("网格文件路径不能为空", NameOf(zipPath))
            End If

            If Not IO.File.Exists(zipPath) Then
                Throw New IO.FileNotFoundException($"找不到 Eulerian 网格文件：{zipPath}", zipPath)
            End If

            Dim raw As EulerianMesh = Moira.CFDEngine.Eulerian.EulerianMeshPackage.Load(zipPath)

            If raw.FaceAreas Is Nothing OrElse raw.CellVolumes Is Nothing Then
                Call raw.ComputeGeometry()
            End If

            Return New EulerianFvMesh(raw, patchTypeOverride)
        End Function

        Public Overrides Function ToString() As String
            Return $"EulerianFvMesh(cells={_cellCount}, faces={_faceCount}, patches={_patchGeometries.Count})"
        End Function

#End Region

    End Class

End Namespace
