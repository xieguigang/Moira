' /********************************************************************************/
'
'   JsonSnapshotRecorder.vb
'
'   基于 JSON 的逐帧数据快照记录器
'
'   作用：
'       构造时写一次 metadata.json（三维体素网格 + 仿真配置），逐帧写
'       frame_xxx.json（step/time + 全部 7 个物理场的扁平数组）。由于网格与
'       配置只在 metadata.json 写一次，帧文件不再重复这些定义，从而显著降低
'       VTK 逐帧快照中因重复网格定义带来的帧间冗余。
'
'   设计说明：
'       - 仅依赖 FluidField（与引擎解耦）。
'       - 沿用 "零内存驻留" 策略：Capture 立即写盘，仅缓存轻量帧引用（在 metadata.Frames）。
'       - 帧数据：扁平一维数组，索引顺序 i*ny*nz + j*nz + k（与 VTK 导出对齐）。
'         velocity 以 u,v,w 交错扁平数组（长度 3N）表示，确保 "全部 7 个场"。
'       - metadata.json 用 System.Text.Json 序列化；frame_xxx.json 因数组巨大，
'         沿用 StreamWriter 手工拼装 JSON，避免反射与巨型字符串驻留。
'
'   文件结构：
'       outputDir/
'         ├── metadata.json        （网格 + 配置 + 帧引用列表，写一次）
'         ├── frame_0000.json      （step/time + 7 场扁平数组）
'         ├── frame_0001.json
'         └── ...
'
' /********************************************************************************/

Imports System.IO
Imports std = System.Math

Namespace Snapshot.JSON

    ''' <summary>
    ''' 基于 JSON 的逐帧数据快照记录器 —— 实现 ISnapshotRecorder。
    ''' 仅依赖 FluidField，与引擎对象解耦。
    ''' </summary>
    Public Class JsonSnapshotRecorder
        Implements ISnapshotRecorder

#Region "配置"

        ''' <summary>输出目录。</summary>
        Public ReadOnly Property OutputDir As String Implements ISnapshotRecorder.OutputDir

        ''' <summary>帧文件基础名（如 "frame"）。</summary>
        Private ReadOnly _baseName As String

        ''' <summary>采样间隔：每隔多少步捕获一帧（1 = 每帧）。</summary>
        Private ReadOnly _interval As Integer

        ''' <summary>帧文件名序号零填充宽度。</summary>
        Private ReadOnly _padWidth As Integer

        ''' <summary>快照元数据（网格 + 配置 + 帧引用列表）。</summary>
        Private ReadOnly _metadata As SnapshotMetadata

        ''' <summary>已写出的帧数。</summary>
        Public ReadOnly Property FrameCount As Integer Implements ISnapshotRecorder.FrameCount
            Get
                Return _metadata.Frames.Count
            End Get
        End Property

#End Region

#Region "构造函数"

        ''' <summary>
        ''' 创建 JSON 快照记录器。输出目录会被自动创建，并立即写出 metadata.json。
        ''' </summary>
        ''' <param name="outputDir">输出目录</param>
        ''' <param name="metadata">快照元数据（网格 + 配置）。可由 SnapshotMetadata.FromTank 构造。</param>
        ''' <param name="baseName">帧文件基础名（如 "frame"）</param>
        ''' <param name="interval">采样间隔（每隔多少步捕获一帧，默认 1）</param>
        ''' <param name="estimatedFrames">预计总帧数，用于估算文件名零填充宽度（默认 0，则用宽度 4）</param>
        Public Sub New(outputDir As String, metadata As SnapshotMetadata,
                       Optional baseName As String = "frame",
                       Optional interval As Integer = 1,
                       Optional estimatedFrames As Integer = 0)

            Me.OutputDir = outputDir
            Me._metadata = metadata
            Me._baseName = baseName
            Me._interval = std.Max(1, interval)

            ' 依据预计帧数估算零填充宽度，至少 4 位
            If estimatedFrames > 0 Then
                _padWidth = std.Max(4, CInt(std.Floor(std.Log10(estimatedFrames))) + 1)
            Else
                _padWidth = 4
            End If

            ' 自动创建输出目录
            Directory.CreateDirectory(outputDir)

            ' 构造时立即写出 metadata.json（网格 + 配置；frames 此时为空）
            WriteMetadata()

        End Sub

#End Region

#Region "捕获与索引"

        ''' <summary>
        ''' 捕获当前流体场为一帧：立即写入 frame_xxx.json 并缓存帧引用。
        ''' 若 step 不满足采样间隔则跳过。
        ''' </summary>
        ''' <param name="field">当前流体场</param>
        ''' <param name="stepIndex">当前时间步序号</param>
        ''' <param name="time">当前模拟时间</param>
        Public Sub Capture(field As FluidField, stepIndex As Integer, time As Double) Implements ISnapshotRecorder.Capture

            ' 采样间隔过滤
            If _interval > 1 AndAlso (stepIndex Mod _interval) <> 0 Then
                Return
            End If

            ' 帧文件名（零填充序号）
            Dim index = _metadata.Frames.Count
            Dim fileName = _baseName & "_" & index.ToString().PadLeft(_padWidth, "0"c) & ".json"
            Dim fullPath = Path.Combine(OutputDir, fileName)

            ' 立即写盘（零内存驻留：不缓存场数据）
            WriteFrame(field, stepIndex, time, fullPath)

            ' 仅缓存轻量帧引用（用于 metadata.json 的 frames 列表）
            _metadata.Frames.Add(New FrameRef With {.stepIndex = stepIndex, .time = time, .File = fileName})

        End Sub

        ''' <summary>
        ''' 模拟结束收尾：把 frames 引用列表补写到 metadata.json。
        ''' </summary>
        Public Sub Finish() Implements ISnapshotRecorder.Finish
            WriteMetadata()
        End Sub

#End Region

#Region "JSON 写出"

        ''' <summary>写出 metadata.json（网格 + 配置 + 帧引用列表）。</summary>
        Private Sub WriteMetadata()
            Dim metaPath = Path.Combine(OutputDir, "metadata.json")
            File.WriteAllText(metaPath, _metadata.ToJson())
        End Sub

        ''' <summary>写出单帧 frame_xxx.json：step/time + 仅活动体素的 7 个物理场扁平数组。
        ''' 体素掩膜（mask）仅在 metadata.json 写一次，本帧只输出活动体素（顺序同 mask），降低逐帧冗余。</summary>
        Private Sub WriteFrame(field As FluidField, stepIndex As Integer, time As Double, fullPath As String)
            Dim f = field
            Dim nx = f.Nx, ny = f.Ny, nz = f.Nz
            Dim n = nx * ny * nz
            Dim uData = f.U.Data, vData = f.V.Data, wData = f.W.Data
            Dim pData = f.Pressure.Data, dData = f.Density.Data

            ' 体素掩膜（True = 活动体素）；无 shape 时全为活动
            Dim mask As Boolean()
            If f.Shape Is Nothing Then
                mask = New Boolean(n - 1) {}
                For i = 0 To n - 1 : mask(i) = True : Next
            Else
                mask = f.Shape.Shape
            End If
            Dim activeCount = If(f.Shape Is Nothing, n, f.Shape.TotalActive)

            Using writer As New StreamWriter(fullPath)
                writer.WriteLine("{")
                writer.WriteLine("  ""step"": {0},", stepIndex)
                writer.WriteLine("  ""time"": {0},", Fmt(time))
                writer.WriteLine("  ""grid"": {{ ""nx"": {0}, ""ny"": {1}, ""nz"": {2} }},", nx, ny, nz)
                writer.WriteLine("  ""activeCount"": {0},", activeCount)
                writer.WriteLine("  ""indexOrder"": ""i*ny*nz + j*nz + k"",")
                writer.WriteLine("  ""fields"": {")

                ' 5 个基础标量场（仅活动体素）
                WriteFlatMasked(writer, "pressure", pData, mask, "    ", isLast:=False)
                WriteFlatMasked(writer, "density", dData, mask, "    ", isLast:=False)
                WriteFlatMasked(writer, "u", uData, mask, "    ", isLast:=False)
                WriteFlatMasked(writer, "v", vData, mask, "    ", isLast:=False)
                WriteFlatMasked(writer, "w", wData, mask, "    ", isLast:=False)
                ' 2 个派生场（仅活动体素）
                WriteSpeedMasked(writer, uData, vData, wData, mask, "    ", isLast:=False)
                WriteVelocityMasked(writer, uData, vData, wData, mask, "    ", isLast:=True)

                writer.WriteLine("  }")
                writer.WriteLine("}")
            End Using
        End Sub

        ''' <summary>写出一个命名扁平数组（标量场），仅含活动体素。</summary>
        Private Sub WriteFlatMasked(writer As StreamWriter, name As String, data() As Double, mask As Boolean(),
                                    indent As String, isLast As Boolean)
            writer.Write(indent & """" & name & """: [")
            Dim first As Boolean = True
            For i = 0 To data.Length - 1
                If mask(i) Then
                    If Not first Then writer.Write(",")
                    writer.Write(Fmt(data(i)))
                    first = False
                End If
            Next
            writer.WriteLine("]" & If(isLast, "", ","))
        End Sub

        ''' <summary>写出 speed 标量场（由 U/V/W 派生），仅含活动体素。</summary>
        Private Sub WriteSpeedMasked(writer As StreamWriter, uData() As Double, vData() As Double, wData() As Double,
                                     mask As Boolean(), indent As String, isLast As Boolean)
            writer.Write(indent & """speed"": [")
            Dim first As Boolean = True
            For i = 0 To uData.Length - 1
                If mask(i) Then
                    If Not first Then writer.Write(",")
                    Dim s = std.Sqrt(uData(i) * uData(i) + vData(i) * vData(i) + wData(i) * wData(i))
                    writer.Write(Fmt(s))
                    first = False
                End If
            Next
            writer.WriteLine("]" & If(isLast, "", ","))
        End Sub

        ''' <summary>写出 velocity 向量场（u,v,w 交错扁平数组），仅含活动体素。</summary>
        Private Sub WriteVelocityMasked(writer As StreamWriter, uData() As Double, vData() As Double, wData() As Double,
                                        mask As Boolean(), indent As String, isLast As Boolean)
            writer.Write(indent & """velocity"": [")
            Dim first As Boolean = True
            For i = 0 To uData.Length - 1
                If mask(i) Then
                    If Not first Then writer.Write(",")
                    writer.Write(Fmt(uData(i)) & "," & Fmt(vData(i)) & "," & Fmt(wData(i)))
                    first = False
                End If
            Next
            writer.WriteLine("]" & If(isLast, "", ","))
        End Sub

        ''' <summary>双精度数转 JSON 数值字符串（net 中默认即最短可往返）。</summary>
        Private Function Fmt(d As Double) As String
            Return d.ToString()
        End Function

#End Region

    End Class

End Namespace
