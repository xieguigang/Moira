Namespace Eulerian

    ''' <summary>
    ''' 边界面片（Patch）的物理类型，对应 CFD 求解器的边界条件类别。
    ''' </summary>
    Public Enum EulerianPatchType
        ''' <summary>壁面（无滑移/滑移固壁）。</summary>
        Wall = 0
        ''' <summary>入口（速度/压力入口）。</summary>
        Inlet = 1
        ''' <summary>出口（压力出口/outflow）。</summary>
        Outlet = 2
        ''' <summary>对称面。</summary>
        Symmetry = 3
    End Enum

    ''' <summary>
    ''' 边界面片：一组被划归同一边界条件类别的边界面（Neighbour = -1 的面）。
    ''' </summary>
    Public Class EulerianPatch

        ''' <summary>Patch 名称（如 "ground"、"inlet-xmin"）。</summary>
        Public Name As String = ""

        ''' <summary>边界类型。</summary>
        Public Type As EulerianPatchType = EulerianPatchType.Wall

        ''' <summary>归属于本 Patch 的面索引集合。</summary>
        Public FaceIndices As New List(Of Integer)

        Public Overrides Function ToString() As String
            Return $"{Name}({Type.ToString}, {FaceIndices.Count} faces)"
        End Function
    End Class
End Namespace
