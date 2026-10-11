Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' Laplacian 网格平滑器：将内部节点移动到相邻节点的几何中心，
''' 改善四面体/多面体单元的畸变（狭长单元）质量。
''' 边界节点（属于 Neighbour = -1 面的节点）锁定不动以保持边界拟合。
''' </summary>
Public Class LaplacianSmoother

    ''' <summary>
    ''' 执行平滑（原地修改 mesh.Points；边界节点保持不动）。
    ''' </summary>
    ''' <param name="mesh">已构建拓扑的 Eulerian 网格。</param>
    ''' <param name="iterations">迭代次数。</param>
    ''' <param name="relax">松弛因子（0..1，1 = 直接移到邻域质心）。</param>
    Public Shared Sub Smooth(mesh As EulerianMesh,
                             Optional iterations As Integer = 5,
                             Optional relax As Double = 0.5)

        If iterations <= 0 OrElse mesh.Faces.Count = 0 Then
            Return
        End If

        ' 节点邻接（面环上相邻节点互为邻居）
        Dim neighbors As List(Of Integer)() = New List(Of Integer)(mesh.Points.Count - 1) {}

        For v As Integer = 0 To mesh.Points.Count - 1
            neighbors(v) = New List(Of Integer)
        Next

        Dim neighborSet As New HashSet(Of Long)

        For Each f As EulerianFace In mesh.Faces
            Dim n As Integer = f.PointIndices.Count

            For j As Integer = 0 To n - 1
                Dim u As Integer = f.PointIndices(j)
                Dim w As Integer = f.PointIndices((j + 1) Mod n)
                Dim ka As Long = CLng(u) * 4294967296L + w
                Dim kb As Long = CLng(w) * 4294967296L + u

                If neighborSet.Add(ka) Then
                    neighbors(u).Add(w)
                End If

                If neighborSet.Add(kb) Then
                    neighbors(w).Add(u)
                End If
            Next
        Next

        ' 边界节点锁定
        Dim isBoundary As New HashSet(Of Integer)

        For Each f As EulerianFace In mesh.Faces
            If f.IsBoundary Then
                For Each vi As Integer In f.PointIndices
                    isBoundary.Add(vi)
                Next
            End If
        Next

        ' 迭代平滑
        For it As Integer = 1 To iterations
            Dim newPos As EulerianVertex() = New EulerianVertex(mesh.Points.Count - 1) {}

            For v As Integer = 0 To mesh.Points.Count - 1
                If isBoundary.Contains(v) OrElse neighbors(v).Count = 0 Then
                    newPos(v) = mesh.Points(v)
                    Continue For
                End If

                Dim sx As Double = 0, sy As Double = 0, sz As Double = 0

                For Each nb As Integer In neighbors(v)
                    sx += mesh.Points(nb).X
                    sy += mesh.Points(nb).Y
                    sz += mesh.Points(nb).Z
                Next

                Dim m As Integer = neighbors(v).Count
                Dim centroid As New EulerianVertex(sx / m, sy / m, sz / m)
                newPos(v) = mesh.Points(v) + (centroid - mesh.Points(v)) * relax
            Next

            For v As Integer = 0 To mesh.Points.Count - 1
                mesh.Points(v) = newPos(v)
            Next
        Next

        ' 几何量失效：重新计算
        Call mesh.ComputeGeometry()
    End Sub
End Class
