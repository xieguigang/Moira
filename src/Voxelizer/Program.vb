Imports System.IO
Imports System.Text.Json
Imports Microsoft.VisualBasic.ApplicationServices
Imports Microsoft.VisualBasic.ApplicationServices.Zip
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Microsoft.VisualBasic.Imaging.Landscape.Data
Imports Microsoft.VisualBasic.Imaging.Landscape.ThreeMF
Imports Microsoft.VisualBasic.Imaging.Landscape.Voxelization
Imports Moira.CFDEngine.Snapshot.JSON
' 别名：项目根命名空间 Voxelizer 会遮蔽 Landscape 的 Voxelizer 模块名
Imports LxVoxelizer = Microsoft.VisualBasic.Imaging.Landscape.Voxelization.Voxelizer

''' <summary>
''' 体素化器实现选择（与 ZMesh/WindTunnelBox 的 VoxelizerKind 同义；
''' CLI 独立定义以避免引入 ZMesh 工程依赖）。
''' </summary>
Public Enum VoxelizerKind
    ''' <summary>列扫描射线投射体素化（已并行 + SIMD 优化）。</summary>
    Standard
    ''' <summary>SDF 符号距离场体素化，带亚采样抗锯齿。</summary>
    Sdf
    ''' <summary>物理尺寸直接体素化（voxelSize 入口，绕开 resolution 量化）。</summary>
    Physical
    ''' <summary>SDF 连续场重采样体素化（任意目标分辨率）。</summary>
    SdfRaw
End Enum

' /********************************************************************************/
'
'   Voxelizer CLI —— 独立体素化命令行工具
'
'   将任意 Landscape 可加载的 3D 模型文件（STL / OBJ / glTF / GLB / DAE / 3DS / 3MF）
'   按命令行参数体素化，并导出为 CFDEngine GridInfo（Snapshot/JSON/Data.vb）形状的
'   JSON 体素模型文件，供 WindTunnelBox 直接加载做模拟计算（预体素化工作流，
'   把高分辨率体素化从 GUI 启动路径中彻底移出）。
'
'   用法：
'       Voxelizer <model> [-o out.json] [--resolution N | --voxel-size F]
'                 [--voxelizer standard|sdf|physical|sdfraw]
'                 [--subsamples N] [--sdf-resolution N]
'
'   GridInfo.Mask 语义（与 CFDEngine 约定一致）：
'       1 = 活动流体体素，0 = 固体障碍 —— 与 Landscape 的 True=固体 相反，导出时反转。
'
' /********************************************************************************/

Module Program

    Function Main(args As String()) As Integer
        If args.Length = 0 OrElse args(0) = "--help" OrElse args(0) = "-h" Then
            ShowUsage()
            Return If(args.Length = 0, 1, 0)
        End If

        ' ---- 参数解析 ----
        Dim modelPath As String = args(0)
        Dim outPath As String = ""
        Dim resolution As Integer = 48
        Dim voxelSize As Double = 0
        Dim kindName As String = "standard"
        Dim subSamples As Integer = 2
        Dim sdfResolution As Integer = 0

        Dim i As Integer = 1
        While i < args.Length
            Select Case args(i)
                Case "-o", "--out" : i += 1 : outPath = args(i)
                Case "--resolution" : i += 1 : resolution = CInt(args(i))
                Case "--voxel-size" : i += 1 : voxelSize = CDbl(args(i))
                Case "--voxelizer" : i += 1 : kindName = args(i).ToLowerInvariant
                Case "--subsamples" : i += 1 : subSamples = CInt(args(i))
                Case "--sdf-resolution" : i += 1 : sdfResolution = CInt(args(i))
                Case Else
                    Console.WriteLine($"unknown option: {args(i)}")
                    ShowUsage()
                    Return 1
            End Select
            i += 1
        End While

        If Not File.Exists(modelPath) Then
            Console.WriteLine($"模型文件不存在: {modelPath}")
            Return 1
        End If

        If outPath = "" Then
            outPath = Path.ChangeExtension(modelPath, ".voxel.json")
        End If

        Dim kind As VoxelizerKind
        Select Case kindName
            Case "standard" : kind = VoxelizerKind.Standard
            Case "sdf" : kind = VoxelizerKind.Sdf
            Case "physical" : kind = VoxelizerKind.Physical
            Case "sdfraw"
                If voxelSize > 0 Then
                    Console.WriteLine("sdfraw 路径请使用 --resolution 指定目标分辨率（--voxel-size 不适用）")
                    Return 1
                End If
                kind = VoxelizerKind.SdfRaw
            Case Else
                Console.WriteLine($"unknown voxelizer: {kindName}")
                Return 1
        End Select

        Console.WriteLine("=== Voxelizer CLI ===")
        Console.WriteLine($"  模型     = {modelPath}")
        Console.WriteLine($"  体素化器 = {kind}   resolution = {resolution}   voxelSize = {voxelSize}")

        ' ---- 1. 加载模型 ----
        Dim totalWatch = Stopwatch.StartNew()
        Dim sw = Stopwatch.StartNew()
        Dim scene As SceneModel = LoadScene(modelPath)

        If scene Is Nothing OrElse scene.Surfaces Is Nothing OrElse scene.Surfaces.Length = 0 Then
            Console.WriteLine("模型加载失败或不含几何表面")
            Return 1
        End If
        Console.WriteLine($"  加载完成: {scene.Surfaces.Length} 三角面   ({sw.Elapsed.TotalSeconds:F1} s)")

        ' ---- 2. 体素化（得到 Landscape 语义：True = 固体）----
        sw.Restart()
        Dim lx As VoxelModel

        Select Case kind
            Case VoxelizerKind.Sdf
                lx = SDFVoxelizer.Voxelize(scene, resolution, subSamples)
            Case VoxelizerKind.Physical
                If voxelSize > 0 Then
                    lx = LxVoxelizer.Voxelize(scene, voxelSize)
                Else
                    lx = LxVoxelizer.Voxelize(scene, resolution)
                End If
            Case VoxelizerKind.SdfRaw
                lx = VoxelizeSdfRaw(scene, resolution, subSamples, sdfResolution)
            Case Else
                If voxelSize > 0 Then
                    lx = LxVoxelizer.Voxelize(scene, voxelSize)
                Else
                    lx = LxVoxelizer.Voxelize(scene, resolution)
                End If
        End Select

        If lx Is Nothing OrElse lx.Shape Is Nothing Then
            Console.WriteLine("体素化失败（返回空体素网格）")
            Return 1
        End If

        Dim solidCount As Integer = lx.Shape.Count(Function(b) b)
        Console.WriteLine($"  体素化完成: {lx.Width}x{lx.Height}x{lx.Depth}   " &
                          $"solid = {solidCount}   ({sw.Elapsed.TotalSeconds:F1} s)")

        ' ---- 3. 语义反转构建 GridInfo（Landscape True=固体 → Mask 0；流体 → Mask 1）----
        sw.Restart()
        Dim w = lx.Width, h = lx.Height, d = lx.Depth
        Dim mask(w * h * d - 1) As Integer
        Dim activeCount As Integer = 0

        For idx As Integer = 0 To w * h * d - 1
            If Not lx.Shape(idx) Then
                mask(idx) = 1
                activeCount += 1
            End If
        Next

        Dim vs As Double = If(lx.VoxelSize > 0, lx.VoxelSize, 1.0)
        Dim grid As New GridInfo With {
            .Nx = w,
            .Ny = h,
            .Nz = d,
            .Width = w,
            .Height = h,
            .Depth = d,
            .Origin = {lx.MinX, lx.MinY, lx.MinZ},
            .Spacing = {vs, vs, vs},
            .TotalVoxels = w * h * d,
            .ActiveVoxels = activeCount,
            .Mask = mask,
            .IndexOrder = "i*ny*nz + j*nz + k"
        }

        ' ---- 4. JSON 导出（紧凑输出，Mask 大数组不缩进）----
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)))
        File.WriteAllText(outPath, JsonSerializer.Serialize(grid, New JsonSerializerOptions With {.WriteIndented = False}))
        Console.WriteLine($"  导出完成: {outPath}   ({sw.Elapsed.TotalMilliseconds:F0} ms, " &
                          $"{New FileInfo(outPath).Length / 1024.0:F0} KB)")

        Console.WriteLine()
        Console.WriteLine($"总耗时 = {totalWatch.Elapsed.TotalSeconds:F1} s")
        Return 0
    End Function

    Private Sub ShowUsage()
        Console.WriteLine("Voxelizer —— 独立体素化命令行工具（Landscape → GridInfo JSON）")
        Console.WriteLine()
        Console.WriteLine("用法: Voxelizer <model> [options]")
        Console.WriteLine()
        Console.WriteLine("  model                    3D 模型文件 (stl/obj/gltf/glb/dae/3ds/3mf)")
        Console.WriteLine("  -o, --out <json>         输出 json 路径（默认 <model>.voxel.json）")
        Console.WriteLine("  --resolution N           最长边体素分辨率（默认 48）")
        Console.WriteLine("  --voxel-size F           物理体素尺寸（physical/standard；设置后忽略 --resolution）")
        Console.WriteLine("  --voxelizer K            standard | sdf | physical | sdfraw（默认 standard）")
        Console.WriteLine("  --subsamples N           SDF 亚采样数（默认 2）")
        Console.WriteLine("  --sdf-resolution N       sdfraw 的 SDF 基准分辨率（0=自动 max(resolution,128)）")
        Console.WriteLine("  -h, --help               显示本帮助")
        Console.WriteLine()
        Console.WriteLine("示例:")
        Console.WriteLine("  Voxelizer city.stl -o city.voxel.json --resolution 96")
        Console.WriteLine("  Voxelizer city.stl --voxelizer physical --voxel-size 0.5")
    End Sub

    ''' <summary>
    ''' 统一模型加载：3MF 走专用路径（解包后直接解析 ``3D/3dmodel.model``，
    ''' 跳过 ``Metadata/thumbnail.png`` —— Landscape 的 BitmapReader 不支持 PNG，
    ''' 无条件加载缩略图会导致含缩略图的 3mf 文件加载失败），其余按扩展名分发。
    ''' </summary>
    Private Function LoadScene(filePath As String) As SceneModel
        If ModelLoader.DetectFormat(filePath) = ModelFormat._3MF Then
            Dim tmp As String = TempFileSystem.GetAppSysTempFile("--" & filePath.FileName, sessionID:=App.PID)
            Call UnZip.ImprovedExtractToDirectory(filePath, tmp, Overwrite.Always)

            Dim proj As New Project With {
                .model = ModelIO.Load3DModel(tmp & "/3D/3dmodel.model")
            }
            Return proj.ToSceneModel
        Else
            Return filePath.LoadModel
        End If
    End Function

    ''' <summary>
    ''' SDF 连续场重采样体素化（SdfRaw）：先以基准分辨率计算连续符号距离场，
    ''' 再以目标分辨率对连续场三线性插值重采样（solid ⇔ SDF &lt; 0），
    ''' 实现「CFD 网格分辨率与体素化分辨率解耦」。
    ''' </summary>
    Private Function VoxelizeSdfRaw(scene As SceneModel,
                                    targetResolution As Integer,
                                    subSamples As Integer,
                                    sdfResolution As Integer) As VoxelModel
        If sdfResolution <= 0 Then
            sdfResolution = Math.Max(targetResolution, 128)
        End If

        Dim sdf As SDFVolume = SDFVoxelizer.ComputeSDF(scene, sdfResolution, subSamples)

        If sdf Is Nothing OrElse sdf.SDF Is Nothing OrElse sdf.SDF.Length = 0 Then
            Return Nothing
        End If

        ' 目标网格：与 SDF 场覆盖同一包围盒，最长边 = targetResolution
        Dim extX = sdf.Width * sdf.VoxelSize
        Dim extY = sdf.Height * sdf.VoxelSize
        Dim extZ = sdf.Depth * sdf.VoxelSize
        Dim cell As Double = Math.Max(extX, Math.Max(extY, extZ)) / targetResolution

        Dim w As Integer = CInt(Math.Ceiling(extX / cell))
        Dim h As Integer = CInt(Math.Ceiling(extY / cell))
        Dim d As Integer = CInt(Math.Ceiling(extZ / cell))
        Dim shape(w * h * d - 1) As Boolean

        For x As Integer = 0 To w - 1
            For y As Integer = 0 To h - 1
                For z As Integer = 0 To d - 1
                    Dim px = sdf.MinX + (x + 0.5) * cell
                    Dim py = sdf.MinY + (y + 0.5) * cell
                    Dim pz = sdf.MinZ + (z + 0.5) * cell

                    If sdf.SampleSDF(New Point3D(px, py, pz)) < 0 Then
                        shape((x * h + y) * d + z) = True
                    End If
                Next
            Next
        Next

        Return New VoxelModel With {
            .Shape = shape,
            .Width = w,
            .Height = h,
            .Depth = d,
            .MinX = sdf.MinX,
            .MinY = sdf.MinY,
            .MinZ = sdf.MinZ,
            .VoxelSize = cell
        }
    End Function

End Module
