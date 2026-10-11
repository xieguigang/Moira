Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>用户显式指定的 Patch 覆盖规则。</summary>
Public Class PatchRule

    ''' <summary>Patch 名称。</summary>
    Public Name As String

    ''' <summary>边界类型。</summary>
    Public Type As EulerianPatchType

    ''' <summary>计算域包围盒面：XMin/XMax/YMin/YMax/ZMin/ZMax（位置匹配）。</summary>
    Public BoxFace As String

    ''' <summary>是否按"几何区域盒"匹配（优先级高于 <see cref="BoxFace"/>）。</summary>
    Public Property RegionBox As Boolean

    ' ---- 区域盒（RegionBox = True 时生效）：面形心落入盒内即命中 ----

    Public Property MinX As Double = 0
    Public Property MinY As Double = 0
    Public Property MinZ As Double = 0
    Public Property MaxX As Double = 0
    Public Property MaxY As Double = 0
    Public Property MaxZ As Double = 0

    ''' <summary>
    ''' 构造一条"区域盒 → patch"规则。
    ''' 用于内流域中区分搅拌桨叶 / 挡板 / 罐壁（它们都在同一套边界面上，
    ''' 无法用包围盒面位置区分）。
    ''' </summary>
    Public Shared Function Region(name As String, type As EulerianPatchType,
                                  minX As Double, minY As Double, minZ As Double,
                                  maxX As Double, maxY As Double, maxZ As Double) As PatchRule

        Return New PatchRule With {
            .Name = name,
            .Type = type,
            .RegionBox = True,
            .MinX = minX,
            .MinY = minY,
            .MinZ = minZ,
            .MaxX = maxX,
            .MaxY = maxY,
            .MaxZ = maxZ}
    End Function

    ''' <summary>面形心是否落在区域盒内。</summary>
    Public Function MatchesRegion(p As EulerianVertex, tol As Double) As Boolean
        If Not RegionBox Then Return False

        Return p.X >= MinX - tol AndAlso p.X <= MaxX + tol AndAlso
               p.Y >= MinY - tol AndAlso p.Y <= MaxY + tol AndAlso
               p.Z >= MinZ - tol AndAlso p.Z <= MaxZ + tol
    End Function
End Class

''' <summary>
''' 边界 Patch 分类器：
''' 默认按计算域包围盒位置自动推断（Y 下 = Wall 地面、X 极值 = Inlet/Outlet、
''' Z 极值 = Symmetry、Y 上 = Outlet 顶面），用户规则（<see cref="PatchRule"/>）
''' 按面片名优先覆盖。
'''
''' 内流模式（<see cref="DomainMode.Internal"/>）：容器外表面的所有面一律归为
''' 壁面（发酵罐的罐壁 / 挡板 / 桨叶 / 轴），不再做 inlet/outlet/ground 的位置推断 ——
''' 否则罐顶会被误判成出口、罐底被误判成地面。
''' </summary>
Public Class PatchClassifier

    ''' <summary>自动分类的容差（相对面形心到盒面距离，采用相对盒尺寸的比例）。</summary>
    Const FaceTolerance As Double = 0.001

    Public Shared Function Classify(mesh As EulerianMesh,
                                    domain As GeometryPreprocessor.FluidDomain,
                                    Optional rules As List(Of PatchRule) = Nothing) As List(Of EulerianPatch)

        Dim bmin, bmax As EulerianVertex
        Call mesh.GetBounds(bmin, bmax)
        Dim size As EulerianVertex = bmax - bmin
        Dim tol As Double = FaceTolerance * std.Max(std.Max(size.X, size.Y), size.Z)
        Dim internal As Boolean = domain IsNot Nothing AndAlso domain.Mode = DomainMode.Internal

        Dim patches As New Dictionary(Of String, EulerianPatch)

        For fi As Integer = 0 To mesh.Faces.Count - 1
            Dim f As EulerianFace = mesh.Faces(fi)

            If Not f.IsBoundary Then
                Continue For
            End If

            Dim centroid As EulerianVertex = mesh.FaceCentroids(fi)
            Dim name As String
            Dim type As EulerianPatchType

            If internal Then
                ' 内流：默认全部为壁面（容器外壳 + 次级实体表面）
                name = "wall"
                type = EulerianPatchType.Wall
            Else
                name = AutoPatchName(centroid, bmin, bmax, tol)
                type = AutoPatchType(name)
            End If

            ' 用户规则覆盖：区域盒优先（内流域区分桨叶 / 挡板 / 罐壁），其次盒面位置
            If rules IsNot Nothing Then
                Dim matched As Boolean = False

                For Each rule As PatchRule In rules
                    If rule.MatchesRegion(centroid, tol) Then
                        name = rule.Name
                        type = rule.Type
                        matched = True
                        Exit For
                    End If
                Next

                If Not matched Then
                    For Each rule As PatchRule In rules
                        If MatchBoxFace(rule.BoxFace, centroid, bmin, bmax, tol) Then
                            name = rule.Name
                            type = rule.Type
                            Exit For
                        End If
                    Next
                End If
            End If

            If Not patches.ContainsKey(name) Then
                patches(name) = New EulerianPatch With {.Name = name, .Type = type}
            End If

            Call patches(name).FaceIndices.Add(fi)
        Next

        Dim result As New List(Of EulerianPatch)
        result.AddRange(patches.Values)
        Return result
    End Function

    Private Shared Function MatchBoxFace(boxFace As String, p As EulerianVertex,
                                         bmin As EulerianVertex, bmax As EulerianVertex, tol As Double) As Boolean
        If String.IsNullOrEmpty(boxFace) Then
            Return False
        End If

        Select Case boxFace.ToLowerInvariant
            Case "xmin" : Return std.Abs(p.X - bmin.X) <= tol
            Case "xmax" : Return std.Abs(p.X - bmax.X) <= tol
            Case "ymin" : Return std.Abs(p.Y - bmin.Y) <= tol
            Case "ymax" : Return std.Abs(p.Y - bmax.Y) <= tol
            Case "zmin" : Return std.Abs(p.Z - bmin.Z) <= tol
            Case "zmax" : Return std.Abs(p.Z - bmax.Z) <= tol
            Case Else : Return False
        End Select
    End Function

    ''' <summary>按包围盒位置自动推断 Patch 名。</summary>
    Public Shared Function AutoPatchName(p As EulerianVertex,
                                         bmin As EulerianVertex, bmax As EulerianVertex, tol As Double) As String
        ' 距哪个盒面最近
        Dim best As String = "wall"
        Dim bestDist As Double = Double.MaxValue
        Dim candidates As (String, Double)() = {
            ("inlet", std.Abs(p.X - bmin.X)),
            ("outlet", std.Abs(p.X - bmax.X)),
            ("ground", std.Abs(p.Y - bmin.Y)),
            ("top", std.Abs(p.Y - bmax.Y)),
            ("symmetry-z0", std.Abs(p.Z - bmin.Z)),
            ("symmetry-z1", std.Abs(p.Z - bmax.Z))}

        For Each c In candidates
            If c.Item2 < bestDist Then
                bestDist = c.Item2
                best = c.Item1
            End If
        Next

        If bestDist > tol Then
            ' 边界面不在任何盒面上（如模型表面）：归入壁面
            Return "wall"
        End If

        Return best
    End Function

    ''' <summary>
    ''' 按 Patch 名自动推断边界类型。
    ''' 除位置推断出的名字外，还识别搅拌罐 / 旋转机械的常见部件命名
    ''' （vessel / baffle / impeller / blade / shaft / headspace），这些一律为壁面；
    ''' 其中 impeller / blade / shaft 由调用方（<c>IncompressibleSolverOptions.RotatingWallPatches</c>）
    ''' 进一步升级为"随 MRF 区旋转"的运动壁面。
    ''' </summary>
    Public Shared Function AutoPatchType(name As String) As EulerianPatchType
        If name Is Nothing Then Return EulerianPatchType.Symmetry

        Select Case name.ToLowerInvariant
            Case "inlet"
                Return EulerianPatchType.Inlet
            Case "outlet", "top"
                Return EulerianPatchType.Outlet
            Case "ground", "wall", "vessel", "baffle", "impeller", "blade", "shaft",
                 "headspace", "wall-impeller", "wall-shaft", "wall-baffle"
                Return EulerianPatchType.Wall
            Case Else
                Return EulerianPatchType.Symmetry
        End Select
    End Function
End Class
