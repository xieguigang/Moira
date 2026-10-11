' /********************************************************************************/
'
'   EulerianSnapshotRecorder.vb
'
'   非结构网格（Eulerian FVM）快照体系 —— 与既有体素快照体系并行、互不干扰。
'
'   为什么需要新接口而不是复用 ISnapshotRecorder：
'       ISnapshotRecorder.Capture(field As FluidField, ...) 是**体素导向**的
'       （FluidField = 结构化 VoxelShape + 单精度场）。Eulerian 求解器的结果是
'       "单元场 + 多面体网格"，与 FluidField 不同构。强行塞进同一接口会污染
'       既有体素链路（CFD_win32 / FluidSim / 浏览器端 VTK.js 消费方）。
'       因此新增：
'           IEulerianSnapshotRecorder   非结构网格记录器接口
'           SnapshotCollectionWriter    animation.pvd + frames.json + metadata.json
'                                       的共享写出器（新代码，不改动任何既有类型）
'           EulerianSnapshotRecorder    逐帧写 frame_*.vtu
'           EulerianToCartesianResampler  把 Eulerian 单元值重采样到结构笛卡尔格
'           EulerianVtiRecorder         在结构格上输出真正的 .vti 动画序列
'
'   输出目录结构（与 .vti 序列保持一致的消费方式：ParaView 打开 animation.pvd）：
'       outputDir/
'         ├── frame_0000.vtu        （或 frame_0000.vti，取决于记录器）
'         ├── ...
'         ├── animation.pvd         （ParaView 时间集合）
'         ├── frames.json           （浏览器端轻量帧清单）
'         └── metadata.json         （非结构网格元数据 + 求解配置，可选）
'
'   混合精度契约：导出时降为 float32（可视化用），不参与求解。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports Moira.CFDEngine.Eulerian

Namespace Snapshot

#Region "接口"

    ''' <summary>非结构 Eulerian 网格快照记录器接口。</summary>
    Public Interface IEulerianSnapshotRecorder

        ''' <summary>输出目录。</summary>
        ReadOnly Property OutputDir As String

        ''' <summary>已写出帧数。</summary>
        ReadOnly Property FrameCount As Integer

        ''' <summary>
        ''' 捕获一帧。
        ''' </summary>
        ''' <param name="mesh">当前网格（拓扑不变）</param>
        ''' <param name="cellFields">逐单元字段（单元主序）</param>
        ''' <param name="stepIndex">时间步序号</param>
        ''' <param name="time">模拟时间</param>
        Sub Capture(mesh As EulerianMesh, cellFields As IEnumerable(Of VtiField), stepIndex As Integer, time As Double)

        ''' <summary>收尾：写出 animation.pvd / frames.json / metadata.json。</summary>
        Sub Finish()
    End Interface

#End Region

#Region "非结构网格元数据"

    ''' <summary>非结构网格快照元数据（用于 metadata.json）。</summary>
    Public Class EulerianSnapshotMetadata

        Public Property Solver As String = ""
        Public Property CaseName As String = ""
        Public Property CellCount As Integer
        Public Property FaceCount As Integer
        Public Property PointCount As Integer
        Public Property TotalVolume As Double
        Public Property StepCount As Integer
        Public Property Time As Double
        Public Property Dt As Double

        ''' <summary>附加说明（网格来源、湍流模型、单位等）。</summary>
        Public ReadOnly Property Notes As New List(Of String)()

        Friend Function ToJson() As String
            Dim sb As New StringBuilder(512)

            sb.Append("{").Append(vbLf)
            sb.Append($"  ""kind"": ""eulerian-unstructured"",").Append(vbLf)
            sb.Append($"  ""solver"": ""{Escape(Solver)}"",").Append(vbLf)
            sb.Append($"  ""case"": ""{Escape(CaseName)}"",").Append(vbLf)
            sb.Append($"  ""cells"": {CellCount},").Append(vbLf)
            sb.Append($"  ""faces"": {FaceCount},").Append(vbLf)
            sb.Append($"  ""points"": {PointCount},").Append(vbLf)
            sb.Append($"  ""totalVolume"": {TotalVolume.ToString("R", CultureInfo.InvariantCulture)},").Append(vbLf)
            sb.Append($"  ""steps"": {StepCount},").Append(vbLf)
            sb.Append($"  ""time"": {Time.ToString("R", CultureInfo.InvariantCulture)},").Append(vbLf)
            sb.Append($"  ""dt"": {Dt.ToString("R", CultureInfo.InvariantCulture)},").Append(vbLf)
            sb.Append("  ""notes"": [").Append(vbLf)

            For i As Integer = 0 To Notes.Count - 1
                sb.Append($"    ""{Escape(Notes(i))}""").Append(If(i = Notes.Count - 1, "", ",")).Append(vbLf)
            Next

            sb.Append("  ]").Append(vbLf)
            sb.Append("}").Append(vbLf)

            Return sb.ToString()
        End Function

        Friend Shared Function Escape(s As String) As String
            If s Is Nothing Then Return ""

            Return s.Replace("\", "\\").Replace("""", "\""").Replace(vbCr, " ").Replace(vbLf, " ")
        End Function

    End Class

#End Region

#Region "共享写出器"

    ''' <summary>
    ''' 帧集合写出器：animation.pvd + frames.json（+ 可选 metadata.json）。
    ''' 与既有 <c>VtiSnapshotRecorder</c> 的输出约定逐字一致，使下游（ParaView /
    ''' 浏览器端 VTK.js / CfdDataset）无需区分数据来源。
    ''' </summary>
    Public Class SnapshotCollectionWriter

        Public ReadOnly Property OutputDir As String
        Public ReadOnly Property PvdName As String
        Public ReadOnly Property BaseName As String

        ''' <summary>一帧的轻量元数据。</summary>
        Private Class FrameEntry
            Public Property StepIndex As Integer
            Public Property Time As Double
            Public Property FileName As String
        End Class

        Private ReadOnly _extension As String
        Private ReadOnly _frames As New List(Of FrameEntry)()

        Public Sub New(outputDir As String,
                       Optional baseName As String = "frame",
                       Optional extension As String = "vtu",
                       Optional pvdName As String = "animation.pvd")

            Me.OutputDir = outputDir
            Me.BaseName = baseName
            Me._extension = If(extension, "vtu")
            Me.PvdName = pvdName

            Call Directory.CreateDirectory(outputDir)
        End Sub

        ''' <summary>已写出帧数。</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return _frames.Count
            End Get
        End Property

        ''' <summary>登记一帧（返回相对文件名）。</summary>
        Public Function AddFrame(stepIndex As Integer, time As Double) As String
            Dim name As String = BaseName & "_" & _frames.Count.ToString().PadLeft(4, "0"c) & "." & _extension

            _frames.Add(New FrameEntry With {.StepIndex = stepIndex, .Time = time, .FileName = name})

            Return name
        End Function

        ''' <summary>写出 animation.pvd / frames.json（+ metadata.json）。</summary>
        Public Sub Write(Optional gridInfo As String = Nothing,
                         Optional fields As String = "velocity,pressure",
                         Optional metadataJson As String = Nothing)

            Dim pvdPath As String = Path.Combine(OutputDir, PvdName)

            Using writer As New StreamWriter(pvdPath, False, New UTF8Encoding(False))
                writer.WriteLine("<?xml version=""1.0""?>")
                writer.WriteLine("<VTKFile type=""Collection"" version=""0.1"" byte_order=""LittleEndian"">")
                writer.WriteLine("  <Collection>")

                For Each frame In _frames
                    writer.WriteLine("    <DataSet timestep=""{0}"" group="""" part=""0"" file=""{1}""/>",
                                     frame.Time.ToString("R", CultureInfo.InvariantCulture), frame.FileName)
                Next

                writer.WriteLine("  </Collection>")
                writer.WriteLine("</VTKFile>")
            End Using

            Using writer As New StreamWriter(Path.Combine(OutputDir, "frames.json"), False, New UTF8Encoding(False))
                writer.WriteLine("{")
                writer.WriteLine($"  ""format"": ""{_extension}"",")
                writer.WriteLine($"  ""grid"": {If(gridInfo, "null")},")
                writer.WriteLine($"  ""fields"": [""{fields.Replace(",", """,""")}""],")
                writer.WriteLine("  ""frames"": [")

                For i As Integer = 0 To _frames.Count - 1
                    Dim f = _frames(i)
                    Dim comma As String = If(i = _frames.Count - 1, "", ",")

                    writer.WriteLine("    {{ ""step"": {0}, ""time"": {1}, ""file"": ""{2}"" }}{3}",
                                     f.StepIndex, f.Time.ToString("R", CultureInfo.InvariantCulture), f.FileName, comma)
                Next

                writer.WriteLine("  ]")
                writer.WriteLine("}")
            End Using

            If metadataJson IsNot Nothing Then
                File.WriteAllText(Path.Combine(OutputDir, "metadata.json"), metadataJson, New UTF8Encoding(False))
            End If
        End Sub

    End Class

#End Region

#Region "VTU 记录器"

    ''' <summary>逐帧写出 <c>frame_*.vtu</c>（非结构多面体网格）。</summary>
    Public Class EulerianSnapshotRecorder
        Implements IEulerianSnapshotRecorder

        Public ReadOnly Property OutputDir As String Implements IEulerianSnapshotRecorder.OutputDir

        ''' <summary>采样间隔（每隔多少步捕获一帧）。</summary>
        Public ReadOnly Property Interval As Integer

        ''' <summary>帧清单写出器。</summary>
        Public ReadOnly Property Writer As SnapshotCollectionWriter

        ''' <summary>元数据（Nothing = 不写 metadata.json）。</summary>
        Public Property Metadata As EulerianSnapshotMetadata

        Public ReadOnly Property FrameCount As Integer Implements IEulerianSnapshotRecorder.FrameCount
            Get
                Return Writer.Count
            End Get
        End Property

        ''' <summary>最近一帧的字段名（用于 frames.json）。</summary>
        Public Property FieldNames As String = "velocity,pressure"

        ''' <summary>最近一帧的网格规模（用于 frames.json 的 grid 字段）。</summary>
        Public Property GridInfo As String = Nothing

        Public Sub New(outputDir As String,
                       Optional interval As Integer = 1,
                       Optional baseName As String = "frame",
                       Optional pvdName As String = "animation.pvd",
                       Optional metadata As EulerianSnapshotMetadata = Nothing)

            Me.OutputDir = outputDir
            Me.Interval = Math.Max(1, interval)
            Me.Writer = New SnapshotCollectionWriter(outputDir, baseName, "vtu", pvdName)
            Me.Metadata = metadata
        End Sub

        Public Sub Capture(mesh As EulerianMesh, cellFields As IEnumerable(Of VtiField),
                           stepIndex As Integer, time As Double) Implements IEulerianSnapshotRecorder.Capture

            If Interval > 1 AndAlso (stepIndex Mod Interval) <> 0 Then
                Return
            End If

            Dim name As String = Writer.AddFrame(stepIndex, time)

            Call VTUExporter.Export(Path.Combine(OutputDir, name), mesh, cellFields, stepIndex, time)

            Dim names As New List(Of String)

            If cellFields IsNot Nothing Then
                For Each f As VtiField In cellFields
                    If f IsNot Nothing Then
                        names.Add(f.Name)
                    End If
                Next
            End If

            If names.Count > 0 Then
                FieldNames = String.Join(",", names)
            End If

            GridInfo = $"{{ ""cells"": {mesh.CellCount}, ""points"": {mesh.PointCount}, ""mesh"": ""unstructured"" }}"

            If Metadata IsNot Nothing Then
                Metadata.CellCount = mesh.CellCount
                Metadata.FaceCount = mesh.FaceCount
                Metadata.PointCount = mesh.PointCount
                Metadata.TotalVolume = mesh.TotalVolume
            End If
        End Sub

        Public Sub Finish() Implements IEulerianSnapshotRecorder.Finish
            If Metadata IsNot Nothing Then
                Metadata.StepCount = FrameCount
                Call Writer.Write(GridInfo, FieldNames, Metadata.ToJson())
            Else
                Call Writer.Write(GridInfo, FieldNames)
            End If
        End Sub

    End Class

#End Region

#Region "结构笛卡尔格重采样（盒式域 → 真正的 .vti 序列）"

    ''' <summary>逐单元场取值委托。</summary>
    Public Delegate Function EulerianFieldValueProvider() As Double()

    ''' <summary>
    ''' <see cref="EulerianVtiRecorder"/> 的字段规格：名称 / 分量数 / 逐单元取值委托。
    ''' </summary>
    Public Class EulerianVtiFieldSpec

        Public Property Name As String = ""

        Public Property Components As Integer = 1

        Public Property Provider As EulerianFieldValueProvider

    End Class

    ''' <summary>
    ''' 把非结构（Eulerian）单元值重采样到结构笛卡尔格上。
    ''' 适用场景：风洞等**盒式计算域**——用户既可以用 .vtu 看细节，
    ''' 也可以得到与既有体素链路完全一致的 .vti 动画序列
    ''' （ParaView / 浏览器端 VTK.js 直接可读）。
    '''
    ''' 采样策略：最近单元中心（零阶）——对分片常数单元场是**精确**的
    ''' （不引入插值误差），代价是单元边界呈阶梯状。
    ''' </summary>
    Public Class EulerianToCartesianResampler

        Public ReadOnly Property Mesh As EulerianMesh

        ''' <summary>结构格原点。</summary>
        Public Property OriginX As Double = 0
        Public Property OriginY As Double = 0
        Public Property OriginZ As Double = 0

        ''' <summary>结构格间距。</summary>
        Public Property SpacingX As Double = 1
        Public Property SpacingY As Double = 1
        Public Property SpacingZ As Double = 1

        ''' <summary>结构格分辨率。</summary>
        Public Property Nx As Integer = 1
        Public Property Ny As Integer = 1
        Public Property Nz As Integer = 1

        Private ReadOnly _binIndex As Dictionary(Of Long, List(Of Integer))
        Private ReadOnly _binSize As Double

        Public Sub New(mesh As EulerianMesh)
            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

            Me.Mesh = mesh

            ' 分箱尺度取"平均单元尺度"（与结构格间距无关，避免构造顺序依赖）
            _binSize = Math.Pow(Math.Max(mesh.TotalVolume, 1.0E-300) / Math.Max(1, mesh.CellCount), 1.0 / 3.0)

            If _binSize <= 0 Then _binSize = 1

            _binIndex = BuildBins(mesh, _binSize)
        End Sub

        ''' <summary>由网格包围盒自动设定结构格。</summary>
        Public Sub AutoConfigure(Optional cellSize As Double = 0, Optional maxCells As Integer = 4000000)
            Dim bmin As EulerianVertex = Nothing
            Dim bmax As EulerianVertex = Nothing

            Call Mesh.GetBounds(bmin, bmax)

            Dim extent As EulerianVertex = bmax - bmin
            Dim h As Double = cellSize

            If h <= 0 Then
                h = Math.Pow(Math.Max(Mesh.TotalVolume, 1.0E-300) / Math.Max(1, Mesh.CellCount), 1.0 / 3.0)
            End If

            If h <= 0 Then h = Math.Max(Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) / 64.0, 1.0E-6)

            Dim nx As Integer = Math.Max(1, CInt(Math.Floor(extent.X / h)) + 1)
            Dim ny As Integer = Math.Max(1, CInt(Math.Floor(extent.Y / h)) + 1)
            Dim nz As Integer = Math.Max(1, CInt(Math.Floor(extent.Z / h)) + 1)

            While CLng(nx) * CLng(ny) * CLng(nz) > maxCells
                h *= 2.0
                nx = Math.Max(1, CInt(Math.Floor(extent.X / h)) + 1)
                ny = Math.Max(1, CInt(Math.Floor(extent.Y / h)) + 1)
                nz = Math.Max(1, CInt(Math.Floor(extent.Z / h)) + 1)
            End While

            OriginX = bmin.X : OriginY = bmin.Y : OriginZ = bmin.Z
            SpacingX = h : SpacingY = h : SpacingZ = h
            Nx = nx : Ny = ny : Nz = nz
        End Sub

        ''' <summary>结构格单元数。</summary>
        Public ReadOnly Property CellCount As Integer
            Get
                Return Nx * Ny * Nz
            End Get
        End Property

        ''' <summary>
        ''' 把一个逐单元字段重采样为结构格（**引擎布局** idx = i*(ny*nz) + j*nz + k，
        ''' 与 VTIExporter 的输入约定一致）。
        ''' </summary>
        ''' <param name="values">逐单元值（单元主序）</param>
        ''' <param name="components">分量数</param>
        Public Function Resample(values As Double(), components As Integer) As Single()
            Dim n As Integer = CellCount
            Dim out(n * components - 1) As Single

            If values Is Nothing Then Return out

            For i As Integer = 0 To Nx - 1
                Dim x As Double = OriginX + i * SpacingX

                For j As Integer = 0 To Ny - 1
                    Dim y As Double = OriginY + j * SpacingY

                    For k As Integer = 0 To Nz - 1
                        Dim z As Double = OriginZ + k * SpacingZ
                        Dim c As Integer = NearestCell(x, y, z)
                        Dim idx As Integer = (i * Ny + j) * Nz + k

                        For comp As Integer = 0 To components - 1
                            Dim src As Integer = c * components + comp

                            out(idx * components + comp) =
                                If(c >= 0 AndAlso src < values.Length, CSng(values(src)), 0.0F)
                        Next
                    Next
                Next
            Next

            Return out
        End Function

        ''' <summary>最近单元编号（未找到返回 −1）。</summary>
        Public Function NearestCell(x As Double, y As Double, z As Double) As Integer
            Dim bx As Integer = CInt(Math.Floor((x - OriginX) / _binSize))
            Dim by As Integer = CInt(Math.Floor((y - OriginY) / _binSize))
            Dim bz As Integer = CInt(Math.Floor((z - OriginZ) / _binSize))
            Dim best As Integer = -1
            Dim bestD As Double = Double.MaxValue

            For ring As Integer = 0 To 2
                For dx As Integer = -ring To ring
                    For dy As Integer = -ring To ring
                        For dz As Integer = -ring To ring
                            If Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) <> ring Then
                                Continue For
                            End If

                            Dim bucket As List(Of Integer) = Nothing

                            If Not _binIndex.TryGetValue(BinKey(bx + dx, by + dy, bz + dz), bucket) Then
                                Continue For
                            End If

                            For Each c As Integer In bucket
                                Dim cc As EulerianVertex = Mesh.CellCentroids(c)
                                Dim ddx As Double = cc.X - x
                                Dim ddy As Double = cc.Y - y
                                Dim ddz As Double = cc.Z - z
                                Dim d As Double = ddx * ddx + ddy * ddy + ddz * ddz

                                If d < bestD Then
                                    bestD = d
                                    best = c
                                End If
                            Next
                        Next
                    Next
                Next

                If best >= 0 Then Exit For
            Next

            Return best
        End Function

        Private Shared Function BuildBins(mesh As EulerianMesh, h As Double) As Dictionary(Of Long, List(Of Integer))
            Dim bins As New Dictionary(Of Long, List(Of Integer))(mesh.CellCount * 2)

            For c As Integer = 0 To mesh.CellCount - 1
                Dim cc As EulerianVertex = mesh.CellCentroids(c)
                Dim key As Long = BinKey(CInt(Math.Floor(cc.X / h)), CInt(Math.Floor(cc.Y / h)), CInt(Math.Floor(cc.Z / h)))
                Dim bucket As List(Of Integer) = Nothing

                If Not bins.TryGetValue(key, bucket) Then
                    bucket = New List(Of Integer)
                    bins(key) = bucket
                End If

                bucket.Add(c)
            Next

            Return bins
        End Function

        Private Shared Function BinKey(ix As Integer, iy As Integer, iz As Integer) As Long
            Return (CLng(ix And &HFFFFF) << 40) Or (CLng(iy And &HFFFFF) << 20) Or CLng(iz And &HFFFFF)
        End Function

    End Class

    ''' <summary>
    ''' 基于重采样器的 .vti 记录器：输出与既有体素链路一致的
    ''' frame_*.vti + animation.pvd + frames.json 动画序列。
    ''' </summary>
    Public Class EulerianVtiRecorder
        Implements IEulerianSnapshotRecorder

        Public ReadOnly Property OutputDir As String Implements IEulerianSnapshotRecorder.OutputDir
        Public ReadOnly Property Interval As Integer
        Public ReadOnly Property Resampler As EulerianToCartesianResampler
        Public ReadOnly Property Writer As SnapshotCollectionWriter

        ''' <summary>需要输出的字段（名称 / 分量数 / 逐单元值提供者）。</summary>
        Public ReadOnly Property Fields As New List(Of EulerianVtiFieldSpec)()

        Public ReadOnly Property FrameCount As Integer Implements IEulerianSnapshotRecorder.FrameCount
            Get
                Return Writer.Count
            End Get
        End Property

        Public Sub New(resampler As EulerianToCartesianResampler,
                       outputDir As String,
                       Optional interval As Integer = 1)

            If resampler Is Nothing Then Throw New ArgumentNullException(NameOf(resampler))

            Me.Resampler = resampler
            Me.OutputDir = outputDir
            Me.Interval = Math.Max(1, interval)
            Me.Writer = New SnapshotCollectionWriter(outputDir, "frame", "vti", "animation.pvd")
        End Sub

        ''' <summary>登记一个要输出的标量 / 矢量字段。</summary>
        Public Sub AddField(name As String, components As Integer, provider As EulerianFieldValueProvider)
            Fields.Add(New EulerianVtiFieldSpec With {
                .Name = name,
                .Components = Math.Max(1, components),
                .Provider = provider})
        End Sub

        Public Sub Capture(mesh As EulerianMesh, cellFields As IEnumerable(Of VtiField),
                           stepIndex As Integer, time As Double) Implements IEulerianSnapshotRecorder.Capture

            If Interval > 1 AndAlso (stepIndex Mod Interval) <> 0 Then
                Return
            End If

            Dim outFields As New List(Of VtiField)

            For Each f As EulerianVtiFieldSpec In Fields
                Dim values As Double() = f.Provider.Invoke()

                If values Is Nothing Then
                    Continue For
                End If

                Dim sampled As Single() = Resampler.Resample(values, f.Components)

                outFields.Add(New VtiField With {
                    .Name = f.Name,
                    .Type = VtiFieldType.Float32,
                    .Components = f.Components,
                    .Data32 = sampled})
            Next

            ' 若调用方直接给了逐单元字段，则透传重采样（无需 Fields 登记）
            If outFields.Count = 0 AndAlso cellFields IsNot Nothing Then
                For Each f As VtiField In cellFields
                    If f Is Nothing Then Continue For

                    Dim src As Double() = Nothing

                    If f.Data32 IsNot Nothing Then
                        src = New Double(f.Data32.Length - 1) {}

                        For i As Integer = 0 To src.Length - 1
                            src(i) = f.Data32(i)
                        Next
                    ElseIf f.Data64 IsNot Nothing Then
                        src = f.Data64
                    End If

                    If src Is Nothing Then Continue For

                    outFields.Add(New VtiField With {
                        .Name = f.Name,
                        .Type = VtiFieldType.Float32,
                        .Components = Math.Max(1, f.Components),
                        .Data32 = Resampler.Resample(src, Math.Max(1, f.Components))})
                Next
            End If

            Dim fileName As String = Writer.AddFrame(stepIndex, time)

            Call VTIExporter.Export(Path.Combine(OutputDir, fileName),
                                    Resampler.Nx, Resampler.Ny, Resampler.Nz,
                                    outFields, Nothing, stepIndex, time, False)
        End Sub

        Public Sub Finish() Implements IEulerianSnapshotRecorder.Finish
            Dim grid As String = $"{{ ""nx"": {Resampler.Nx}, ""ny"": {Resampler.Ny}, ""nz"": {Resampler.Nz}, " &
                                 $"""origin"": [{Resampler.OriginX.ToString("R", CultureInfo.InvariantCulture)}, " &
                                 $"{Resampler.OriginY.ToString("R", CultureInfo.InvariantCulture)}, " &
                                 $"{Resampler.OriginZ.ToString("R", CultureInfo.InvariantCulture)}], " &
                                 $"""spacing"": {Resampler.SpacingX.ToString("R", CultureInfo.InvariantCulture)} }}"

            Dim names As New List(Of String)

            For Each f As EulerianVtiFieldSpec In Fields
                names.Add(f.Name)
            Next

            Call Writer.Write(grid, If(names.Count > 0, String.Join(",", names), "velocity,pressure"))
        End Sub

    End Class

#End Region

End Namespace
