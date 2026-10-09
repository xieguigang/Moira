Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   TankVtiRecorder.vb
'
'   发酵罐专用 VTI 快照记录器 —— 实现 ISnapshotRecorder。
'
'   与通用 VtiSnapshotRecorder 的区别：
'       每帧除标准五场（mask/pressure/density/velocity）外，
'       还导出发酵仿真扩展场：alpha_g（气含率）、k、epsilon、DO、
'       nut、kLa、speed —— 供 ParaView / VTK.js 端完整动画播放。
'
'   复用 Snapshot.zip 体系的：ISnapshotRecorder 接口、VTIExporter
'   任意字段导出、SnapshotMetadata JSON 元数据。
'
' /********************************************************************************/

Imports System.IO
Imports System.Text
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine
Imports Moira.CFDEngine.Snapshot
Imports Moira.CFDEngine.Snapshot.JSON

''' <summary>
''' 发酵罐 FVM 仿真的 VTI 逐帧记录器（标准五场 + 湍流/两相/传质扩展场）。
'''
''' ★ 领域类型已收敛：FluidField 现在就是 Moira.CFDEngine 的混合精度
'''   FluidField，因此本类可以<b>原生</b>实现 ISnapshotRecorder，无需任何
'''   类型桥接。五个标准场读取 Single 主存储（求解器每步末已 SyncToSingle），
'''   扩展标量场读取 Double 的 ExtraScalars。
''' </summary>
Public Class TankVtiRecorder
    Implements ISnapshotRecorder

    Private Structure FrameInfo
        Public StepIndex As Integer
        Public Time As Double
        Public FileName As String
    End Structure

    Public ReadOnly Property OutputDir As String Implements ISnapshotRecorder.OutputDir
    Public ReadOnly Property FrameCount As Integer Implements ISnapshotRecorder.FrameCount
        Get
            Return _frames.Count
        End Get
    End Property

    Private ReadOnly _baseName As String
    Private ReadOnly _interval As Integer
    Private ReadOnly _metadata As SnapshotMetadata
    Private ReadOnly _frames As New List(Of FrameInfo)
    Private ReadOnly _padWidth As Integer = 4
    Private _nx, _ny, _nz As Integer

    ''' <summary>创建记录器；metadata 为 Nothing 时跳过 metadata.json。</summary>
    Public Sub New(outDir As String, interval As Integer, metadata As SnapshotMetadata)
        ' NOTE: 参数名不可与属性 OutputDir 仅大小写不同 —— VB 标识符不区分大小写，
        ' 'OutputDir = outputDir' 会被解析为参数自赋值而属性保持 Nothing。
        OutputDir = outDir
        _baseName = "frame"
        _interval = System.Math.Max(1, interval)
        _metadata = metadata
        Directory.CreateDirectory(outDir)
    End Sub

    ''' <summary>按采样间隔写一帧 .vti（标准五场 + 扩展场）。</summary>
    Public Sub Capture(field As FluidField, stepIndex As Integer, time As Double) Implements ISnapshotRecorder.Capture
        If stepIndex Mod _interval <> 0 Then Return

        _nx = field.Nx
        _ny = field.Ny
        _nz = field.Nz

        Dim index = _frames.Count
        Dim fileName = _baseName & "_" & index.ToString().PadLeft(_padWidth, "0"c) & ".vti"
        Dim fullPath = Path.Combine(OutputDir, fileName)

        VTIExporter.Export(fullPath, _nx, _ny, _nz, BuildFields(field), field.Shape,
                           stepIndex, time)

        _frames.Add(New FrameInfo With {.StepIndex = stepIndex, .Time = time, .FileName = fileName})
    End Sub

    ''' <summary>收尾：animation.pvd + frames.json + metadata.json。</summary>
    Public Sub Finish() Implements ISnapshotRecorder.Finish
        WritePvd()
        WriteFramesJson()
        WriteMetadataJson()
    End Sub

    ''' <summary>构建标准五场 + 发酵扩展场。</summary>
    Private Shared Function BuildFields(field As FluidField) As List(Of VtiField)
        Dim n = field.TotalVoxels
        Dim plane = field.Ny * field.Nz

        Dim maskBuf(n - 1) As Byte
        Dim pBuf(n - 1) As Single
        Dim dBuf(n - 1) As Single
        Dim velBuf(3 * n - 1) As Single

        For i = 0 To field.Nx - 1
            For j = 0 To field.Ny - 1
                For k = 0 To field.Nz - 1
                    Dim idx = i * plane + j * field.Nz + k
                    Dim act = field.Shape IsNot Nothing AndAlso field.Shape.IsActive(i, j, k)
                    maskBuf(idx) = If(act, CByte(1), CByte(0))
                    pBuf(idx) = CSng(field.Pressure.Data(idx))
                    dBuf(idx) = CSng(field.Density.Data(idx))
                    velBuf(3 * idx) = CSng(field.U.Data(idx))
                    velBuf(3 * idx + 1) = CSng(field.V.Data(idx))
                    velBuf(3 * idx + 2) = CSng(field.W.Data(idx))
                Next
            Next
        Next

        Dim result As New List(Of VtiField) From {
            VtiField.Mask("mask", maskBuf),
            VtiField.Scalar("pressure", pBuf),
            VtiField.Scalar("density", dBuf),
            New VtiField With {.Name = "velocity", .Type = VtiFieldType.Float32,
                               .Components = 3, .Data32 = velBuf}
        }

        ' ---- 扩展场（alpha_g / k / epsilon / DO / nut / kLa / speed）----
        Dim extraDefs = {
            ("alpha_g", "alpha_g"), ("k", "k"), ("epsilon", "epsilon"),
            ("DO", "DO"), ("nut", "nut"), ("kLa", "kLa"),
            ("d32", "d32"), ("n0", "n0")
        }
        For Each namePair In extraDefs
            Dim t = field.GetExtra(namePair.Item2)
            If t Is Nothing Then Continue For
            Dim buf(n - 1) As Single
            Dim src = t.Data             ' Array.Copy 不做 Double→Single 收缩，逐元素转换
            For idx = 0 To n - 1
                buf(idx) = CSng(src(idx))
            Next
            result.Add(VtiField.Scalar(namePair.Item1, buf))
        Next

        ' speed = |velocity|（可视化便利）
        Dim sp(n - 1) As Single
        For idx = 0 To n - 1
            Dim uu = velBuf(3 * idx), vv = velBuf(3 * idx + 1), ww = velBuf(3 * idx + 2)
            sp(idx) = CSng(System.Math.Sqrt(uu * uu + vv * vv + ww * ww))
        Next
        result.Add(VtiField.Scalar("speed", sp))

        Return result
    End Function

    Private Sub WritePvd()
        Dim pvdPath = Path.Combine(OutputDir, "animation.pvd")
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

    Private Sub WriteFramesJson()
        Dim jsonPath = Path.Combine(OutputDir, "frames.json")
        Using writer As New StreamWriter(jsonPath, False, New UTF8Encoding(False))
            writer.WriteLine("{")
            writer.WriteLine("  ""format"": ""vti"",")
            writer.WriteLine("  ""grid"": {{ ""nx"": {0}, ""ny"": {1}, ""nz"": {2} }},", _nx, _ny, _nz)
            writer.WriteLine("  ""fields"": [""mask"", ""pressure"", ""density"", ""velocity"", ""alpha_g"", ""k"", ""epsilon"", ""DO"", ""nut"", ""kLa"", ""d32"", ""n0"", ""speed""],")
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

    Private Sub WriteMetadataJson()
        If _metadata Is Nothing Then Return
        _metadata.Frames = New List(Of FrameRef)(_frames.Count)
        For Each f In _frames
            _metadata.Frames.Add(New FrameRef With {
                .StepIndex = f.StepIndex,
                .Time = f.Time,
                .File = f.FileName
            })
        Next
        File.WriteAllText(Path.Combine(OutputDir, "metadata.json"), _metadata.ToJson())
    End Sub

End Class
