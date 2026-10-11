' /********************************************************************************/
'
'   EulerianMesh.vb —— Eulerian 网格转换命令行工具
'
'   将目标三维模型文件（STL/OBJ/glTF/GLB/DAE/3DS/3MF）转换为
'   CFD 求解器所需的 "网格/Eulerian" 数据模型（zip 压缩包）。
'
'   用法：
'     EulerianMesh --input <模型文件> [--output <输出zip>] [--type octree|delaunay|structured|boundarylayer]
'                  [--resolution <比例>] [--domain-scale <n>] [--cut-cells] [--poisson <n>]
'                  [--no-smooth] [--patch <名称:类型:盒面>] [--gpu]
'
' /********************************************************************************/

Imports System.IO
Imports Moira.CFDEngine.Eulerian
Imports Moira.ZMesh

Module Program

    Sub Main(args As String())
        Console.Title = "EulerianMesh - 3D model to CFD Eulerian mesh converter"

        Dim inputFile As String = Nothing
        Dim outputFile As String = Nothing
        Dim meshType As EulerianPipeline.MeshType = EulerianPipeline.MeshType.Octree
        Dim typeName As String = "octree"
        Dim resolution As Double = 0.05
        Dim domainScale As Double = 2.0
        Dim cutCells As Boolean = False
        Dim poisson As Integer = 0
        Dim smooth As Boolean = True
        Dim rules As New List(Of PatchRule)

        Try
            Dim i As Integer = 0

            While i < args.Length
                Dim arg As String = args(i).ToLowerInvariant

                Select Case arg
                    Case "--input", "-i" : i += 1 : inputFile = args(i)
                    Case "--output", "-o" : i += 1 : outputFile = args(i)
                    Case "--type", "-t"
                        i += 1
                        typeName = args(i).ToLowerInvariant

                        Select Case typeName
                            Case "octree" : meshType = EulerianPipeline.MeshType.Octree
                            Case "delaunay" : meshType = EulerianPipeline.MeshType.Delaunay
                            Case "structured" : meshType = EulerianPipeline.MeshType.Structured
                            Case "boundarylayer", "bl" : meshType = EulerianPipeline.MeshType.BoundaryLayer
                            Case Else
                                Throw New ArgumentException($"未知的网格类型：{args(i)}（可选 octree/delaunay/structured/boundarylayer）")
                        End Select

                    Case "--resolution", "-r"
                        i += 1
                        resolution = Double.Parse(args(i))

                        If resolution <= 0 OrElse resolution > 1 Then
                            Throw New ArgumentException($"--resolution 应为 (0, 1] 区间的比例值：{args(i)}")
                        End If

                    Case "--domain-scale", "-s"
                        i += 1
                        domainScale = Double.Parse(args(i))

                        If domainScale < 1.0 Then
                            Throw New ArgumentException("--domain-scale 不能小于 1")
                        End If

                    Case "--cut-cells" : cutCells = True
                    Case "--poisson" : i += 1 : poisson = Integer.Parse(args(i))
                    Case "--no-smooth" : smooth = False
                    Case "--patch", "-p"
                        i += 1
                        ' 格式：名称:类型:盒面，如 ground:Wall:YMin
                        Dim parts As String() = args(i).Split(":"c)

                        If parts.Length <> 3 Then
                            Throw New ArgumentException($"--patch 参数格式应为 名称:类型:盒面：{args(i)}")
                        End If

                        rules.Add(New PatchRule With {
                            .Name = parts(0),
                            .Type = DirectCast([Enum].Parse(GetType(EulerianPatchType), parts(1), True), EulerianPatchType),
                            .BoxFace = parts(2)})

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
                rules:=rules)

            Console.WriteLine(New String("="c, 72))
            Console.WriteLine(report.ToString)

            For Each patch As EulerianPatch In report.Patches
                Console.WriteLine($"  patch {patch.ToString}")
            Next

            Console.WriteLine($"done -> {report.OutputFile}")
        Catch ex As Exception
            Console.Error.WriteLine($"error: {ex.Message}")

            If Environment.GetEnvironmentVariable("EULERIANMESH_DEBUG") = "1" Then
                Console.Error.WriteLine(ex.ToString)
            End If

            Environment.ExitCode = 1
        End Try
    End Sub

    Sub PrintUsage()
        Console.WriteLine("EulerianMesh —— 三维模型 → CFD Eulerian 网格转换工具")
        Console.WriteLine()
        Console.WriteLine("用法：")
        Console.WriteLine("  EulerianMesh --input <模型文件> [选项]")
        Console.WriteLine()
        Console.WriteLine("选项：")
        Console.WriteLine("  --input, -i <file>      三维模型文件（STL/OBJ/glTF/GLB/DAE/3DS/3MF）")
        Console.WriteLine("  --output, -o <zip>      输出 Eulerian 网格包（默认 <input>.eulerian.zip）")
        Console.WriteLine("  --type, -t <type>       网格算法：octree(默认) | delaunay | structured | boundarylayer")
        Console.WriteLine("  --resolution, -r <p>    网格尺度（相对模型对角线比例，0.05 = 约 20 格跨模型，默认 0.05）")
        Console.WriteLine("  --domain-scale, -s <n>  计算域放大倍数（默认 2.0）")
        Console.WriteLine("  --cut-cells             八叉树网格启用切割单元（默认 IBM 完整六面体）")
        Console.WriteLine("  --poisson <n>           结构化网格 Poisson 平滑轮数（默认 0）")
        Console.WriteLine("  --no-smooth             关闭 Laplacian 平滑")
        Console.WriteLine("  --patch <名:类型:盒面>   边界 Patch 覆盖，如 ground:Wall:YMin，可多次指定")
        Console.WriteLine("  --gpu                   使用 CUDA GPU 张量后端（若可用）")
        Console.WriteLine("  --help, -h              显示本帮助")
        Console.WriteLine()
        Console.WriteLine("示例：")
        Console.WriteLine("  EulerianMesh -i car.stl -t octree -r 0.02 --cut-cells -o car.eulerian.zip")
    End Sub
End Module
