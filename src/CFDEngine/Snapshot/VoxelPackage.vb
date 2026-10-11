Imports System.IO
Imports System.IO.Compression
Imports System.Text.Json
Imports Moira.CFDEngine.Snapshot.JSON

' /********************************************************************************/
'
'   VoxelPackage.vb
'
'   二进制体素模型压缩包（zip）流式读写 —— 解决高分辨率体素模型
'   （数亿体素）在 JSON 序列化 Mask 大数组时的内存不足问题。
'
'   包内文件布局：
'       model.json —— GridInfo 元数据（不含 Mask 数组，序列化时为 Nothing）
'       model.vox  —— 掩膜原始字节流（Deflate 压缩，ZipArchiveEntry 自动处理）：
'                       byte = 1 表示活动体素（流体）
'                       byte = 0 表示空腔（固体障碍）
'                     长度 = Width * Height * Depth，
'                     索引顺序与 GridInfo.IndexOrder 一致（(x*Height + y)*Depth + z）
'
'   写入采用分块流式（WriteMaskChunk），读取同样按块流式（OpenMask /
'   ReadMaskChunk），或用 <see cref="LoadVoxelModel"/> 一步流式转换为
'   CFDEngine <see cref="VoxelModel"/>（True = 流体，与 byte=1 语义一致，
'   全程无 Integer() 中间数组）。
'
' /********************************************************************************/

Namespace Snapshot

    ''' <summary>
    ''' 二进制体素模型压缩包读写工具（zip：model.json + model.vox）。
    ''' </summary>
    Public Module VoxelPackage

        ''' <summary>包内元数据 json 条目名。</summary>
        Public Const ModelJsonEntry As String = "model.json"

        ''' <summary>包内掩膜字节流条目名。</summary>
        Public Const ModelVoxEntry As String = "model.vox"

        ''' <summary>掩膜流式读写的默认块大小（4 MB）。</summary>
        Public Const DefaultChunkSize As Integer = 4 * 1024 * 1024

        ''' <summary>
        ''' 便捷方法：加载 zip 体素包并流式转换为 CFDEngine 语义的
        ''' <see cref="VoxelModel"/>（True = 活动流体），全程无 Integer() 中间数组。
        ''' </summary>
        ''' <param name="zipPath">体素包（zip）文件路径</param>
        Public Function LoadVoxelModel(zipPath As String) As VoxelModel
            Using reader As New Reader(zipPath)
                Return reader.LoadVoxelModel(zipPath)
            End Using
        End Function

        ''' <summary>便捷方法：只读包内元数据（model.json，不含掩膜）。</summary>
        Public Function ReadMetadata(zipPath As String) As GridInfo
            Using reader As New Reader(zipPath)
                Return reader.Grid
            End Using
        End Function

        ''' <summary>
        ''' 从掩膜字节流便捷构建写端元数据（不含 Mask）。
        ''' </summary>
        Public Function BuildMetadata(grid As GridInfo) As GridInfo
            Return New GridInfo With {
                .Nx = grid.Nx, .Ny = grid.Ny, .Nz = grid.Nz,
                .Width = grid.Width, .Height = grid.Height, .Depth = grid.Depth,
                .Origin = grid.Origin, .Spacing = grid.Spacing,
                .TotalVoxels = grid.TotalVoxels, .ActiveVoxels = grid.ActiveVoxels,
                .IndexOrder = grid.IndexOrder
            }
        End Function

        ''' <summary>从流中读满 count 字节（或流提前结束），返回实际读取数。</summary>
        Friend Function ReadFull(stream As Stream, buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim total As Integer = 0

            While total < count
                Dim got As Integer = stream.Read(buffer, offset + total, count - total)

                If got <= 0 Then
                    Exit While
                End If
                total += got
            End While

            Return total
        End Function

        ''' <summary>
        ''' 写端：分块流式写入体素包。
        ''' 用法：New → WriteMetadata → OpenMask → WriteMaskChunk × n → Dispose。
        ''' </summary>
        Public NotInheritable Class Writer : Implements IDisposable

            Private ReadOnly _zip As ZipArchive
            Private _vox As Stream
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

            ''' <summary>
            ''' 写出元数据 model.json（传入的 grid 的 Mask 字段会被忽略，
            ''' 内部以副本序列化，不修改调用方对象）。
            ''' </summary>
            Public Sub WriteMetadata(grid As GridInfo)
                Using entryStream As Stream = _zip.CreateEntry(ModelJsonEntry).Open()
                    Using text As New StreamWriter(entryStream)
                        Call text.Write(JsonSerializer.Serialize(
                            BuildMetadata(grid),
                            New JsonSerializerOptions With {.WriteIndented = False}))
                    End Using
                End Using
            End Sub

            ''' <summary>打开（创建）掩膜字节流条目 model.vox，随后可分块写入。</summary>
            Public Sub OpenMask()
                If _vox IsNot Nothing Then
                    Throw New InvalidOperationException("model.vox 条目已打开")
                End If
                _vox = _zip.CreateEntry(ModelVoxEntry, CompressionLevel.Optimal).Open()
            End Sub

            ''' <summary>向 model.vox 追加一块掩膜字节（buffer[0..count)）。</summary>
            Public Sub WriteMaskChunk(buffer As Byte(), count As Integer)
                If _vox Is Nothing Then
                    Throw New InvalidOperationException("请先调用 OpenMask()")
                End If
                Call _vox.Write(buffer, 0, count)
            End Sub

            ''' <summary>收尾：关闭掩膜流（zip 条目在 Dispose 时最终落盘）。</summary>
            Public Sub Finish()
                If _vox IsNot Nothing Then
                    Call _vox.Dispose()
                    _vox = Nothing
                End If
                _finished = True
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Not _finished Then
                    Call Finish()
                End If
                Call _zip.Dispose()
            End Sub
        End Class

        ''' <summary>
        ''' 读端：按需流式读取体素包。
        ''' </summary>
        Public NotInheritable Class Reader : Implements IDisposable

            Private ReadOnly _zip As ZipArchive
            Private ReadOnly _voxEntry As ZipArchiveEntry

            ''' <summary>包内元数据（model.json；Mask 字段为 Nothing）。</summary>
            Public ReadOnly Property Grid As GridInfo

            ''' <summary>model.vox 条目的解压后字节长度（应等于 Width*Height*Depth）。</summary>
            Public ReadOnly Property MaskLength As Long
                Get
                    Return _voxEntry.Length
                End Get
            End Property

            ''' <summary>打开读端（从文件）。</summary>
            Public Sub New(filePath As String)
                _zip = ZipFile.OpenRead(filePath)
                _voxEntry = FindEntry(ModelVoxEntry)
                Grid = ParseMetadata()
            End Sub

            ''' <summary>打开读端（从任意输入流，调用方负责流生命周期）。</summary>
            Public Sub New(input As Stream, leaveOpen As Boolean)
                _zip = New ZipArchive(input, ZipArchiveMode.Read, leaveOpen)
                _voxEntry = FindEntry(ModelVoxEntry)
                Grid = ParseMetadata()
            End Sub

            ''' <summary>
            ''' 打开掩膜字节流（顺序读，不可随机访问；调用方负责 Dispose）。
            ''' </summary>
            Public Function OpenMask() As Stream
                Return _voxEntry.Open()
            End Function

            ''' <summary>一次性读出全部掩膜字节（长度 = <see cref="MaskLength"/>）。</summary>
            Public Function ReadMaskAll() As Byte()
                Dim buffer(CInt(MaskLength - 1)) As Byte

                Using s As Stream = OpenMask()
                    If ReadFull(s, buffer, 0, buffer.Length) <> buffer.Length Then
                        Throw New InvalidDataException("model.vox 数据提前结束")
                    End If
                End Using

                Return buffer
            End Function

            ''' <summary>
            ''' 流式转换为 CFDEngine 语义的 <see cref="VoxelModel"/>
            ''' （True = 活动流体 ⇔ byte = 1），全程块读取，无 Integer() 中间数组，
            ''' 并顺带统计固体体素数与固体包围盒。
            ''' </summary>
            ''' <param name="sourcePath">源包路径（VoxelModel.SourceModel 元数据记录用）</param>
            Public Function LoadVoxelModel(sourcePath As String) As VoxelModel
                Dim g As GridInfo = Grid

                If g Is Nothing Then
                    Throw New InvalidDataException("体素包缺少 model.json 元数据")
                End If

                Dim w = g.Width, h = g.Height, d = g.Depth
                Dim n As Long = CLng(w) * h * d

                If MaskLength <> n Then
                    Throw New InvalidDataException(
                        $"model.vox 长度 {MaskLength} 与网格 {w}x{h}x{d}={n} 不一致")
                End If

                Dim shape(CInt(n - 1)) As Boolean
                Dim chunk(DefaultChunkSize - 1) As Byte
                Dim solidCount As Long = 0
                Dim minX = Integer.MaxValue, minY = Integer.MaxValue, minZ = Integer.MaxValue
                Dim maxX = -1, maxY = -1, maxZ = -1
                Dim plane As Integer = h * d
                Dim filled As Long = 0

                Using s As Stream = OpenMask()
                    While filled < n
                        Dim want As Integer = CInt(Math.Min(CLng(chunk.Length), n - filled))
                        Dim got As Integer = ReadFull(s, chunk, 0, want)

                        If got <= 0 Then
                            Throw New InvalidDataException("model.vox 数据提前结束")
                        End If

                        For b As Integer = 0 To got - 1
                            Dim flat As Long = filled + b

                            If chunk(b) = 1 Then
                                ' byte = 1 → 活动流体（与 CFDEngine VoxelModel.Shape True 语义一致）
                                shape(CInt(flat)) = True
                            Else
                                ' byte = 0 → 固体障碍：统计固体数与固体包围盒
                                solidCount += 1L

                                Dim k As Integer = CInt(flat Mod d)
                                Dim remd As Integer = CInt(flat \ d)
                                Dim j As Integer = remd Mod h
                                Dim xx As Integer = remd \ h

                                If xx < minX Then minX = xx
                                If j < minY Then minY = j
                                If k < minZ Then minZ = k
                                If xx > maxX Then maxX = xx
                                If j > maxY Then maxY = j
                                If k > maxZ Then maxZ = k
                            End If
                        Next

                        filled += got
                    End While
                End Using

                If solidCount = 0 Then
                    minX = -1 : minY = -1 : minZ = -1
                End If

                Dim vs As Double = If(g.Spacing IsNot Nothing AndAlso g.Spacing.Length = 3,
                                      g.Spacing(0), 1.0)

                Return New VoxelModel With {
                    .Shape = New VoxelShape(w, h, d, shape),
                    .Width = w,
                    .Height = h,
                    .Depth = d,
                    .VoxelSize = {vs, vs, vs},
                    .SourceModel = If(String.IsNullOrEmpty(sourcePath), "", sourcePath),
                    .SolidVoxelCount = CInt(solidCount),
                    .SolidBounds = (minX, minY, minZ, maxX, maxY, maxZ)
                }
            End Function

            Private Function FindEntry(name As String) As ZipArchiveEntry
                Dim entry As ZipArchiveEntry = _zip.GetEntry(name)

                If entry Is Nothing Then
                    Throw New InvalidDataException($"体素包缺少条目 {name}")
                End If

                Return entry
            End Function

            Private Function ParseMetadata() As GridInfo
                Using entryStream As Stream = FindEntry(ModelJsonEntry).Open()
                    Dim grid As GridInfo = JsonSerializer.Deserialize(Of GridInfo)(entryStream)

                    If grid Is Nothing Then
                        Throw New InvalidDataException("model.json 解析失败")
                    End If

                    Return grid
                End Using
            End Function

            Public Sub Dispose() Implements IDisposable.Dispose
                Call _zip.Dispose()
            End Sub
        End Class

    End Module

End Namespace
