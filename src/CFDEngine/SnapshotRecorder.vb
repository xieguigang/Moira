' /********************************************************************************/
'
'   SnapshotRecorder.vb
'
'   数据快照记录器 —— 逐帧持久化 CFD 计算结果
'
'   作用：
'       在模拟推进过程中，按采样间隔把每一帧流体场写入独立的 .vtk 文件，
'       并在结束时生成一个 ParaView 时间集合文件 (.pvd)，
'       使 ParaView 可一键加载整个时间序列并以动画形式播放仿真过程。
'
'   设计说明：
'       - 本记录器只依赖 FluidField，与 FermentationTank / FluidSim 等引擎对象解耦。
'       - 采用 "零内存驻留" 策略：Capture 时立即把当前帧写入磁盘，仅在内存中
'         缓存 (step, time, fileName) 元数据，避免 N 帧全量场数据驻留导致内存爆炸。
'       - 帧文件名使用零填充序号（如 frame_0000.vtk），保证文件排序正确。
'
'   使用方法：
'       Dim rec As New SnapshotRecorder("output_dir", "frame")
'       ' 在每个时间步之后：
'       rec.Capture(tank.Field, tank.StepCount, tank.Time)
'       ' 模拟结束后：
'       rec.WriteIndex()   ' 生成 animation.pvd
'
' /********************************************************************************/

Imports System.IO
Imports std = System.Math

Namespace CFDEngine

    ''' <summary>
    ''' 逐帧数据快照记录器 —— 把每帧写为 .vtk 并生成 .pvd 动画集合。
    ''' 仅依赖 FluidField，与引擎对象解耦。
    ''' </summary>
    Public Class SnapshotRecorder

#Region "单帧元数据"

        ''' <summary>已写出的一帧的元数据（用于生成 .pvd 集合）。</summary>
        Private Structure FrameInfo
            ''' <summary>该帧模拟时间。</summary>
            Public Time As Double
            ''' <summary>该帧 .vtk 文件名（相对 outputDir）。</summary>
            Public FileName As String
        End Structure

#End Region

#Region "配置"

        ''' <summary>输出目录。</summary>
        Public ReadOnly Property OutputDir As String

        ''' <summary>帧文件基础名（如 "frame"）。</summary>
        Public ReadOnly Property BaseName As String

        ''' <summary>采样间隔：每隔多少步捕获一帧（1 = 每帧）。</summary>
        Public ReadOnly Property Interval As Integer

        ''' <summary>.pvd 集合文件名。</summary>
        Public ReadOnly Property PvdName As String

        ''' <summary>已写出的帧数。</summary>
        Public ReadOnly Property FrameCount As Integer
            Get
                Return _frames.Count
            End Get
        End Property

#End Region

        ''' <summary>帧文件名序号零填充宽度。</summary>
        Private ReadOnly _padWidth As Integer

        ''' <summary>已写出帧的元数据列表。</summary>
        Private ReadOnly _frames As New List(Of FrameInfo)

#Region "构造函数"

        ''' <summary>
        ''' 创建快照记录器。输出目录会被自动创建。
        ''' </summary>
        ''' <param name="outputDir">输出目录</param>
        ''' <param name="baseName">帧文件基础名（如 "frame"）</param>
        ''' <param name="interval">采样间隔（每隔多少步捕获一帧，默认 1）</param>
        ''' <param name="pvdName">.pvd 集合文件名（默认 "animation.pvd"）</param>
        ''' <param name="estimatedFrames">预计总帧数，用于估算文件名零填充宽度（默认 0，则用宽度 4）</param>
        Public Sub New(outputDir As String, baseName As String,
                       Optional interval As Integer = 1,
                       Optional pvdName As String = "animation.pvd",
                       Optional estimatedFrames As Integer = 0)

            Me.OutputDir = outputDir
            Me.BaseName = baseName
            Me.Interval = std.Max(1, interval)
            Me.PvdName = pvdName

            ' 依据预计帧数估算零填充宽度，至少 4 位
            If estimatedFrames > 0 Then
                _padWidth = std.Max(4, CInt(std.Floor(std.Log10(estimatedFrames))) + 1)
            Else
                _padWidth = 4
            End If

            ' 自动创建输出目录
            Directory.CreateDirectory(outputDir)

        End Sub

#End Region

#Region "捕获与索引"

        ''' <summary>
        ''' 捕获当前流体场为一帧：立即写入 .vtk 文件并缓存元数据。
        ''' 若 step 不满足采样间隔则跳过。
        ''' </summary>
        ''' <param name="field">当前流体场</param>
        ''' <param name="stepIndex">当前时间步序号</param>
        ''' <param name="time">当前模拟时间</param>
        Public Sub Capture(field As FluidField, stepIndex As Integer, time As Double)

            ' 采样间隔过滤
            If Interval > 1 AndAlso (stepIndex Mod Interval) <> 0 Then
                Return
            End If

            ' 帧文件名（零填充序号）
            Dim index = _frames.Count
            Dim fileName = BaseName & "_" & index.ToString().PadLeft(_padWidth, "0"c) & ".vtk"
            Dim fullPath = Path.Combine(OutputDir, fileName)

            ' 立即写盘（零内存驻留：不缓存场数据）
            VTKExporter.Export(field, fullPath, stepIndex, time)

            ' 仅缓存元数据
            _frames.Add(New FrameInfo With {.Time = time, .FileName = fileName})

        End Sub

        ''' <summary>
        ''' 生成 ParaView 时间集合文件 (.pvd)，引用已写出的所有帧。
        ''' ParaView 打开该文件即可按时间轴播放动画。
        ''' </summary>
        Public Sub WriteIndex()

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

#End Region

    End Class

End Namespace
