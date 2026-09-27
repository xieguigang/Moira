' /********************************************************************************/
'
'   CfdDataset.vb
'
'   CFD 快照数据集 —— metadata.json + 逐帧 .vti 的加载与缓存管理
'
'   作用：
'       解析 metadata.json（复用 CFDEngine.Snapshot.JSON.SnapshotMetadata 模型），
'       构建活动体素索引表；Frames 为空时按文件名排序扫描文件夹内 *.vti 补齐；
'       提供 LRU 帧缓存、按需字段加载与逐字段的全局值域统计。
'
'   ★ 适配"字段可配置"的 VTI 导出器：
'       新格式里不再固定 mask/pressure/density/velocity 四件套，而是任意
'       组合的标量场 + 多分量场（压力 / 温度 / pH / 各物种细胞数 / 代谢物
'       浓度 / 逐基因表达量……）。因此本数据集：
'           - 用**字段名（字符串）**而不是枚举来标识可视化标量场
'           - 只在需要时把指定字段从磁盘上取出来（GetFrame(index, wanted)），
'             避免每帧读几十 MB
'           - 每个字段的全局值域按需计算（EnsureRange），而不是加载时就全量扫描
'
' /********************************************************************************/

Imports System.IO
Imports System.Text.Json
Imports Moira.CFDEngine.Snapshot.JSON

Namespace Data

    ''' <summary>横截面轴。</summary>
    Public Enum CfdAxis
        X
        Y
        Z
    End Enum

    ''' <summary>帧元信息。</summary>
    Public Class CfdFrameMeta
        Public Property StepIndex As Integer
        Public Property Time As Double
        Public Property FilePath As String
    End Class

    ''' <summary>
    ''' 体素时间序列（时间点 + 对应场值）。
    ''' </summary>
    Public Class VoxelSeries
        Public Property Times As Double()
        Public Property Values As Double()
    End Class

    ''' <summary>
    ''' CFD 快照数据集：懒加载 + LRU 缓存 + 逐字段值域统计。
    ''' </summary>
    Public Class CfdDataset

        Const CacheCapacity As Integer = 12

        ReadOnly m_cache As New Dictionary(Of Integer, VtiFrameData)
        ReadOnly m_lru As New LinkedList(Of Integer)
        ReadOnly m_frames As New List(Of CfdFrameMeta)
        ReadOnly m_ranges As New Dictionary(Of String, Tuple(Of Double, Double))(StringComparer.OrdinalIgnoreCase)
        ReadOnly m_fileCache As New Dictionary(Of String, VtiFile)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>活动体素索引表（引擎序 i*ny*nz + j*nz + k，常数，跨帧复用）。</summary>
        Dim m_active As Integer()

        ''' <summary>全部活动体素质心（懒计算，加载后为常数）。</summary>
        Dim m_centroid As Double()

        ''' <summary>数据集中可用的标量场名称（首帧字段清单）。</summary>
        Dim m_fields As New List(Of String)

        Public Property Metadata As SnapshotMetadata

        Public Property Nx As Integer
        Public Property Ny As Integer
        Public Property Nz As Integer

        Public Property Origin As Double() = {0.0, 0.0, 0.0}
        Public Property Spacing As Double() = {1.0, 1.0, 1.0}

        ''' <summary>活动体素数量。</summary>
        Public ReadOnly Property ActiveVoxels As Integer
            Get
                Return If(m_active Is Nothing, 0, m_active.Length)
            End Get
        End Property

        ''' <summary>活动体素索引数组（只读复用，禁止外部修改）。</summary>
        Public ReadOnly Property ActiveIndices As Integer()
            Get
                Return m_active
            End Get
        End Property

        ''' <summary>可用标量场名称。</summary>
        Public ReadOnly Property FieldNames As String()
            Get
                Return m_fields.ToArray()
            End Get
        End Property

        ''' <summary>
        ''' 全部活动体素质心（世界坐标，常数）。渲染层据此做质心补偿，
        ''' 使任意可见子集重载点云后场景质心保持不变，避免模型跳动。
        ''' </summary>
        Public ReadOnly Property FullCentroid As Double()
            Get
                If m_centroid Is Nothing AndAlso m_active IsNot Nothing Then
                    Dim sx As Double = 0.0, sy As Double = 0.0, sz As Double = 0.0
                    Dim dx As Double = Spacing(0), dy As Double = Spacing(1), dz As Double = Spacing(2)

                    For Each idx As Integer In m_active
                        Dim i, j, k As Integer
                        Call IdxToIJK(idx, i, j, k)

                        sx += Origin(0) + (i + 0.5) * dx
                        sy += Origin(1) + (j + 0.5) * dy
                        sz += Origin(2) + (k + 0.5) * dz
                    Next

                    Dim n As Double = Math.Max(1, m_active.Length)
                    m_centroid = New Double() {sx / n, sy / n, sz / n}
                End If

                Return m_centroid
            End Get
        End Property

        Public Property Folder As String

        ''' <summary>帧数量。</summary>
        Public ReadOnly Property FrameCount As Integer
            Get
                Return m_frames.Count
            End Get
        End Property

        ''' <summary>某个标量场的全局值域被更新时触发（可能在后台线程触发）。</summary>
        Public Event RangeUpdated(field As String)

        ''' <summary>获取帧元信息。</summary>
        Public Function GetFrameMeta(index As Integer) As CfdFrameMeta
            If index < 0 OrElse index >= m_frames.Count Then
                Return Nothing
            End If
            Return m_frames(index)
        End Function

        ''' <summary>
        ''' 从文件夹加载数据集（metadata.json + *.vti）。
        ''' </summary>
        Public Shared Function Load(folder As String) As CfdDataset
            If Not Directory.Exists(folder) Then
                Throw New DirectoryNotFoundException($"数据文件夹不存在: {folder}")
            End If

            Dim metaFile As String = Nothing
            For Each f As String In Directory.EnumerateFiles(folder, "*.json")
                If String.Compare(Path.GetFileName(f), "metadata.json", True) = 0 Then
                    metaFile = f
                    Exit For
                End If
            Next

            If metaFile Is Nothing Then
                Throw New FileNotFoundException($"文件夹中未找到 metadata.json: {folder}")
            End If

            Dim meta = JsonSerializer.Deserialize(Of SnapshotMetadata)(File.ReadAllText(metaFile))
            If meta?.Grid Is Nothing Then
                Throw New InvalidDataException($"metadata.json 解析失败或缺少 Grid 段: {metaFile}")
            End If

            Dim data As New CfdDataset With {
                .Metadata = meta,
                .Folder = folder,
                .Nx = meta.Grid.Nx,
                .Ny = meta.Grid.Ny,
                .Nz = meta.Grid.Nz
            }

            If meta.Grid.Origin IsNot Nothing AndAlso meta.Grid.Origin.Length = 3 Then
                data.Origin = meta.Grid.Origin
            End If
            If meta.Grid.Spacing IsNot Nothing AndAlso meta.Grid.Spacing.Length = 3 Then
                data.Spacing = meta.Grid.Spacing
            End If

            ' ---- 活动体素索引表（引擎序，与 Mask 数组顺序一致）----
            Dim mask = meta.Grid.Mask
            Dim active As New List(Of Integer)(If(mask Is Nothing, 0, mask.Length))

            If mask IsNot Nothing Then
                For i As Integer = 0 To mask.Length - 1
                    If mask(i) <> 0 Then
                        active.Add(i)
                    End If
                Next
            End If

            data.m_active = active.ToArray()

            ' ---- 帧列表：Frames 为空时扫描 *.vti 按文件名排序补齐 ----
            Dim dt As Double = If(meta.Simulation?.TimeStep, 0.0)

            If meta.Frames IsNot Nothing AndAlso meta.Frames.Count > 0 Then
                For Each fr As FrameRef In meta.Frames
                    data.m_frames.Add(New CfdFrameMeta With {
                        .StepIndex = fr.StepIndex,
                        .Time = fr.Time,
                        .FilePath = Path.Combine(folder, fr.File)
                    })
                Next
            Else
                Dim files As String() = Directory.GetFiles(folder, "*.vti")
                Array.Sort(files, StringComparer.OrdinalIgnoreCase)

                For stepIndex As Integer = 0 To files.Length - 1
                    data.m_frames.Add(New CfdFrameMeta With {
                        .StepIndex = stepIndex,
                        .Time = If(dt > 0, stepIndex * dt, CDbl(stepIndex)),
                        .FilePath = files(stepIndex)
                    })
                Next
            End If

            If data.m_frames.Count = 0 Then
                Throw New InvalidDataException($"文件夹中没有可用的帧数据(*.vti): {folder}")
            End If

            ' ---- 用首帧的 XML 头得到可选标量场清单（不读取数据段）----
            Dim probe As VtiFile = data.OpenFile(0)

            If probe.Nx > 0 AndAlso probe.Ny > 0 AndAlso probe.Nz > 0 Then
                ' 网格以 .vti 的实际声明为准，避免 metadata 与实际不一致
                data.Nx = probe.Nx
                data.Ny = probe.Ny
                data.Nz = probe.Nz
            End If

            data.m_fields = probe.ListScalarFields()

            ' 保证金(?): 至少有一个可视化字段
            If data.m_fields.Count = 0 Then
                Throw New InvalidDataException($"帧文件中没有可用的标量场: {data.m_frames(0).FilePath}")
            End If

            Return data
        End Function

        ''' <summary>默认显示的标量场（优先 pressure）。</summary>
        Public Function DefaultField() As String
            For Each prefer In {"pressure", "speed", "temperature_c", "ph"}
                If m_fields.Contains(prefer, StringComparer.OrdinalIgnoreCase) Then
                    Return m_fields.First(Function(s) String.Equals(s, prefer, StringComparison.OrdinalIgnoreCase))
                End If
            Next
            Return m_fields(0)
        End Function

        Public Function HasField(name As String) As Boolean
            Return name IsNot Nothing AndAlso m_fields.Contains(name, StringComparer.OrdinalIgnoreCase)
        End Function

        ''' <summary>取得已解析过的 .vti 头部（带缓存）。</summary>
        Private Function OpenFile(index As Integer) As VtiFile
            Dim path As String = m_frames(index).FilePath
            Dim hit As VtiFile = Nothing

            If m_fileCache.TryGetValue(path, hit) Then
                Return hit
            End If

            hit = VtiFile.Parse(path)
            m_fileCache(path) = hit

            Return hit
        End Function

        ''' <summary>
        ''' 获取帧数据（懒加载 + LRU 缓存 + 按需字段）。
        ''' </summary>
        ''' <param name="index">帧序号</param>
        ''' <param name="wanted">需要加载的字段（为 Nothing 时加载全部标量场）</param>
        Public Function GetFrame(index As Integer, Optional wanted As IEnumerable(Of String) = Nothing) As VtiFrameData
            If index < 0 OrElse index >= m_frames.Count Then
                Return Nothing
            End If

            Dim file As VtiFile = OpenFile(index)

            SyncLock m_cache
                Dim cached As VtiFrameData = Nothing

                If m_cache.TryGetValue(index, cached) Then
                    ' 命中缓存：补齐还没加载过的字段后返回
                    Dim missing As List(Of String) = MissingFields(cached, wanted, file)

                    If missing.Count > 0 Then
                        Dim extra As VtiFrameData = file.ReadFrame(missing)

                        ' 原子替换字典引用：UI 线程可能正在枚举旧字典（悬停提示），
                        ' 就地写入会造成并发修改
                        Dim merged As New Dictionary(Of String, Single())(cached.Fields, StringComparer.OrdinalIgnoreCase)

                        For Each kv In extra.Fields
                            merged(kv.Key) = kv.Value
                        Next

                        cached.Fields = merged

                        If extra.Speed IsNot Nothing Then cached.Speed = extra.Speed

                        Call FillVelocityAlias(cached)
                    End If

                    m_lru.Remove(index)
                    m_lru.AddLast(index)

                    If missing.Count > 0 Then
                        Call UpdateRanges(cached)
                    End If

                    Return cached
                End If
            End SyncLock

            Dim fresh As VtiFrameData = file.ReadFrame(wanted)

            SyncLock m_cache
                m_cache(index) = fresh
                m_lru.AddLast(index)

                Do While m_lru.Count > CacheCapacity
                    Dim oldest = m_lru.First.Value
                    m_lru.RemoveFirst()
                    m_cache.Remove(oldest)
                Loop
            End SyncLock

            Call UpdateRanges(fresh)

            Return fresh
        End Function

        ''' <summary>列出缓存帧中还缺少的字段。</summary>
        Private Shared Function MissingFields(frame As VtiFrameData, wanted As IEnumerable(Of String), file As VtiFile) As List(Of String)
            Dim missing As New List(Of String)()

            If wanted Is Nothing Then
                Return missing
            End If

            For Each name As String In wanted
                If IsLoaded(frame, name, file) Then Continue For
                missing.Add(name)
            Next

            Return missing
        End Function

        ''' <summary>某个字段是否已经在帧里（多分量场检查其分量）。</summary>
        Private Shared Function IsLoaded(frame As VtiFrameData, name As String, file As VtiFile) As Boolean
            If frame.Fields.ContainsKey(name) Then Return True

            Dim arr As VtiArrayInfo = file.Find(name)

            If arr IsNot Nothing AndAlso arr.Components > 1 Then
                Return frame.Fields.ContainsKey($"{name}[0]")
            End If

            Return False
        End Function

        ''' <summary>由各分量补出 u/v/w/speed 引用。</summary>
        Private Shared Sub FillVelocityAlias(frame As VtiFrameData)
            If frame.U IsNot Nothing Then Return

            Dim u0 = frame.TryGet("velocity[0]")
            Dim v0 = frame.TryGet("velocity[1]")
            Dim w0 = frame.TryGet("velocity[2]")

            If u0 Is Nothing OrElse v0 Is Nothing OrElse w0 Is Nothing Then Return

            frame.U = u0
            frame.V = v0
            frame.W = w0

            Dim spd = frame.TryGet("speed")
            If spd Is Nothing Then
                Dim n As Integer = frame.Count
                spd = New Single(n - 1) {}

                For t As Integer = 0 To n - 1
                    Dim uu As Double = u0(t), vv As Double = v0(t), ww As Double = w0(t)
                    spd(t) = CSng(Math.Sqrt(uu * uu + vv * vv + ww * ww))
                Next

                frame.Fields("speed") = spd
            End If

            frame.Speed = spd
        End Sub

        ''' <summary>更新已加载字段的全局值域（渐进）。</summary>
        Private Sub UpdateRanges(frame As VtiFrameData)
            For Each kv In frame.Fields
                Call UpdateRange(kv.Key, kv.Value, frame.Mask)
            Next
        End Sub

        Private Sub UpdateRange(field As String, values As Single(), mask As Boolean())
            If values Is Nothing Then Return

            Dim r As Tuple(Of Double, Double) = Nothing

            SyncLock m_ranges
                If Not m_ranges.TryGetValue(field, r) Then r = Nothing
            End SyncLock

            Dim mn As Double, mx As Double

            If r Is Nothing Then
                mn = Double.PositiveInfinity
                mx = Double.NegativeInfinity
            Else
                mn = r.Item1
                mx = r.Item2
            End If

            For i As Integer = 0 To values.Length - 1
                If mask IsNot Nothing AndAlso Not mask(i) Then Continue For

                Dim x As Double = values(i)
                If x < mn Then mn = x
                If x > mx Then mx = x
            Next

            Dim changed As Boolean

            SyncLock m_ranges
                If Double.IsFinite(mn) AndAlso Double.IsFinite(mx) Then
                    If mn <= mx Then
                        If r Is Nothing OrElse mn < r.Item1 OrElse mx > r.Item2 Then
                            m_ranges(field) = New Tuple(Of Double, Double)(mn, mx)
                            changed = True
                        End If
                    End If
                End If
            End SyncLock

            If changed Then
                RaiseEvent RangeUpdated(field)
            End If
        End Sub

        ''' <summary>获取某标量场的当前已知值域。</summary>
        Public Function GetRange(field As String) As Tuple(Of Double, Double)
            If field Is Nothing Then
                Return New Tuple(Of Double, Double)(0.0, 1.0)
            End If

            SyncLock m_ranges
                Dim r As Tuple(Of Double, Double) = Nothing
                If m_ranges.TryGetValue(field, r) Then
                    Return r
                End If
            End SyncLock

            Return New Tuple(Of Double, Double)(0.0, 1.0)
        End Function

        Public Function HasRange(field As String) As Boolean
            SyncLock m_ranges
                Return m_ranges.ContainsKey(field)
            End SyncLock
        End Function

        ''' <summary>
        ''' 遍历全部帧求某字段的精确全局值域（只读取该字段；耗时较长，建议后台线程调用）。
        ''' </summary>
        Public Sub ComputeGlobalRange(field As String)
            If field Is Nothing Then Return

            SyncLock m_ranges
                If m_ranges.ContainsKey(field) Then Return
            End SyncLock

            Dim mn As Double = Double.PositiveInfinity
            Dim mx As Double = Double.NegativeInfinity

            For i As Integer = 0 To m_frames.Count - 1
                Dim frame = GetFrame(i, {field})

                If frame Is Nothing Then Continue For

                Dim values As Single() = frame.TryGet(field)
                If values Is Nothing Then Continue For

                Dim mask = frame.Mask

                For t As Integer = 0 To values.Length - 1
                    If mask IsNot Nothing AndAlso Not mask(t) Then Continue For

                    Dim x As Double = values(t)
                    If x < mn Then mn = x
                    If x > mx Then mx = x
                Next
            Next

            If Double.IsFinite(mn) AndAlso Double.IsFinite(mx) AndAlso mn <= mx Then
                Dim changed As Boolean

                SyncLock m_ranges
                    Dim r As Tuple(Of Double, Double) = Nothing
                    If Not m_ranges.TryGetValue(field, r) OrElse mn < r.Item1 OrElse mx > r.Item2 Then
                        m_ranges(field) = New Tuple(Of Double, Double)(mn, mx)
                        changed = True
                    End If
                End SyncLock

                If changed Then
                    RaiseEvent RangeUpdated(field)
                End If
            End If
        End Sub

        ''' <summary>
        ''' 提取某体素在某标量场下的时间序列（遍历全部帧）。
        ''' </summary>
        ''' <param name="idx">体素引擎索引（i*ny*nz + j*nz + k）</param>
        Public Function GetVoxelSeries(idx As Integer, field As String) As VoxelSeries
            Dim n As Integer = m_frames.Count
            Dim times(n - 1) As Double
            Dim values(n - 1) As Double

            For i As Integer = 0 To n - 1
                Dim frame = GetFrame(i, {field})

                times(i) = m_frames(i).Time

                If frame Is Nothing Then
                    values(i) = 0.0
                    Continue For
                End If

                values(i) = frame.ValueAt(field, idx)
            Next

            Return New VoxelSeries With {.Times = times, .Values = values}
        End Function

        ''' <summary>读取帧内某体素的速度分量（无速度场时为 0）。</summary>
        Public Shared Sub GetVelocity(frame As VtiFrameData, idx As Integer, ByRef u As Double, ByRef v As Double, ByRef w As Double)
            If frame.U Is Nothing Then
                u = 0.0 : v = 0.0 : w = 0.0
                Return
            End If

            u = frame.U(idx)
            v = frame.V(idx)
            w = frame.W(idx)
        End Sub

        ''' <summary>帧内某标量场在某体素上的值。</summary>
        Public Shared Function GetFieldValue(frame As VtiFrameData, field As String, idx As Integer) As Double
            Return frame.ValueAt(field, idx)
        End Function

        ''' <summary>引擎索引 → (i, j, k)，i*ny*nz + j*nz + k。</summary>
        Public Sub IdxToIJK(idx As Integer, ByRef i As Integer, ByRef j As Integer, ByRef k As Integer)
            Dim plane As Integer = Ny * Nz
            i = idx \ plane
            Dim rem1 As Integer = idx - i * plane
            j = rem1 \ Nz
            k = rem1 - j * Nz
        End Sub

        ''' <summary>(i, j, k) → 引擎索引。</summary>
        Public Function IJKToIdx(i As Integer, j As Integer, k As Integer) As Integer
            Return i * Ny * Nz + j * Nz + k
        End Function

        ''' <summary>体素中心的世界坐标。</summary>
        Public Function VoxelCenter(idx As Integer) As Double()
            Dim i, j, k As Integer
            Call IdxToIJK(idx, i, j, k)

            Return New Double() {
                Origin(0) + (i + 0.5) * Spacing(0),
                Origin(1) + (j + 0.5) * Spacing(1),
                Origin(2) + (k + 0.5) * Spacing(2)
            }
        End Function

    End Class

End Namespace
