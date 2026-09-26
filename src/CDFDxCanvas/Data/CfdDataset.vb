' /********************************************************************************/
'
'   CfdDataset.vb
'
'   CFD 快照数据集 —— metadata.json + 逐帧 .vti 的加载与缓存管理
'
'   作用：
'       解析 metadata.json（复用 CFDEngine.Snapshot.JSON.SnapshotMetadata 模型），
'       构建活动体素索引表；Frames 为空时按文件名排序扫描文件夹内 *.vti 补齐；
'       提供 LRU 帧缓存（默认 12 帧）、全局值域渐进统计与体素时间序列提取。
'
' /********************************************************************************/

Imports System.IO
Imports System.Text.Json
Imports Moira.CFDEngine.Snapshot.JSON

Namespace Data

    ''' <summary>标量场类型。</summary>
    Public Enum CfdField
        Pressure
        Density
        Speed
    End Enum

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
    ''' CFD 快照数据集：懒加载 + LRU 缓存 + 全局值域渐进统计。
    ''' </summary>
    Public Class CfdDataset

        Const CacheCapacity As Integer = 12

        ReadOnly m_cache As New Dictionary(Of Integer, VtiFrameData)
        ReadOnly m_lru As New LinkedList(Of Integer)
        ReadOnly m_frames As New List(Of CfdFrameMeta)
        ReadOnly m_ranges As New Dictionary(Of CfdField, Tuple(Of Double, Double))

        ''' <summary>活动体素索引表（引擎序 i*ny*nz + j*nz + k，常数，跨帧复用）。</summary>
        Dim m_active As Integer()

        ''' <summary>全部活动体素质心（懒计算，加载后为常数）。</summary>
        Dim m_centroid As Double()

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

        ''' <summary>全局值域随帧加载渐进更新时触发（可能在后台线程触发）。</summary>
        Public Event RangeUpdated(field As CfdField)

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
            Dim plane As Integer = data.Ny * data.Nz

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

            ' ---- 用第一帧种子化全局值域 ----
            data.UpdateRanges(data.GetFrame(0))

            Return data
        End Function

        ''' <summary>
        ''' 获取帧数据（懒加载 + LRU 缓存）。
        ''' </summary>
        Public Function GetFrame(index As Integer) As VtiFrameData
            If index < 0 OrElse index >= m_frames.Count Then
                Return Nothing
            End If

            SyncLock m_cache
                If m_cache.ContainsKey(index) Then
                    ' 刷新 LRU 顺序
                    m_lru.Remove(index)
                    m_lru.AddLast(index)
                    Return m_cache(index)
                End If
            End SyncLock

            Dim frame As VtiFrameData = VtiReader.Load(m_frames(index).FilePath)

            SyncLock m_cache
                m_cache(index) = frame
                m_lru.AddLast(index)

                Do While m_lru.Count > CacheCapacity
                    Dim oldest = m_lru.First.Value
                    m_lru.RemoveFirst()
                    m_cache.Remove(oldest)
                Loop
            End SyncLock

            Call UpdateRanges(frame)

            Return frame
        End Function

        ''' <summary>更新全局值域（渐进，与网页版行为一致）。</summary>
        Private Sub UpdateRanges(frame As VtiFrameData)
            Call UpdateRange(CfdField.Pressure, frame.Pressure, frame.Mask)
            Call UpdateRange(CfdField.Density, frame.Density, frame.Mask)
            Call UpdateRange(CfdField.Speed, frame.Speed, frame.Mask)
        End Sub

        Private Sub UpdateRange(field As CfdField, values As Single(), mask As Boolean())
            Dim r As Tuple(Of Double, Double) = Nothing

            SyncLock m_ranges
                If Not m_ranges.TryGetValue(field, r) Then
                    r = Nothing
                End If
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
                If mask IsNot Nothing AndAlso Not mask(i) Then
                    Continue For
                End If

                Dim x As Double = values(i)
                If x < mn Then mn = x
                If x > mx Then mx = x
            Next

            Dim changed As Boolean

            SyncLock m_ranges
                If Double.IsFinite(mn) AndAlso Double.IsFinite(mx) Then
                    If r Is Nothing OrElse mn < r.Item1 OrElse mx > r.Item2 Then
                        m_ranges(field) = New Tuple(Of Double, Double)(mn, mx)
                        changed = True
                    End If
                End If
            End SyncLock

            If changed Then
                RaiseEvent RangeUpdated(field)
            End If
        End Sub

        ''' <summary>
        ''' 遍历全部帧求精确全局值域（会顺带预热缓存，耗时较长，建议后台线程调用）。
        ''' </summary>
        Public Sub ComputeGlobalRange()
            For i As Integer = 0 To m_frames.Count - 1
                Call GetFrame(i)
            Next
        End Sub

        ''' <summary>获取某标量场的当前全局值域。</summary>
        Public Function GetRange(field As CfdField) As Tuple(Of Double, Double)
            SyncLock m_ranges
                Dim r As Tuple(Of Double, Double) = Nothing
                If m_ranges.TryGetValue(field, r) Then
                    Return r
                End If
            End SyncLock

            Return New Tuple(Of Double, Double)(0.0, 1.0)
        End Function

        ''' <summary>
        ''' 提取某体素的时间序列（遍历全部帧，帧数据经过 LRU 缓存）。
        ''' </summary>
        ''' <param name="idx">体素引擎索引（i*ny*nz + j*nz + k）</param>
        Public Function GetVoxelSeries(idx As Integer, field As CfdField) As VoxelSeries
            Dim n As Integer = m_frames.Count
            Dim times(n - 1) As Double
            Dim values(n - 1) As Double

            For i As Integer = 0 To n - 1
                Dim frame = GetFrame(i)
                times(i) = m_frames(i).Time

                If frame Is Nothing Then
                    values(i) = 0.0
                    Continue For
                End If

                values(i) = GetFieldValue(frame, field, idx)
            Next

            Return New VoxelSeries With {.Times = times, .Values = values}
        End Function

        ''' <summary>读取帧内某体素的标量场值。</summary>
        Public Shared Function GetFieldValue(frame As VtiFrameData, field As CfdField, idx As Integer) As Double
            Select Case field
                Case CfdField.Pressure : Return frame.Pressure(idx)
                Case CfdField.Density : Return frame.Density(idx)
                Case CfdField.Speed : Return frame.Speed(idx)
                Case Else : Return 0.0
            End Select
        End Function

        ''' <summary>读取帧内某体素的速度分量。</summary>
        Public Shared Sub GetVelocity(frame As VtiFrameData, idx As Integer, ByRef u As Double, ByRef v As Double, ByRef w As Double)
            u = frame.U(idx)
            v = frame.V(idx)
            w = frame.W(idx)
        End Sub

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
