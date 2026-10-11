' /********************************************************************************/
'
'   VTUExporter.vb
'
'   VTU 导出器 —— 把 **非结构 Eulerian 网格** 的仿真结果导出为
'   VTK XML UnstructuredGrid（.vtu）二进制文件。
'
'   为什么需要它（与 VTIExporter 的分工）：
'       VTI（VTK XML ImageData）在数学上只能表达**结构化的笛卡尔格**，
'       而 Eulerian FVM 网格是任意面数的多面体（八叉树切割单元 / 四面体 /
'       棱柱）。要把多面体单元的单元值可视化，必须使用 UnstructuredGrid。
'         · VTIExporter   → 结构化体素场（既有体素求解器，零回归）
'         · VTUExporter   → 非结构多面体网格（Eulerian 求解器）
'
'   ★ 单元表达方式的选择（重要工程取舍）
'       原生 VTK_POLYHEDRON（cell type 42）在 XML 格式下需要同时写出
'       connectivity / offsets / faces / faceoffsets 四套数组，且各版本
'       ParaView/VTK 对偏移语义的处理存在历史差异，**离线无法验证**。
'       本实现改用**"以单元形心为锥顶的三角形锥分解"**：
'           每个多面体单元 → 它的每个面三角扇 + 单元形心 → 若干 VTK_TETRA
'       优点：
'           · 只用到 VTK_TETRA(10) 与点数据，任何 VTK 版本 / ParaView /
'             VTK.js 都能直接读取，零兼容性风险；
'           · 单元数据在每个单元的所有子四面体上取同值，因此**可视化结果与
'             原生多面体完全一致**（单元数据本就是分片常数）；
'           · 单元形心作为追加点写入，不修改原网格。
'       代价：文件体积增大约 4~6 倍（六面体 6 面 → 12 个四面体）。
'             需要更紧凑时，可实现原生 polyhedron 写出器替换
'             <see cref="BuildTetrahedra"/>，对外接口不变。
'
'   文件格式（与 VTIExporter 一致的 appended-raw 约定）：
'       <VTKFile type="UnstructuredGrid" header_type="UInt32">
'       AppendedData encoding="raw"，每个数组前有一个 UInt32 长度头（字节数），
'       数组在 appended 段中的偏移写入对应 DataArray 的 offset 属性。
'
'   使用方法：
'       VTUExporter.Export("frame_0000.vtu", mesh, fields, step:=10, time:=1.0)
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports Moira.CFDEngine.Eulerian

Namespace Snapshot

    ''' <summary>
    ''' 把非结构 Eulerian 网格的单元场导出为 VTK XML UnstructuredGrid（.vtu，二进制）。
    ''' </summary>
    Public Class VTUExporter

        ''' <summary>VTK_TETRA 的单元类型编号。</summary>
        Public Const VtkTetra As Byte = 10

        ''' <summary>
        ''' 导出到 .vtu 文件。
        ''' </summary>
        ''' <param name="filePath">输出文件路径</param>
        ''' <param name="mesh">非结构网格</param>
        ''' <param name="cellFields">
        ''' 逐单元字段（复用 <see cref="VtiField"/> 描述）。数据按**单元主序**给出：
        ''' idx = cell * Components + component。
        ''' </param>
        ''' <param name="stepIndex">时间步序号（写入 FieldData，可选）</param>
        ''' <param name="time">模拟时间（写入 FieldData，可选）</param>
        Public Shared Sub Export(filePath As String,
                                 mesh As EulerianMesh,
                                 cellFields As IEnumerable(Of VtiField),
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0)

            If filePath Is Nothing Then Throw New ArgumentNullException(NameOf(filePath))
            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

            Dim nCells As Integer = mesh.CellFaces.Count
            Dim nPts As Integer = mesh.Points.Count

            ' ---- 1. 四面体分解（同时给出每个子四面体的宿主单元） ----
            Dim host As Integer() = Nothing
            Dim tets As List(Of Integer()) = BuildTetrahedra(mesh, host)
            Dim nTets As Integer = tets.Count

            ' ---- 2. 点表 = 原网格点 + 每个单元形心 ----
            Dim coordArray As Double() = New Double((nPts + nCells) * 3 - 1) {}

            For i As Integer = 0 To nPts - 1
                Dim p As EulerianVertex = mesh.Points(i)

                coordArray(3 * i) = p.X
                coordArray(3 * i + 1) = p.Y
                coordArray(3 * i + 2) = p.Z
            Next

            For c As Integer = 0 To nCells - 1
                Dim cc As EulerianVertex = mesh.CellCentroids(c)
                Dim o As Integer = 3 * (nPts + c)

                coordArray(o) = cc.X
                coordArray(o + 1) = cc.Y
                coordArray(o + 2) = cc.Z
            Next

            ' ---- 3. 连接表与单元类型 ----
            Dim conn As Long() = New Long(nTets * 4 - 1) {}
            Dim offsets As Long() = New Long(nTets - 1) {}
            Dim types(nTets - 1) As Byte

            For i As Integer = 0 To nTets - 1
                Dim t As Integer() = tets(i)

                conn(4 * i) = t(0)
                conn(4 * i + 1) = t(1)
                conn(4 * i + 2) = t(2)
                conn(4 * i + 3) = t(3)

                offsets(i) = 4 * (i + 1)
                types(i) = VtkTetra
            Next

            ' ---- 4. 字段（单元 → 子四面体展开） ----
            Dim fieldList As New List(Of VtiField)

            If cellFields IsNot Nothing Then
                For Each f As VtiField In cellFields
                    If f IsNot Nothing Then
                        fieldList.Add(f)
                    End If
                Next
            End If

            ' ---- 5. 写出 ----
            Using stream As New FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16)
                Using w As New BinaryWriter(stream, New UTF8Encoding(False))
                    Dim blocks As New List(Of Byte())()
                    Dim blockOffsets As New List(Of Long)()

                    Call AppendBlock(blocks, blockOffsets, ToBytesF64(coordArray))
                    Call AppendBlock(blocks, blockOffsets, ToBytesI64(conn))
                    Call AppendBlock(blocks, blockOffsets, ToBytesI64(offsets))
                    Call AppendBlock(blocks, blockOffsets, types)

                    Dim fieldOffsetStart As Integer = blockOffsets.Count
                    Dim fieldBytes As New List(Of Byte())()

                    For i As Integer = 0 To fieldList.Count - 1
                        Dim bytes As Byte() = ExpandField(fieldList(i), host, nTets)
                        Dim off As Long = NextOffset(blocks)

                        Call AppendBlock(blocks, blockOffsets, bytes)
                        fieldBytes.Add(bytes)
                    Next

                    ' ---- XML ----
                    Dim scalars As String = Nothing

                    For Each f As VtiField In fieldList
                        If f.Components = 1 Then
                            scalars = f.Name
                            Exit For
                        End If
                    Next

                    Dim sb As New StringBuilder(4096)

                    sb.Append("<?xml version=""1.0""?>").Append(vbLf)
                    sb.Append("<VTKFile type=""UnstructuredGrid"" version=""0.1"" byte_order=""LittleEndian"" header_type=""UInt32"">").Append(vbLf)
                    sb.Append("  <UnstructuredGrid>").Append(vbLf)
                    sb.Append($"    <Piece NumberOfPoints=""{nPts + nCells}"" NumberOfCells=""{nTets}"">").Append(vbLf)
                    sb.Append("      <FieldData>").Append(vbLf)
                    sb.Append($"        <DataArray type=""Int32"" Name=""step"" NumberOfTuples=""1"" format=""ascii"">{stepIndex}</DataArray>").Append(vbLf)
                    sb.Append($"        <DataArray type=""Float64"" Name=""time"" NumberOfTuples=""1"" format=""ascii"">{time.ToString("R", CultureInfo.InvariantCulture)}</DataArray>").Append(vbLf)
                    sb.Append("      </FieldData>").Append(vbLf)
                    sb.Append("      <Points>").Append(vbLf)
                    sb.Append($"        <DataArray type=""Float64"" Name=""Points"" NumberOfComponents=""3"" format=""appended"" offset=""{blockOffsets(0)}""/>").Append(vbLf)
                    sb.Append("      </Points>").Append(vbLf)
                    sb.Append("      <Cells>").Append(vbLf)
                    sb.Append($"        <DataArray type=""Int64"" Name=""connectivity"" format=""appended"" offset=""{blockOffsets(1)}""/>").Append(vbLf)
                    sb.Append($"        <DataArray type=""Int64"" Name=""offsets"" format=""appended"" offset=""{blockOffsets(2)}""/>").Append(vbLf)
                    sb.Append($"        <DataArray type=""UInt8"" Name=""types"" format=""appended"" offset=""{blockOffsets(3)}""/>").Append(vbLf)
                    sb.Append("      </Cells>").Append(vbLf)

                    If fieldList.Count > 0 Then
                        sb.Append("      <CellData" &
                                  If(scalars IsNot Nothing, $" scalars=""{Escape(scalars)}""", "") & ">").Append(vbLf)

                        For i As Integer = 0 To fieldList.Count - 1
                            Dim f As VtiField = fieldList(i)
                            Dim vtkType As String = If(f.Type = VtiFieldType.UInt8, "UInt8", "Float32")

                            sb.Append($"        <DataArray type=""{vtkType}"" Name=""{Escape(f.Name)}"" " &
                                      $"NumberOfComponents=""{Math.Max(1, f.Components)}"" format=""appended"" " &
                                      $"offset=""{blockOffsets(fieldOffsetStart + i)}""/>").Append(vbLf)
                        Next

                        sb.Append("      </CellData>").Append(vbLf)
                    End If

                    sb.Append("    </Piece>").Append(vbLf)
                    sb.Append("  </UnstructuredGrid>").Append(vbLf)
                    sb.Append("  <AppendedData encoding=""raw"">").Append(vbLf)

                    Call w.Write(Encoding.ASCII.GetBytes(sb.ToString()))
                    Call w.Write(CByte(Asc("_"c)))

                    For Each block As Byte() In blocks
                        Call w.Write(CUInt(block.Length))
                        Call w.Write(block)
                    Next

                    Call w.Write(Encoding.ASCII.GetBytes(vbLf & "  </AppendedData>" & vbLf & "</VTKFile>" & vbLf))
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' 把多面体网格分解为四面体：每个面三角扇 + 单元形心。
        ''' 形心点索引 = 原网格点数 + 单元编号。
        ''' </summary>
        ''' <param name="mesh">非结构网格</param>
        ''' <param name="hostCell">输出：每个子四面体对应的宿主单元编号</param>
        Public Shared Function BuildTetrahedra(mesh As EulerianMesh, ByRef hostCell As Integer()) As List(Of Integer())
            Dim nPts As Integer = mesh.Points.Count
            Dim nCells As Integer = mesh.CellFaces.Count
            Dim tets As New List(Of Integer())(nCells * 6)
            Dim host As New List(Of Integer)(nCells * 6)

            For c As Integer = 0 To nCells - 1
                Dim cid As Integer = nPts + c

                For Each f As Integer In mesh.CellFaces(c)
                    Dim pi As List(Of Integer) = mesh.Faces(f).PointIndices
                    Dim nv As Integer = pi.Count

                    If nv < 3 Then
                        Continue For
                    End If

                    For t As Integer = 1 To nv - 2
                        tets.Add(New Integer() {pi(0), pi(t), pi(t + 1), cid})
                        host.Add(c)
                    Next
                Next
            Next

            hostCell = host.ToArray

            Return tets
        End Function

#Region "序列化辅助"

        Private Shared Function NextOffset(blocks As List(Of Byte())) As Long
            Dim off As Long = 0

            For Each b As Byte() In blocks
                off += 4 + b.Length
            Next

            Return off
        End Function

        Private Shared Sub AppendBlock(blocks As List(Of Byte()), offsets As List(Of Long), data As Byte())
            offsets.Add(NextOffset(blocks))
            blocks.Add(data)
        End Sub

        Private Shared Function ToBytesF64(a As Double()) As Byte()
            If a.Length = 0 Then Return New Byte(-1) {}

            Dim b(a.Length * 8 - 1) As Byte

            Buffer.BlockCopy(a, 0, b, 0, b.Length)
            Return b
        End Function

        Private Shared Function ToBytesI64(a As Long()) As Byte()
            If a.Length = 0 Then Return New Byte(-1) {}

            Dim b(a.Length * 8 - 1) As Byte

            Buffer.BlockCopy(a, 0, b, 0, b.Length)
            Return b
        End Function

        ''' <summary>
        ''' 把"逐单元"字段展开为"逐子四面体"的字节块（同一宿主单元的子四面体取同值）。
        ''' </summary>
        Private Shared Function ExpandField(f As VtiField, hostCell As Integer(), nTets As Integer) As Byte()
            Dim comps As Integer = Math.Max(1, f.Components)
            Dim nCells As Integer = If(hostCell.Length = 0, 0,
                                       EnumerableMax(hostCell) + 1)

            If f.Type = VtiFieldType.UInt8 Then
                Dim out8(nTets * comps - 1) As Byte

                For t As Integer = 0 To nTets - 1
                    Dim c As Integer = hostCell(t)

                    For k As Integer = 0 To comps - 1
                        out8(t * comps + k) = If(f.Data8 IsNot Nothing AndAlso f.Data8.Length > c * comps + k,
                                                 f.Data8(c * comps + k), CByte(0))
                    Next
                Next

                Return out8
            End If

            Dim out32(nTets * comps - 1) As Single

            If f.Data32 IsNot Nothing AndAlso f.Data32.Length >= nCells * comps Then
                For t As Integer = 0 To nTets - 1
                    Dim c As Integer = hostCell(t)

                    For k As Integer = 0 To comps - 1
                        out32(t * comps + k) = f.Data32(c * comps + k)
                    Next
                Next
            ElseIf f.Data64 IsNot Nothing Then
                For t As Integer = 0 To nTets - 1
                    Dim c As Integer = hostCell(t)

                    For k As Integer = 0 To comps - 1
                        Dim idx As Integer = c * comps + k

                        out32(t * comps + k) = If(idx < f.Data64.Length, CSng(f.Data64(idx)), 0.0F)
                    Next
                Next
            End If

            Return ToBytesF32(out32)
        End Function

        Private Shared Function ToBytesF32(a As Single()) As Byte()
            If a.Length = 0 Then Return New Byte(-1) {}

            Dim b(a.Length * 4 - 1) As Byte

            Buffer.BlockCopy(a, 0, b, 0, b.Length)
            Return b
        End Function

        Private Shared Function EnumerableMax(a As Integer()) As Integer
            Dim m As Integer = 0

            For i As Integer = 0 To a.Length - 1
                If a(i) > m Then m = a(i)
            Next

            Return m
        End Function

        Private Shared Function Escape(s As String) As String
            If s Is Nothing Then Return ""

            Return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;")
        End Function

#End Region

    End Class

End Namespace
