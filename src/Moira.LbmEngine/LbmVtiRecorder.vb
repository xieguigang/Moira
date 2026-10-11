Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   LbmVtiRecorder.vb
'
'   LBM 引擎专用 VTI 快照记录器 —— 复用 Snapshot.zip 体系的 VTIExporter。
'
'   每帧导出六场：
'       mask     流体掩膜（UInt8）
'       velocity 速度矢量（Float32 × 3）
'       speed    速度模（Float32）
'       pressure 表压 p = (ρ−1)/3（Float32）
'       density  密度 ρ（Float32）
'       vorticity 展向涡量 ω_y = ∂u_z/∂x − ∂u_x/∂z（Float32，中心差分）
'
'   收尾导出：animation.pvd + frames.json + metadata.json
'   （ParaView / VTK.js / CfdDataset 浏览器直接加载）
'
' /********************************************************************************/

Imports System.IO
Imports System.Text
Imports Moira.CFDEngine.Snapshot
Imports Moira.CFDEngine.Snapshot.JSON

''' <summary>LBM 仿真的 VTI 逐帧记录器。</summary>
Public Class LbmVtiRecorder

    Private Structure FrameInfo
        Public StepIndex As Integer
        Public Time As Double
        Public FileName As String
    End Structure

    ''' <summary>已导出帧数。</summary>
    Public ReadOnly Property FrameCount As Integer
        Get
            Return _frames.Count
        End Get
    End Property

    ''' <summary>输出目录。</summary>
    Public ReadOnly OutputDir As String
    ''' <summary>每 N 步导一帧。</summary>
    Public ReadOnly Interval As Integer

    Private ReadOnly _baseName As String = "lbm"
    Private ReadOnly _padWidth As Integer = 4
    Private ReadOnly _frames As New List(Of FrameInfo)
    Private _nx, _ny, _nz As Integer
    Private ReadOnly _metadata As SnapshotMetadata

    ''' <summary>创建记录器（目录自动创建）。</summary>
    Public Sub New(outputDir As String, captureInterval As Integer,
                   solver As LbmSolver)
        Me.OutputDir = outputDir
        Me.Interval = System.Math.Max(1, captureInterval)
        Directory.CreateDirectory(outputDir)
        _nx = solver.Nx : _ny = solver.Ny : _nz = solver.Nz

        _metadata = New SnapshotMetadata With {
            .Grid = New GridInfo With {
                .Nx = solver.Nx, .Ny = solver.Ny, .Nz = solver.Nz,
                .Width = solver.Nx, .Height = solver.Ny, .Depth = solver.Nz,
                .IndexOrder = "x-major (i*Ny*Nz + j*Nz + k)",
                .ActiveVoxels = solver.FluidCount
            },
            .Simulation = New SimulationInfo With {
                .Viscosity = solver.Nu,
                .TimeStep = 1.0R,
                .Solver = $"LBM D3Q19-{solver.Model} (tau={solver.Tau:F4}, Re={solver.Re:F0}, Uwall={solver.UWall:F3})"
            }
        }
    End Sub

    ''' <summary>尝试导出一帧（按 interval 采样）。</summary>
    Public Sub Capture(solver As LbmSolver)
        If solver.StepCount Mod Interval <> 0 OrElse solver.StepCount = 0 Then Return

        Dim index = _frames.Count
        Dim fileName = _baseName & "_" & index.ToString().PadLeft(_padWidth, "0"c) & ".vti"
        Dim fullPath = Path.Combine(OutputDir, fileName)

        VTIExporter.Export(fullPath, _nx, _ny, _nz, BuildFields(solver), solver.FluidShape,
                           solver.StepCount, solver.SimTime)

        _frames.Add(New FrameInfo With {.StepIndex = solver.StepCount, .Time = solver.SimTime, .FileName = fileName})
    End Sub

    ''' <summary>收尾：animation.pvd + frames.json + metadata.json。</summary>
    Public Sub Finish()
        WritePvd()
        WriteFramesJson()
        WriteMetadataJson()
    End Sub

    ''' <summary>构建 LBM 输出场。</summary>
    Private Shared Function BuildFields(s As LbmSolver) As List(Of VtiField)
        Dim n = s.Nx * s.Ny * s.Nz
        Dim plane = s.Ny * s.Nz, row = s.Nz

        Dim mask8(n - 1) As Byte
        Dim vel(3 * n - 1) As Single
        Dim spd(n - 1) As Single
        Dim pre(n - 1) As Single
        Dim den(n - 1) As Single
        Dim vort(n - 1) As Single

        For i = 0 To s.Nx - 1
            For j = 0 To s.Ny - 1
                For k = 0 To s.Nz - 1
                    Dim idx = i * plane + j * row + k
                    Dim isFluid = s.Fluid.Data(idx) > 0.5R
                    mask8(idx) = If(isFluid, CByte(1), CByte(0))

                    Dim ux = If(isFluid, s.Ux.Data(idx), 0.0R)
                    Dim uy = If(isFluid, s.Uy.Data(idx), 0.0R)
                    Dim uz = If(isFluid, s.Uz.Data(idx), 0.0R)
                    vel(3 * idx) = CSng(ux)
                    vel(3 * idx + 1) = CSng(uy)
                    vel(3 * idx + 2) = CSng(uz)
                    spd(idx) = CSng(System.Math.Sqrt(ux * ux + uy * uy + uz * uz))
                    pre(idx) = CSng((s.Rho.Data(idx) - 1.0R) / 3.0R)
                    den(idx) = CSng(s.Rho.Data(idx))

                    ' 展向涡量 ω_y = ∂u_z/∂x − ∂u_x/∂z（中心差分，壁面用单侧）
                    Dim ip = System.Math.Min(i + 1, s.Nx - 2) * plane + j * row + k
                    Dim im = System.Math.Max(i - 1, 1) * plane + j * row + k
                    Dim kp = i * plane + j * row + System.Math.Min(k + 1, s.Nz - 2)
                    Dim km = i * plane + j * row + System.Math.Max(k - 1, 1)
                    Dim duzdx = (s.Uz.Data(ip) - s.Uz.Data(im)) / 2.0R
                    Dim duxdz = (s.Ux.Data(kp) - s.Ux.Data(km)) / 2.0R
                    vort(idx) = If(isFluid, CSng(duzdx - duxdz), 0.0F)
                Next
            Next
        Next

        Return New List(Of VtiField) From {
            New VtiField With {.Name = "mask", .Type = VtiFieldType.UInt8, .Components = 1, .Data8 = mask8},
            New VtiField With {.Name = "velocity", .Type = VtiFieldType.Float32, .Components = 3, .Data32 = vel},
            New VtiField With {.Name = "speed", .Type = VtiFieldType.Float32, .Components = 1, .Data32 = spd},
            New VtiField With {.Name = "pressure", .Type = VtiFieldType.Float32, .Components = 1, .Data32 = pre},
            New VtiField With {.Name = "density", .Type = VtiFieldType.Float32, .Components = 1, .Data32 = den},
            New VtiField With {.Name = "vorticity", .Type = VtiFieldType.Float32, .Components = 1, .Data32 = vort}
        }
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
            writer.WriteLine("  ""fields"": [""mask"", ""velocity"", ""speed"", ""pressure"", ""density"", ""vorticity""],")
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
