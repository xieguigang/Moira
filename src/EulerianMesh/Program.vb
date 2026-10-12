' /********************************************************************************/
'
'   EulerianMesh.vb —— Eulerian 网格转换命令行工具
'
'   将目标三维模型文件（STL/OBJ/glTF/GLB/DAE/3DS/3MF）转换为
'   CFD 求解器所需的 "网格/Eulerian" 数据模型（zip 压缩包）。
'
'   用法：
'     EulerianMesh maxres  --input <模型文件> [选项]
'     EulerianMesh convert --input <模型文件> [--output <输出zip>] [--type octree|delaunay|structured|boundarylayer]
'                          [--resolution <比例>] [--domain-scale <n>] [--cut-cells] [--poisson <n>]
'                          [--no-smooth] [--patch <名称:类型:盒面>] [--gpu]
'     EulerianMesh help
'
'   不带子命令时（首个参数以 - 开头）走兼容模式，等价于 convert。
'
' /********************************************************************************/

Imports System.IO
Imports Moira.CFDEngine
Imports Moira.CFDEngine.Eulerian
Imports Moira.ZMesh

Module Program

    Sub Main(args As String())
        Console.Title = "EulerianMesh - 3D model to CFD Eulerian mesh converter"

        Try
            If args Is Nothing OrElse args.Length = 0 Then
                Call PrintUsage()
                Environment.ExitCode = 2
                Return
            End If

            Dim head As String = args(0).ToLowerInvariant
            Dim rest As String() = args.Skip(1).ToArray

            Select Case head
                Case "maxres", "max", "ultra", "max-detail"
                    Call RunMaxDetail(rest)

                Case "convert", "mesh", "default"
                    Call RunConvert(rest)

                Case "help", "-h", "--help", "/?"
                    Call PrintUsage()

                Case Else
                    ' 兼容旧调用：EulerianMesh --input xxx ...
                    If head.StartsWith("-"c) OrElse head.StartsWith("/"c) Then
                        Call RunConvert(args)
                    Else
                        Console.Error.WriteLine($"error: 未知子命令：{args(0)}")
                        Console.Error.WriteLine("  可用子命令：maxres | convert | help（--help 查看用法）")
                        Environment.ExitCode = 2
                    End If
            End Select
        Catch ex As Exception
            Console.Error.WriteLine($"error: {ex.Message}")

            If Environment.GetEnvironmentVariable("EULERIANMESH_DEBUG") = "1" Then
                Console.Error.WriteLine(ex.ToString)
            End If

            Environment.ExitCode = 1
        End Try
    End Sub

    ''' <summary>取 "--选项 值" 形式的参数值；缺值或非法时抛出带说明的异常。</summary>
    Private Function TakeValue(args As String(), ByRef i As Integer) As String
        i += 1

        If i >= args.Length Then
            Throw New ArgumentException($"缺少参数值：{args(i - 1)}")
        End If

        Return args(i)
    End Function

    Private Function TryEnableGpu() As Boolean
        If CudaTensorF.TryRegister() Then
            Console.WriteLine("GPU: CUDA tensor 后端已启用")
            Return True
        Else
            Console.WriteLine($"warning: CUDA GPU 后端不可用（{CudaTensorF.LastError}），回退 CPU SIMD")
            Return False
        End If
    End Function

    Private Function ParsePatch(spec As String) As PatchRule
        ' 格式：名称:类型:盒面，如 ground:Wall:YMin
        Dim parts As String() = spec.Split(":"c)

        If parts.Length <> 3 Then
            Throw New ArgumentException($"--patch 参数格式应为 名称:类型:盒面：{spec}")
        End If

        Return New PatchRule With {
            .Name = parts(0),
            .Type = DirectCast([Enum].Parse(GetType(EulerianPatchType), parts(1), True), EulerianPatchType),
            .BoxFace = parts(2)}
    End Function

    ' ---------------------------------------------------------------------------------
    '  子命令 1：maxres —— 原始几何精度 + 可行的最高分辨率
    ' ---------------------------------------------------------------------------------

    Sub RunMaxDetail(args As String())
        Dim inputFile As String = Nothing
        Dim outputFile As String = Nothing
        Dim maxCells As Long = EulerianPipeline.DefaultMaxCells
        Dim maxDepth As Integer = EulerianPipeline.MaxOctreeDepth
        Dim domainScale As Double = 2.0
        Dim voxelRatio As Double = 0.5
        Dim maxVoxels As Long = EulerianPipeline.DefaultMaxVoxels
        Dim planOnly As Boolean = False
        Dim mode As DomainMode = DomainMode.External
        Dim solids As New List(Of String)
        Dim rules As New List(Of PatchRule)

        Dim i As Integer = 0

        While i < args.Length
            Dim arg As String = args(i).ToLowerInvariant

            Select Case arg
                Case "--input", "-i" : inputFile = TakeValue(args, i)
                Case "--output", "-o" : outputFile = TakeValue(args, i)
                Case "--cells", "-n" : maxCells = Long.Parse(TakeValue(args, i))
                Case "--max-depth", "-d" : maxDepth = Integer.Parse(TakeValue(args, i))
                Case "--domain-scale", "-s" : domainScale = Double.Parse(TakeValue(args, i))
                Case "--voxel-ratio" : voxelRatio = Double.Parse(TakeValue(args, i))
                Case "--max-voxels" : maxVoxels = Long.Parse(TakeValue(args, i))
                Case "--plan", "--dry-run" : planOnly = True
                Case "--mode"
                    Dim m As String = TakeValue(args, i).ToLowerInvariant

                    Select Case m
                        Case "external", "outer", "windtunnel"
                            mode = DomainMode.External
                        Case "internal", "inner", "vessel"
                            mode = DomainMode.Internal
                        Case Else
                            Throw New ArgumentException($"--mode 只支持 external|internal：{m}")
                    End Select

                Case "--internal-solid", "-x" : solids.Add(TakeValue(args, i))
                Case "--patch", "-p" : rules.Add(ParsePatch(TakeValue(args, i)))
                Case "--gpu", "-g"
                    Call TryEnableGpu()
                Case "--help", "-h", "/?"
                    Call PrintUsage()
                    Return

                Case Else
                    Throw New ArgumentException($"未知参数：{args(i)}（EulerianMesh maxres --help 查看用法）")
            End Select

            i += 1
        End While

        If String.IsNullOrEmpty(inputFile) Then
            Console.Error.WriteLine("error: maxres 需要 --input <模型文件>")
            Console.Error.WriteLine("  用法：EulerianMesh maxres -i car.stl [-o car.eulerian.zip] [--cells 2500000]")
            Environment.ExitCode = 2
            Return
        End If

        If String.IsNullOrEmpty(outputFile) Then
            outputFile = Path.ChangeExtension(inputFile, ".maxres.eulerian.zip")
        End If

        If maxCells < 8 Then
            Throw New ArgumentException($"--cells 预算过小：{maxCells}（至少 8）")
        End If

        If domainScale < 1.0 Then
            Throw New ArgumentException("--domain-scale 不能小于 1")
        End If

        If voxelRatio <= 0 Then
            Throw New ArgumentException($"--voxel-ratio 应大于 0：{voxelRatio}")
        End If

        Console.WriteLine("command   : maxres（原始几何精度 + 最高可行分辨率）")
        Console.WriteLine($"input     : {inputFile}")
        Console.WriteLine($"output    : {outputFile}")
        Console.WriteLine($"mode      : {mode.ToString}")
        Console.WriteLine($"leaf budget: {maxCells} (--cells)")
        Console.WriteLine($"depth cap : {maxDepth} (--max-depth)")

        If mode = DomainMode.Internal AndAlso solids.Count > 0 Then
            Console.WriteLine($"internals : {String.Join(", ", solids)}")
        End If

        Console.WriteLine(New String("="c, 72))

        Dim report As EulerianPipeline.PipelineReport =
            EulerianPipeline.ConvertMaxDetail(inputFile, outputFile,
                                              maxCells:=maxCells,
                                              maxDepth:=maxDepth,
                                              domainScale:=domainScale,
                                              mode:=mode,
                                              internalSolids:=If(solids.Count > 0, solids.ToArray, Nothing),
                                              rules:=rules,
                                              voxelRatio:=voxelRatio,
                                              maxVoxels:=maxVoxels,
                                              planOnly:=planOnly)

        Console.WriteLine(New String("="c, 72))

        If planOnly Then
            Console.WriteLine("plan only（--plan 未生成网格），尺度方案：")
            Console.WriteLine(report.DetailSummary())
            Console.WriteLine($"  预估最细单元: {report.CellSize.ToString("G6")}（域最长边 / 2^{report.OctreeDepth}）")
            Console.WriteLine($"  预估叶子总数: 8^{report.OctreeDepth} = {CLng(System.Math.Pow(8, report.OctreeDepth))}")
            Console.WriteLine($"  三角面片    : {report.SourceTriangles}")
            Console.WriteLine($"规划耗时      : {report.ElapsedMs} ms")
            Return
        End If

        Console.WriteLine(report.ToString)
        Console.WriteLine(report.DetailSummary())

        For Each patch As EulerianPatch In report.Patches
            Console.WriteLine($"  patch {patch.ToString}")
        Next

        Console.WriteLine($"done -> {report.OutputFile}")
    End Sub

    ' ---------------------------------------------------------------------------------
    '  子命令 2：convert —— 常规（用户显式指定尺度）转换
    ' ---------------------------------------------------------------------------------

    Sub RunConvert(args As String())
        Dim inputFile As String = Nothing
        Dim outputFile As String = Nothing
        Dim meshType As EulerianPipeline.MeshType = EulerianPipeline.MeshType.Octree
        Dim typeName As String = "octree"
        Dim resolution As Double = 0.05
        Dim domainScale As Double = 2.0
        Dim cutCells As Boolean = False
        Dim poisson As Integer = 0
        Dim smooth As Boolean = True
        Dim mode As DomainMode = DomainMode.External
        Dim solids As New List(Of String)
        Dim rules As New List(Of PatchRule)

        Dim i As Integer = 0

        While i < args.Length
            Dim arg As String = args(i).ToLowerInvariant

            Select Case arg
                Case "--input", "-i" : inputFile = TakeValue(args, i)
                Case "--output", "-o" : outputFile = TakeValue(args, i)

                Case "--type", "-t"
                    Dim raw As String = TakeValue(args, i)
                    typeName = raw.ToLowerInvariant

                    Select Case typeName
                        Case "octree" : meshType = EulerianPipeline.MeshType.Octree
                        Case "delaunay" : meshType = EulerianPipeline.MeshType.Delaunay
                        Case "structured" : meshType = EulerianPipeline.MeshType.Structured
                        Case "boundarylayer", "bl" : meshType = EulerianPipeline.MeshType.BoundaryLayer
                        Case Else
                            Throw New ArgumentException($"未知的网格类型：{raw}（可选 octree/delaunay/structured/boundarylayer）")
                    End Select

                Case "--resolution", "-r"
                    resolution = Double.Parse(TakeValue(args, i))

                    If resolution <= 0 OrElse resolution > 1 Then
                        Throw New ArgumentException($"--resolution 应为 (0, 1] 区间的比例值：{args(i)}")
                    End If

                Case "--domain-scale", "-s"
                    domainScale = Double.Parse(TakeValue(args, i))

                    If domainScale < 1.0 Then
                        Throw New ArgumentException("--domain-scale 不能小于 1")
                    End If

                Case "--cut-cells" : cutCells = True
                Case "--poisson" : poisson = Integer.Parse(TakeValue(args, i))
                Case "--no-smooth" : smooth = False
                Case "--gpu", "-g"
                    Call TryEnableGpu()
                Case "--mode"
                    Dim m As String = TakeValue(args, i).ToLowerInvariant

                    Select Case m
                        Case "external", "outer", "windtunnel"
                            mode = DomainMode.External
                        Case "internal", "inner", "vessel"
                            mode = DomainMode.Internal
                        Case Else
                            Throw New ArgumentException($"--mode 只支持 external|internal：{m}")
                    End Select

                Case "--internal-solid", "-x" : solids.Add(TakeValue(args, i))
                Case "--patch", "-p" : rules.Add(ParsePatch(TakeValue(args, i)))

                Case "--help", "-h", "/?"
                    Call PrintUsage()
                    Return

                Case Else
                    Throw New ArgumentException($"未知参数：{args(i)}（--help 查看用法）")
            End Select

            i += 1
        End While

        If String.IsNullOrEmpty(inputFile) Then
            Call PrintUsage()
            Environment.ExitCode = 2
            Return
        End If

        If String.IsNullOrEmpty(outputFile) Then
            outputFile = Path.ChangeExtension(inputFile, ".eulerian.zip")
        End If

        Console.WriteLine($"input     : {inputFile}")
        Console.WriteLine($"output    : {outputFile}")
        Console.WriteLine($"mesh type : {typeName}")
        Console.WriteLine($"resolution: {resolution} (相对模型对角线长度比例)")
        Console.WriteLine($"domain    : x{domainScale.ToString("G2")}")
        Console.WriteLine(New String("="c, 72))

        Dim report As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
            inputFile, outputFile, meshType,
            resolution:=resolution,
            domainScale:=domainScale,
            cutCells:=cutCells,
            poissonIterations:=poisson,
            smoothLaplacian:=smooth,
            rules:=rules,
            mode:=mode,
            internalSolids:=If(solids.Count > 0, solids.ToArray, Nothing))

        Console.WriteLine(New String("="c, 72))
        Console.WriteLine(report.ToString)

        For Each patch As EulerianPatch In report.Patches
            Console.WriteLine($"  patch {patch.ToString}")
        Next

        Console.WriteLine($"done -> {report.OutputFile}")
    End Sub

    Sub PrintUsage()
        Console.WriteLine("EulerianMesh —— 三维模型 → CFD Eulerian 网格转换工具")
        Console.WriteLine()
        Console.WriteLine("用法：")
        Console.WriteLine("  EulerianMesh <子命令> [选项]")
        Console.WriteLine()
        Console.WriteLine("子命令：")
        Console.WriteLine("  maxres    原始几何精度 + 可行最高分辨率的一键转换（推荐用于正式 CFD 计算）")
        Console.WriteLine("            = 顶点零缝合 + 解析切割单元(cut-cell) + 与单元同尺度的体素分类场")
        Console.WriteLine("              + 由单元预算自动推导的最大八叉树深度")
        Console.WriteLine("  convert   常规转换（按用户指定的 --type / --resolution 生成）")
        Console.WriteLine("  help      显示本帮助")
        Console.WriteLine()
        Console.WriteLine("maxres 选项：")
        Console.WriteLine("  --input, -i <file>      三维模型文件（STL/OBJ/glTF/GLB/DAE/3DS/3MF）【必需】")
        Console.WriteLine("  --output, -o <zip>      输出 Eulerian 网格包（默认 <input>.maxres.eulerian.zip）")
        Console.WriteLine("  --cells, -n <count>     八叉树叶子预算（默认 2500000 ⇒ 深度 7，约 210 万最细单元）")
        Console.WriteLine("                          预算每跨一个 8 倍台阶，深度 +1（8^d ≤ cells）")
        Console.WriteLine("  --max-depth, -d <n>     深度上限（默认 18 = 生成器硬上限）")
        Console.WriteLine("  --domain-scale, -s <n>  计算域放大倍数（默认 2.0）")
        Console.WriteLine("  --voxel-ratio <p>       固体分类场体素尺寸 / 单元尺度（默认 0.5，越小分类越准、内存越多）")
        Console.WriteLine("  --max-voxels <n>        分类场体素总数上限（默认 134217728 ≈ 512³）")
        Console.WriteLine("  --plan, --dry-run       只输出尺度方案（三角面片数/深度/单元尺度/规模），不生成网格")
        Console.WriteLine("  --mode <external|internal>   计算域语义（默认 external 外流绕流；internal 容器内流）")
        Console.WriteLine("  --internal-solid, -x <file>  内流模式的次级实体（挡板/桨叶/轴），可多次指定")
        Console.WriteLine("  --patch <名:类型:盒面>   边界 Patch 覆盖，如 ground:Wall:YMin，可多次指定")
        Console.WriteLine("  --gpu                   使用 CUDA GPU 张量后端（若可用）")
        Console.WriteLine()
        Console.WriteLine("convert 选项：")
        Console.WriteLine("  --input, -i <file>      三维模型文件")
        Console.WriteLine("  --output, -o <zip>      输出 Eulerian 网格包（默认 <input>.eulerian.zip）")
        Console.WriteLine("  --type, -t <type>       网格算法：octree(默认) | delaunay | structured | boundarylayer")
        Console.WriteLine("  --resolution, -r <p>    网格尺度（相对模型对角线比例，0.05 ≈ 20 格跨模型，默认 0.05）")
        Console.WriteLine("  --domain-scale, -s <n>  计算域放大倍数（默认 2.0）")
        Console.WriteLine("  --cut-cells             八叉树网格启用切割单元（默认 IBM 完整六面体）")
        Console.WriteLine("  --poisson <n>           结构化网格 Poisson 平滑轮数（默认 0）")
        Console.WriteLine("  --no-smooth             关闭 Laplacian 平滑")
        Console.WriteLine("  --mode <external|internal>  计算域语义（默认 external）")
        Console.WriteLine("  --internal-solid, -x <file> 内流次级实体，可多次指定")
        Console.WriteLine("  --patch <名:类型:盒面>   边界 Patch 覆盖，可多次指定")
        Console.WriteLine("  --gpu                   使用 CUDA GPU 张量后端（若可用）")
        Console.WriteLine()
        Console.WriteLine("示例：")
        Console.WriteLine("  EulerianMesh maxres -i car.stl -o car.eulerian.zip")
        Console.WriteLine("  EulerianMesh maxres -i car.stl --cells 20000000 -d 8 --plan")
        Console.WriteLine("  EulerianMesh convert -i car.stl -t octree -r 0.02 --cut-cells")
    End Sub
End Module
