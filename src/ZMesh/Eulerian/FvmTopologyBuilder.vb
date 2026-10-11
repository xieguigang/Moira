Imports System.IO
Imports Moira.CFDEngine.Eulerian

''' <summary>
''' FVM 拓扑构建器：将生成器产出的中间多面体网格 <see cref="RawCellMesh"/>
''' 转换为 OpenFOAM 风格的 <see cref="EulerianMesh"/>（非邻接列表数据结构）。
'''
''' 算法（图遍历 + 哈希映射）：
'''   1. 遍历每个单元的每个面，以"顶点索引排序组合"为键做哈希（消除方向性）；
'''   2. 键首次出现的面登记入面表（Owner = 登记单元，绕序 = 单元外法向）；
'''   3. 键再次出现的面 = 内部面：Neighbour = 第二个单元（两侧绕序互逆，由
'''      <see cref="IsSameCycle"/> 做环绕等价校验）；
'''   4. 只出现一次的面 = 边界面（Neighbour = -1）；
'''   5. 构建 CellFaces（单元 → 面索引非邻接列表）并预计算几何量。
''' </summary>
Public Class FvmTopologyBuilder

    Public Shared Function Build(raw As RawCellMesh) As EulerianMesh
        Dim mesh As New EulerianMesh
        mesh.Points.AddRange(raw.Points)

        ' 过滤退化单元（面数不足或含退化面；真实模型脏数据防护）
        Dim cells As New List(Of RawCell)
        Dim skippedCells As Integer = 0

        For Each cell As RawCell In raw.Cells
            Dim valid As Integer = cell.Faces.Where(Function(f) f.PointIndices.Count >= 3).Count()

            If valid < 4 Then
                skippedCells += 1
                Continue For
            End If

            cells.Add(cell)
        Next

        ' 单元 → 面索引表
        Dim cellFaces As List(Of Integer)() = New List(Of Integer)(cells.Count - 1) {}

        For c As Integer = 0 To cells.Count - 1
            cellFaces(c) = New List(Of Integer)
        Next

        ' 面键 → 候选面索引（哈希碰撞时做环绕等价校验）
        Dim faceMap As New Dictionary(Of Long, List(Of Integer))

        For c As Integer = 0 To cells.Count - 1
            For Each f As RawFace In cells(c).Faces
                If f.PointIndices.Count < 3 Then
                    Continue For
                End If

                Dim key As Long = FaceKey(f.PointIndices)
                Dim found As Integer = -1

                If faceMap.ContainsKey(key) Then
                    For Each fi As Integer In faceMap(key)
                        If IsSameCycle(mesh.Faces(fi).PointIndices, f.PointIndices) Then
                            found = fi
                            Exit For
                        End If
                    Next
                End If

                If found < 0 Then
                    ' 首次登记：Owner = 本单元，Neighbour 暂 -1（边界面语义）
                    Dim face As New EulerianFace With {
                        .PointIndices = New List(Of Integer)(f.PointIndices),
                        .Owner = c,
                        .Neighbour = -1}
                    mesh.Faces.Add(face)
                    found = mesh.Faces.Count - 1

                    If Not faceMap.ContainsKey(key) Then
                        faceMap(key) = New List(Of Integer)
                    End If

                    faceMap(key).Add(found)
                Else
                    ' 内部面：Neighbour = 本单元
                    mesh.Faces(found).Neighbour = c
                End If
            Next
        Next

        ' CellFaces
        For fi As Integer = 0 To mesh.Faces.Count - 1
            cellFaces(mesh.Faces(fi).Owner).Add(fi)

            If mesh.Faces(fi).Neighbour >= 0 Then
                cellFaces(mesh.Faces(fi).Neighbour).Add(fi)
            End If
        Next

        For c As Integer = 0 To cells.Count - 1
            mesh.CellFaces.Add(cellFaces(c).ToArray)
        Next

        Call mesh.ComputeGeometry()
        Return mesh
    End Function

    ''' <summary>面键：排序顶点索引前三项位打包（消除方向性；碰撞由环绕校验兜底）。</summary>
    Friend Shared Function FaceKey(loop0 As List(Of Integer)) As Long
        Dim sorted As Integer() = loop0.ToArray
        Array.Sort(sorted)
        Return DelaunayFaceKey(sorted(0), sorted(1), sorted(2))
    End Function

    Public Shared Function DelaunayFaceKey(a As Integer, b As Integer, c As Integer) As Long
        Dim x As Integer = a, y As Integer = b, z As Integer = c

        If y < x Then
            Dim t As Integer = x : x = y : y = t
        End If

        If z < y Then
            Dim t As Integer = y : y = z : z = t

            If y < x Then
                t = x : x = y : y = t
            End If
        End If

        Return CLng(x And &H1FFFFF) Or (CLng(y And &H1FFFFF) << 21) Or (CLng(z And &H1FFFFF) << 42)
    End Function

    ''' <summary>
    ''' 判断两个顶点环是否为同一多边形（允许循环旋转与反向）。
    ''' 内部面在相邻两个单元中的绕序恰好相反。
    ''' </summary>
    Friend Shared Function IsSameCycle(a As List(Of Integer), b As List(Of Integer)) As Boolean
        Dim n As Integer = a.Count

        If n <> b.Count Then
            Return False
        End If

        For start As Integer = 0 To n - 1
            If b(start) <> a(0) Then
                Continue For
            End If

            ' 正向
            Dim ok As Boolean = True

            For i As Integer = 0 To n - 1
                If b((start + i) Mod n) <> a(i) Then
                    ok = False
                    Exit For
                End If
            Next

            If ok Then
                Return True
            End If

            ' 反向
            ok = True

            For i As Integer = 0 To n - 1
                If b((start - i + n) Mod n) <> a(i) Then
                    ok = False
                    Exit For
                End If
            Next

            If ok Then
                Return True
            End If
        Next

        Return False
    End Function
End Class
