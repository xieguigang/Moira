' /********************************************************************************/
'
'   VtiSnapshotRecorder.vb
'
'   基于二进制 .vti 的逐帧数据快照记录器
'
'   作用：
'       在模拟推进过程中按采样间隔把每一帧流体场写成独立的 .vti 文件，
'       模拟结束时生成两个索引：
'         - animation.pvd  —— ParaView 时间集合，可直接播放动画
'         - frames.json    —— 给浏览器端（VTK.js）用的轻量清单，
'                             记录每帧的 step / time / 文件名与网格尺寸，
'                             前端据此按需 fetch 单帧，避免一次性加载全部数据
'
'   设计说明：
'       - 仅依赖 FluidField，与 FermentationTank / FluidSim 等引擎对象解耦。
'       - 沿用 "零内存驻留" 策略：Capture 时立即写盘，仅在内存中缓存
'         (step, time, fileName) 这类轻量元数据。
'       - 输出字段为 mask / pressure / density / velocity 四项 Float32 二进制，
'         不再落派生的 speed 与重复的 u/v/w 标量。
'
'   输出结构：
'       outputDir/
'         ├── frame_0000.vti
'         ├── frame_0001.vti
'         ├── ...
'         ├── animation.pvd      （ParaView）
'         └── frames.json        （浏览器）
'
' /********************************************************************************/

Imports System.IO
Imports System.Text
Imports std = System.Math

Namespace Snapshot

    ''' <summary>
    ''' 基于二进制 .vti 的逐帧快照记录器 —— 实现 <see cref="ISnapshotRecorder"/>。
    ''' 仅依赖 FluidField，与引擎对象解耦。
    ''' </summary>
    Public Class VtiSnapshotRecorder
        Implements ISnapshotRecorder

#Region "单帧元数据"

        ''' <summary>已写出的一帧的元数据（用于生成 .pvd 与 frames.json）。</summary>
        Private Structure FrameInfo
            ''' <summary>该帧对应的时间步序号</summary>
            Public StepIndex As Integer
            ''' <summary>该帧模拟时间</summary>
            Public Time As Double
            ''' <summary>该帧 .vti 文件名（相对 outputDir）</summary>
            Public FileName As String
        End Structure

#End Region

#Region "配置"

        ''' <summary>输出目录。</summary>
        Public ReadOnly Property OutputDir As String Implements ISnapshotRecorder.OutputDir

        ''' <summary>帧文件基础名（如 "frame"）。</summary>
        Public ReadOnly Property BaseName As String

        ''' <summary>采样间隔：每隔多少步捕获一帧。</summary>
        Public ReadOnly Property Interval As Integer

        ''' <summary>.pvd 集合文件名。</summary>
        Public ReadOnly Property PvdName As String

        ''' <summary>已写出的帧数。</summary>
        Public ReadOnly Property FrameCount As Integer Implements ISnapshotRecorder.FrameCount
            Get
                Return _frames.Count
            End Get
        End Property

#End Region

        ''' <summary>帧文件名序号零填充宽度。</summary>
        Private ReadOnly _padWidth As Integer

        ''' <summary>已写出帧的元数据列表。</summary>
        Private ReadOnly _frames As New List(Of FrameInfo)()

        ''' <summary>最近一帧的网格尺寸（用于 frames.json）。</summary>
        Private _nx As Integer, _ny As Integer, _nz As Integer

#Region "构造函数"

        ''' <summary>
        ''' 创建 .vti 快照记录器。输出目录会被自动创建。
        ''' </summary>
        ''' <param name="outputDir">输出目录</param>
        ''' <param name="baseName">帧文件基础名（如 "frame"）</param>
        ''' <param name="interval">采样间隔（每隔多少步捕获一帧，默认 1）</param>
        ''' <param name="pvdName">.pvd 集合文件名（默认 "animation.pvd"）</param>
        ''' <param name="estimatedFrames">预计总帧数，用于估算文件名零填充宽度（默认 0，则用宽度 4）</param>
        Public Sub New(outputDir As String,
                       Optional baseName As String = "frame",
                       Optional interval As Integer = 1,
                       Optional pvdName As String = "animation.pvd",
                       Optional estimatedFrames As Integer = 0)

            Me.OutputDir = outputDir
            Me.BaseName = baseName
            Me.Interval = std.Max(1, interval)
            Me.PvdName = pvdName

            If estimatedFrames > 0 Then
                _padWidth = std.Max(4, CInt(std.Floor(std.Log10(estimatedFrames))) + 1)
            Else
                _padWidth = 4
            End If

            Directory.CreateDirectory(outputDir)
        End Sub

#End Region

#Region "捕获与索引"

        ''' <summary>
        ''' 捕获当前流体场为一帧：立即写入 .vti 并缓存元数据。
        ''' 若 step 不满足采样间隔则跳过。
        ''' </summary>
        Public Sub Capture(field As FluidField, stepIndex As Integer, time As Double) Implements ISnapshotRecorder.Capture

            If Interval > 1 AndAlso (stepIndex Mod Interval) <> 0 Then
                Return
            End If

            _nx = field.Nx
            _ny = field.Ny
            _nz = field.Nz

            Dim index = _frames.Count
            Dim fileName = BaseName & "_" & index.ToString().PadLeft(_padWidth, "0"c) & ".vti"
            Dim fullPath = Path.Combine(OutputDir, fileName)

            ' 立即写盘（零内存驻留：不缓存场数据）
            VTIExporter.Export(field, fullPath, stepIndex, time)

            _frames.Add(New FrameInfo With {
                .StepIndex = stepIndex,
                .Time = time,
                .FileName = fileName
            })
        End Sub

        ''' <summary>
        ''' 模拟结束收尾：生成 animation.pvd（ParaView）与 frames.json（浏览器）。
        ''' </summary>
        Public Sub Finish() Implements ISnapshotRecorder.Finish
            WritePvd()
            WriteFramesJson()
        End Sub

#End Region

#Region "索引写出"

        ''' <summary>生成 ParaView 时间集合文件 (.pvd)。</summary>
        Private Sub WritePvd()
            Dim pvdPath = Path.Combine(OutputDir, PvdName)

            Using writer As New StreamWriter(pvdPath)
                writer.WriteLine("<?xml version=""1.0""?>")
                writer.WriteLine("<VTKFile type=""Collection"" version=""0.1"" byte_order=""LittleEndian"">")
                writer.WriteLine("  <Collection>")
                For Each frame In _frames
                    writer.WriteLine("    <DataSet timestep=""{0}"" group="""" part=""0"" file=""{1}""/>",
                                     frame.Time.ToString("R"), frame.FileName)
                Next
                writer.WriteLine("  </Collection>")
                writer.WriteLine("</VTKFile>")
            End Using
        End Sub

        ''' <summary>
        ''' 生成 frames.json —— 浏览器端的轻量帧清单。
        ''' 前端据此按帧 fetch 单个 .vti，而不必一次性加载整个时间序列。
        ''' </summary>
        Private Sub WriteFramesJson()
            Dim jsonPath = Path.Combine(OutputDir, "frames.json")

            Using writer As New StreamWriter(jsonPath, False, New UTF8Encoding(False))
                writer.WriteLine("{")
                writer.WriteLine("  ""format"": ""vti"",")
                writer.WriteLine("  ""grid"": {{ ""nx"": {0}, ""ny"": {1}, ""nz"": {2} }},", _nx, _ny, _nz)
                writer.WriteLine("  ""fields"": [""mask"", ""pressure"", ""density"", ""velocity""],")
                writer.WriteLine("  ""frames"": [")

                For i = 0 To _frames.Count - 1
                    Dim f = _frames(i)
                    Dim comma = If(i = _frames.Count - 1, "", ",")
                    writer.WriteLine("    {{ ""step"": {0}, ""time"": {1}, ""file"": ""{2}"" }}{3}",
                                     f.StepIndex, f.Time.ToString("R"), f.FileName, comma)
                Next

                writer.WriteLine("  ]")
                writer.WriteLine("}")
            End Using
        End Sub

#End Region

    End Class

End Namespace
