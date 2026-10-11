Imports System.IO
Imports System.IO.Compression
Imports System.Text.Json

' /********************************************************************************/
'
'   EulerianMeshPackage.vb
'
'   Eulerian（FVM）网格数据模型压缩包（zip）读写 —— 沿用 VoxelPackage 的
'   model.json 元数据 + 二进制数据条目（ZipArchiveEntry 自动 Deflate）模式。
'
'   包内文件布局：
'       mesh.json      —— EulerianMeshInfo 元数据（点/面/单元数、Patch 定义、包围盒）
'       mesh.points    —— 节点坐标流：每点 3 × Double（小端）
'       mesh.faces     —— 面流：每面 [Int32 顶点数 n][Int32 owner][Int32 neighbour][n × Int32 顶点索引]
'       mesh.cellfaces —— 单元流：每单元 [Int32 面数 m][m × Int32 面索引]
'       mesh.patches   —— Patch 流：每 Patch [Int32 面数 k][k × Int32 面索引]
'
'   二进制条目通过 BinaryWriter/BinaryReader 顺序流式读写（小端），
'   数据条目由 ZipArchiveEntry 以 CompressionLevel.Optimal 自动 Deflate 压缩，
'   大网格全程无巨型中间缓冲。
'
' /********************************************************************************/

Namespace Eulerian

    ''' <summary>mesh.json 元数据 POCO。</summary>
    Public Class EulerianMeshInfo

        <System.Text.Json.Serialization.JsonPropertyName("version")>
        Public Version As Integer = 1

        <System.Text.Json.Serialization.JsonPropertyName("pointCount")>
        Public PointCount As Integer

        <System.Text.Json.Serialization.JsonPropertyName("faceCount")>
        Public FaceCount As Integer

        <System.Text.Json.Serialization.JsonPropertyName("cellCount")>
        Public CellCount As Integer

        <System.Text.Json.Serialization.JsonPropertyName("boundaryFaceCount")>
        Public BoundaryFaceCount As Integer

        <System.Text.Json.Serialization.JsonPropertyName("boundsMin")>
        Public BoundsMin As Double()

        <System.Text.Json.Serialization.JsonPropertyName("boundsMax")>
        Public BoundsMax As Double()

        <System.Text.Json.Serialization.JsonPropertyName("totalVolume")>
        Public TotalVolume As Double

        <System.Text.Json.Serialization.JsonPropertyName("patches")>
        Public Patches As List(Of EulerianPatchInfo)

        <System.Text.Json.Serialization.JsonPropertyName("generator")>
        Public Generator As String = ""

        <System.Text.Json.Serialization.JsonPropertyName("createdUtc")>
        Public CreatedUtc As String = ""
    End Class

    ''' <summary>Patch 元数据。</summary>
    Public Class EulerianPatchInfo

        <System.Text.Json.Serialization.JsonPropertyName("name")>
        Public Name As String

        <System.Text.Json.Serialization.JsonPropertyName("type")>
        Public Type As String

        <System.Text.Json.Serialization.JsonPropertyName("faceCount")>
        Public FaceCount As Integer
    End Class

    ''' <summary>
    ''' Eulerian 网格压缩包读写工具（zip：mesh.json + 二进制数据条目）。
    ''' </summary>
    Public Module EulerianMeshPackage

        Public Const MeshJsonEntry As String = "mesh.json"
        Public Const MeshPointsEntry As String = "mesh.points"
        Public Const MeshFacesEntry As String = "mesh.faces"
        Public Const MeshCellFacesEntry As String = "mesh.cellfaces"
        Public Const MeshPatchesEntry As String = "mesh.patches"

        ''' <summary>便捷方法：将网格保存为 zip 压缩包。</summary>
        ''' <param name="mesh">网格（若几何量未预计算则内部调用 ComputeGeometry）。</param>
        ''' <param name="zipPath">输出 zip 路径。</param>
        ''' <param name="generator">生成器标识（写入元数据）。</param>
        Public Sub Save(mesh As EulerianMesh, zipPath As String, Optional generator As String = "")
            Using writer As New Writer(zipPath)
                Call writer.Write(mesh, generator)
            End Using
        End Sub

        ''' <summary>便捷方法：从 zip 压缩包加载网格。</summary>
        Public Function Load(zipPath As String) As EulerianMesh
            Using reader As New Reader(zipPath)
                Return reader.LoadMesh()
            End Using
        End Function

        ''' <summary>便捷方法：只读包内元数据（mesh.json）。</summary>
        Public Function ReadMetadata(zipPath As String) As EulerianMeshInfo
            Using reader As New Reader(zipPath)
                Return reader.Info
            End Using
        End Function

        ''' <summary>
        ''' 写端：用法 New → Write(mesh) → Dispose；或 WriteMetadata / OpenXxx 手动流式写。
        ''' </summary>
        Public NotInheritable Class Writer : Implements IDisposable

            Private ReadOnly _zip As ZipArchive
            Private _finished As Boolean

            ''' <summary>创建写端（zip 落盘到文件）。</summary>
            Public Sub New(filePath As String)
                Dim dir = Path.GetDirectoryName(Path.GetFullPath(filePath))

                If Not String.IsNullOrEmpty(dir) Then
                    Call Directory.CreateDirectory(dir)
                End If

                _zip = New ZipArchive(File.Create(filePath), ZipArchiveMode.Create)
            End Sub

            ''' <summary>创建写端（写入任意输出流）。</summary>
            Public Sub New(output As Stream)
                _zip = New ZipArchive(output, ZipArchiveMode.Create)
            End Sub

            ''' <summary>一次性写出完整网格（元数据 + 全部二进制条目）。</summary>
            Public Sub Write(mesh As EulerianMesh, Optional generator As String = "")
                Call WriteMetadata(mesh, generator)

                Using ps As Stream = OpenEntry(MeshPointsEntry)
                    Using w As New BinaryWriter(ps)
                        For Each p As EulerianVertex In mesh.Points
                            Call w.Write(p.X)
                            Call w.Write(p.Y)
                            Call w.Write(p.Z)
                        Next
                    End Using
                End Using

                Using fs As Stream = OpenEntry(MeshFacesEntry)
                    Using w As New BinaryWriter(fs)
                        For Each f As EulerianFace In mesh.Faces
                            Call w.Write(f.PointIndices.Count)
                            Call w.Write(f.Owner)
                            Call w.Write(f.Neighbour)

                            For Each vi As Integer In f.PointIndices
                                Call w.Write(vi)
                            Next
                        Next
                    End Using
                End Using

                Using cs As Stream = OpenEntry(MeshCellFacesEntry)
                    Using w As New BinaryWriter(cs)
                        For Each cell As Integer() In mesh.CellFaces
                            Call w.Write(cell.Length)

                            For Each fi As Integer In cell
                                Call w.Write(fi)
                            Next
                        Next
                    End Using
                End Using

                Using ps As Stream = OpenEntry(MeshPatchesEntry)
                    Using w As New BinaryWriter(ps)
                        For Each patch As EulerianPatch In mesh.Patches
                            Call w.Write(patch.FaceIndices.Count)

                            For Each fi As Integer In patch.FaceIndices
                                Call w.Write(fi)
                            Next
                        Next
                    End Using
                End Using
            End Sub

            ''' <summary>写出元数据 mesh.json。</summary>
            Public Sub WriteMetadata(mesh As EulerianMesh, Optional generator As String = "")
                Dim info As New EulerianMeshInfo With {
                    .Version = 1,
                    .PointCount = mesh.PointCount,
                    .FaceCount = mesh.FaceCount,
                    .CellCount = mesh.CellCount,
                    .BoundaryFaceCount = mesh.BoundaryFaceCount,
                    .TotalVolume = mesh.TotalVolume,
                    .Generator = generator,
                    .CreatedUtc = Date.UtcNow.ToString("u"),
                    .Patches = New List(Of EulerianPatchInfo)
                }

                For Each patch As EulerianPatch In mesh.Patches
                    info.Patches.Add(New EulerianPatchInfo With {
                        .Name = patch.Name,
                        .Type = patch.Type.ToString,
                        .FaceCount = patch.FaceIndices.Count})
                Next

                Dim bmin, bmax As EulerianVertex
                Call mesh.GetBounds(bmin, bmax)
                info.BoundsMin = {bmin.X, bmin.Y, bmin.Z}
                info.BoundsMax = {bmax.X, bmax.Y, bmax.Z}

                Using entryStream As Stream = _zip.CreateEntry(MeshJsonEntry).Open()
                    Using text As New StreamWriter(entryStream)
                        Call text.Write(JsonSerializer.Serialize(
                            info,
                            New JsonSerializerOptions With {.WriteIndented = False, .IncludeFields = True}))
                    End Using
                End Using
            End Sub

            Friend Function OpenEntry(name As String) As Stream
                Return _zip.CreateEntry(name, CompressionLevel.Optimal).Open()
            End Function

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _finished Then
                    _finished = True
                End If

                Call _zip.Dispose()
            End Sub
        End Class

        ''' <summary>
        ''' 读端：New → Info（元数据）→ LoadMesh()（完整网格）→ Dispose。
        ''' </summary>
        Public NotInheritable Class Reader : Implements IDisposable

            Private ReadOnly _zip As ZipArchive

            ''' <summary>包内元数据（mesh.json）。</summary>
            Public ReadOnly Property Info As EulerianMeshInfo

            Public Sub New(filePath As String)
                _zip = ZipFile.OpenRead(filePath)
                Info = ParseMetadata()
            End Sub

            Public Sub New(input As Stream, leaveOpen As Boolean)
                _zip = New ZipArchive(input, ZipArchiveMode.Read, leaveOpen)
                Info = ParseMetadata()
            End Sub

            ''' <summary>加载完整网格（点/面/单元/Patch + 几何量预计算）。</summary>
            ''' <param name="computeGeometry">是否在加载后调用 ComputeGeometry 预计算几何量。</param>
            Public Function LoadMesh(Optional computeGeometry As Boolean = True) As EulerianMesh
                Dim mesh As New EulerianMesh

                ' DeflateStream 不支持 Length/Position，先把条目读入内存再解析
                Using r As BinaryReader = OpenBinary(MeshPointsEntry)
                    Dim n As Integer = If(Info IsNot Nothing, Info.PointCount, CInt(r.BaseStream.Length \ 24))

                    For i As Integer = 0 To n - 1
                        mesh.Points.Add(New EulerianVertex(r.ReadDouble, r.ReadDouble, r.ReadDouble))
                    Next
                End Using

                ' 面（写端顺序：[顶点数 n][owner][neighbour][n × 顶点索引]）
                Using r As BinaryReader = OpenBinary(MeshFacesEntry)
                    While r.BaseStream.Position < r.BaseStream.Length
                        Dim nv As Integer = r.ReadInt32()
                        Dim owner As Integer = r.ReadInt32()
                        Dim neighbour As Integer = r.ReadInt32()
                        Dim f As New EulerianFace With {
                            .PointIndices = New List(Of Integer),
                            .Owner = owner,
                            .Neighbour = neighbour}

                        For j As Integer = 0 To nv - 1
                            f.PointIndices.Add(r.ReadInt32())
                        Next

                        mesh.Faces.Add(f)
                    End While
                End Using

                ' 单元 → 面索引表
                Using r As BinaryReader = OpenBinary(MeshCellFacesEntry)
                    While r.BaseStream.Position < r.BaseStream.Length
                        Dim m As Integer = r.ReadInt32()
                        Dim cell As Integer() = New Integer(m - 1) {}

                        For j As Integer = 0 To m - 1
                            cell(j) = r.ReadInt32()
                        Next

                        mesh.CellFaces.Add(cell)
                    End While
                End Using

                ' Patch（面索引；名称/类型来自元数据）
                Using r As BinaryReader = OpenBinary(MeshPatchesEntry)
                    Dim patchIndex As Integer = 0

                    While r.BaseStream.Position < r.BaseStream.Length
                        Dim k As Integer = r.ReadInt32()
                        Dim patch As New EulerianPatch

                        If Info IsNot Nothing AndAlso Info.Patches IsNot Nothing AndAlso
                            patchIndex < Info.Patches.Count Then

                            patch.Name = Info.Patches(patchIndex).Name
                            patch.Type = ParsePatchType(Info.Patches(patchIndex).Type)
                        Else
                            patch.Name = $"patch{patchIndex}"
                        End If

                        For j As Integer = 0 To k - 1
                            patch.FaceIndices.Add(r.ReadInt32())
                        Next

                        mesh.Patches.Add(patch)
                        patchIndex += 1
                    End While
                End Using

                If computeGeometry Then
                    Call mesh.ComputeGeometry()
                End If

                Return mesh
            End Function

            ''' <summary>把 zip 条目整段读入内存并包装为 BinaryReader。</summary>
            Private Function OpenBinary(entryName As String) As BinaryReader
                Using s As Stream = FindEntry(entryName).Open()
                    Dim ms As New MemoryStream
                    Call s.CopyTo(ms)
                    ms.Position = 0
                    Return New BinaryReader(ms)
                End Using
            End Function

            Private Shared Function ParsePatchType(text As String) As EulerianPatchType
                Dim parsed As EulerianPatchType = EulerianPatchType.Wall

                If Not String.IsNullOrEmpty(text) AndAlso [Enum].TryParse(text, True, parsed) Then
                    Return parsed
                End If

                Return EulerianPatchType.Wall
            End Function

            Private Function FindEntry(name As String) As ZipArchiveEntry
                Dim entry As ZipArchiveEntry = _zip.GetEntry(name)

                If entry Is Nothing Then
                    Throw New InvalidDataException($"Eulerian 网格包缺少条目 {name}")
                End If

                Return entry
            End Function

            Private Function ParseMetadata() As EulerianMeshInfo
                Using entryStream As Stream = FindEntry(MeshJsonEntry).Open()
                    Dim info As EulerianMeshInfo = JsonSerializer.Deserialize(Of EulerianMeshInfo)(
                        entryStream,
                        New JsonSerializerOptions With {.IncludeFields = True})

                    If info Is Nothing Then
                        Throw New InvalidDataException("mesh.json 解析失败")
                    End If

                    Return info
                End Using
            End Function

            Public Sub Dispose() Implements IDisposable.Dispose
                Call _zip.Dispose()
            End Sub
        End Class

    End Module
End Namespace
