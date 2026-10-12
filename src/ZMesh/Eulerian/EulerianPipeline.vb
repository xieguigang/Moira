Imports System.IO
Imports System.Diagnostics
Imports Microsoft.VisualBasic.Imaging.Landscape.Data
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' Eulerian 网格生成一键流水线入口：
''' LoadModel → 几何预处理 → 网格生成（四类算法）→ FVM 拓扑 → Patch 分类 →
''' （可选 Laplacian 平滑）→ zip 压缩包输出。
''' </summary>
Public Class EulerianPipeline

    ''' <summary>八叉树均匀细分的深度上限（生成器内部硬限制）。</summary>
    Public Const MaxOctreeDepth As Integer = 18

    ''' <summary>默认的八叉树叶子预算（--cells）：8^7 = 2,097,152。</summary>
    Public Const DefaultMaxCells As Long = 2500000L

    ''' <summary>默认固体分类场体素总数上限（约 512³）。</summary>
    Public Const DefaultMaxVoxels As Long = 134217728L

    ''' <summary>网格生成算法类型。</summary>
    Public Enum MeshType
        ''' <summary>笛卡尔八叉树 + 切割单元。</summary>
        Octree = 0
        ''' <summary>Delaunay 四面体填充。</summary>
        Delaunay = 1
        ''' <summary>结构化 TFI/Poisson。</summary>
        Structured = 2
        ''' <summary>边界层层推进（棱柱层）。</summary>
        BoundaryLayer = 3
    End Enum

    ''' <summary>流水线执行报告。</summary>
    Public Class PipelineReport

        Public SourceModel As String
        Public MeshKind As String
        Public ElapsedMs As Long
        Public Mesh As EulerianMesh
        Public Patches As List(Of EulerianPatch)
        Public OutputFile As String

        ' ---- 高保真（<see cref="ConvertMaxDetail"/>）诊断信息 ----

        ''' <summary>是否为最高精度模式。</summary>
        Public MaxDetail As Boolean
        ''' <summary>实际使用的八叉树深度（最细单元 = 域最长边 / 2^depth）。</summary>
        Public OctreeDepth As Integer
        ''' <summary>目标单元尺度（世界坐标单位）。</summary>
        Public CellSize As Double
        ''' <summary>固体分类场实际体素尺寸（世界坐标单位）。</summary>
        Public SolidVoxelSize As Double
        ''' <summary>叶子预算（8^depth ≤ LeafBudget）。</summary>
        Public LeafBudget As Long
        ''' <summary>源模型的三角面片数。</summary>
        Public SourceTriangles As Integer
        ''' <summary>顶点缝合容差（0 = 保留全部原始顶点，不做任何合并）。</summary>
        Public StitchTolerance As Double

        ''' <summary>高保真模式的参数摘要（多行文本，供 CLI 打印）。</summary>
        Public Function DetailSummary() As String
            Dim lines As New List(Of String)

            If MaxDetail Then
                lines.Add($"  max detail : octree depth={OctreeDepth}, target cell={CellSize.ToString("G6")}")
                lines.Add($"  leaf budget: {LeafBudget} (8^{OctreeDepth} = {CLng(std.Pow(8, OctreeDepth))} leaves)")
            End If

            lines.Add($"  triangles  : {SourceTriangles}, stitch tolerance={StitchTolerance}")

            If SolidVoxelSize > 0 Then
                lines.Add($"  solid field: voxel size={SolidVoxelSize.ToString("G6")}")
            End If

            Return String.Join(vbLf, lines)
        End Function

        Public Overrides Function ToString() As String
            Dim head As String = $"{MeshKind}: {Mesh.ToString}, patches={Patches.Count}, {ElapsedMs} ms" &
                                 If(OutputFile IsNot Nothing, $" -> {OutputFile}", "")

            If Not MaxDetail Then
                Return head
            End If

            Return head & vbLf &
                   $"  max-detail: depth={OctreeDepth}, cell={CellSize.ToString("G6")}, voxel={SolidVoxelSize.ToString("G6")}"
        End Function
    End Class

    ''' <summary>
    ''' 一键转换：三维模型文件 → Eulerian 网格 zip 包。
    ''' </summary>
    ''' <param name="modelFile">三维模型文件。</param>
    ''' <param name="outputZip">输出 zip 路径（Nothing = 只构建不落盘）。</param>
    ''' <param name="type">网格生成算法。</param>
    ''' <param name="resolution">目标网格尺度（相对模型最大边长的比例，0.05 ≈ 20 格跨越模型）。</param>
    ''' <param name="domainScale">计算域放大倍数。</param>
    ''' <param name="cutCells">八叉树网格是否做切割单元。</param>
    ''' <param name="poissonIterations">结构化网格 Poisson 平滑轮数。</param>
    ''' <param name="smoothLaplacian">是否对四面体网格做 Laplacian 平滑。</param>
    ''' <param name="rules">用户 Patch 覆盖规则。</param>
    ''' <param name="mode">域语义：外流（默认，风洞绕流）/ 内流（发酵罐等容器内流）。</param>
    ''' <param name="internalSolids">内流模式的次级实体模型文件（挡板 / 桨叶 / 轴）。</param>
    Public Shared Function Convert(modelFile As String,
                                   outputZip As String,
                                   type As MeshType,
                                   Optional resolution As Double = 0.05,
                                   Optional domainScale As Double = 2.0,
                                   Optional cutCells As Boolean = False,
                                   Optional poissonIterations As Integer = 0,
                                   Optional smoothLaplacian As Boolean = True,
                                   Optional rules As List(Of PatchRule) = Nothing,
                                   Optional mode As DomainMode = DomainMode.External,
                                   Optional internalSolids As String() = Nothing) As PipelineReport

        Dim watch As Stopwatch = Stopwatch.StartNew()

        ' ---- 1. 几何预处理 ----
        ' 内流模式的计算域就是容器包围盒，不做放大与地面净空
        Dim effectiveScale As Double = If(mode = DomainMode.Internal, 1.0, domainScale)
        Dim effectiveClearance As Double = If(mode = DomainMode.Internal, 0.0, 0.1)

        Dim domain As GeometryPreprocessor.FluidDomain =
            GeometryPreprocessor.FromModelFile(modelFile,
                                               domainScale:=effectiveScale,
                                               groundClearance:=effectiveClearance,
                                               mode:=mode,
                                               internalSolids:=internalSolids)
        Dim modelExtent As Double = (domain.ModelMax - domain.ModelMin).Length

        ' resolution 语义：相对模型最大对角线长度的比例（0.05 = 模型跨越约 20 个单元）
        Dim res As Double = If(resolution > 0, modelExtent * resolution, modelExtent * 0.05)

        Return RunGenerator(watch, domain, modelFile, outputZip, type, res,
                            cutCells, poissonIterations, smoothLaplacian, rules,
                            stitchTolerance:=0.0001)
    End Function

    ''' <summary>
    ''' 最高精度 / 原始几何精度转换：
    ''' 把三维模型文件直接转换为"该八叉树网格体系下能做到的最细"CFD 计算模型文件
    ''' （Eulerian zip 包）。
    '''
    ''' 与普通 <see cref="Convert"/> 的区别（保真四件套）：
    '''   1. 顶点零缝合：缝合容差取 0，模型顶点坐标原样保留，
    '''      不做任何量化合并（避免了"相距很远的点被判为同一点"的经典哈希丢位缺陷）；
    '''   2. 切割单元：几何边界由模型三角面片的解析平面精确裁剪（而不是 IBM 阶梯近似）；
    '''   3. 分类场跟随网格：固体/流体判定使用的体素尺寸 = <paramref name="voxelRatio"/> × 单元尺度，
    '''      不会出现"128 格的低精度体素场决定百万级单元去留"的失真；
    '''   4. 分辨率最大化：由叶子预算自动推导最大可行八叉树深度
    '''      （8^depth ≤ <paramref name="maxCells"/>，受 <paramref name="maxDepth"/> 与生成器的 18 层上限约束）。
    ''' </summary>
    ''' <param name="modelFile">三维模型文件。</param>
    ''' <param name="outputZip">输出 zip 路径（Nothing = 只构建不落盘）。</param>
    ''' <param name="maxCells">八叉树叶子预算（满树叶子总数上限，实际输出单元数会更少：固体内单元被剔除）。</param>
    ''' <param name="maxDepth">深度上限（&lt;= 18）。</param>
    ''' <param name="domainScale">计算域放大倍数（外流模式）。</param>
    ''' <param name="mode">域语义。</param>
    ''' <param name="internalSolids">内流模式的次级实体模型文件。</param>
    ''' <param name="rules">用户 Patch 覆盖规则。</param>
    ''' <param name="voxelRatio">分类场体素尺寸 / 单元尺度（默认 0.5 = 体素比单元细一倍）。</param>
    ''' <param name="maxVoxels">分类场体素总数上限（内存保护）。</param>
    ''' <param name="planOnly">True = 只计算尺度计划（含预期规模）并直接返回，不生成网格。</param>
    Public Shared Function ConvertMaxDetail(modelFile As String,
                                            outputZip As String,
                                            Optional maxCells As Long = DefaultMaxCells,
                                            Optional maxDepth As Integer = MaxOctreeDepth,
                                            Optional domainScale As Double = 2.0,
                                            Optional mode As DomainMode = DomainMode.External,
                                            Optional internalSolids As String() = Nothing,
                                            Optional rules As List(Of PatchRule) = Nothing,
                                            Optional voxelRatio As Double = 0.5,
                                            Optional maxVoxels As Long = DefaultMaxVoxels,
                                            Optional planOnly As Boolean = False) As PipelineReport

        If maxCells < 8 Then
            Throw New ArgumentException($"--cells 预算过小：{maxCells}（至少 8，即 1 次八分）")
        End If

        If voxelRatio <= 0 Then
            voxelRatio = 0.5
        End If

        If maxDepth < 1 Then
            maxDepth = 1
        ElseIf maxDepth > MaxOctreeDepth Then
            maxDepth = MaxOctreeDepth
        End If

        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim scene As SceneModel = GeometryPreprocessor.LoadScene(modelFile)

        ' 内流模式的计算域就是容器包围盒，不做放大与地面净空
        Dim effectiveScale As Double = If(mode = DomainMode.Internal, 1.0, domainScale)
        Dim effectiveClearance As Double = If(mode = DomainMode.Internal, 0.0, 0.1)

        ' ---- 1. 几何抽取 + 域盒（本步不做体素化：先用真实尺度规划网格分辨率）----
        Dim domain As GeometryPreprocessor.FluidDomain =
            GeometryPreprocessor.FromSceneModel(scene, modelFile,
                                                domainScale:=effectiveScale,
                                                groundClearance:=effectiveClearance,
                                                stitchTolerance:=0.0,
                                                mode:=mode,
                                                internalSolids:=internalSolids,
                                                buildSolidField:=Not planOnly)

        Dim ext As EulerianVertex = domain.DomainSize
        Dim domainExtent As Double = std.Max(std.Max(ext.X, ext.Y), ext.Z)

        If domainExtent <= 0 Then
            Throw New InvalidDataException("计算域尺寸非法（退化模型）")
        End If

        ' ---- 2. 最大可行深度：8^depth <= maxCells ----
        Dim depth As Integer = CInt(std.Floor(std.Log(maxCells, 8.0)))

        If depth < 1 Then
            depth = 1
        End If

        If depth > maxDepth Then
            depth = maxDepth
        End If

        Dim cellSize As Double = domainExtent / (1L << depth)
        ' 乘 (1 + 1e-9)：把 Ceiling(Log2(extent/res)) 稳定钉在 depth 上，
        ' 否则浮点误差会让它跳到 depth+1（单元数瞬间 ×8）。
        Dim res As Double = cellSize * (1.0 + 0.000000001)
        Dim voxelSize As Double = cellSize * voxelRatio

        Dim plan As New PipelineReport With {
            .SourceModel = modelFile,
            .MeshKind = "octree-cutcell (max detail)",
            .MaxDetail = True,
            .OctreeDepth = depth,
            .CellSize = cellSize,
            .LeafBudget = maxCells,
            .SourceTriangles = domain.Triangles.Count,
            .StitchTolerance = 0.0,
            .ElapsedMs = watch.ElapsedMilliseconds}

        If Not planOnly Then
            ' ---- 3. 与网格同尺度的固体分类场 ----
            Call GeometryPreprocessor.FillSolidField(domain, scene,
                                                     targetVoxelSize:=voxelSize,
                                                     maxVoxels:=maxVoxels)
            plan.SolidVoxelSize = domain.Solid.VoxelSize
        End If

        If planOnly Then
            watch.Stop()
            plan.ElapsedMs = watch.ElapsedMilliseconds
            Return plan
        End If

        Dim report As PipelineReport = RunGenerator(watch, domain, modelFile, outputZip,
                                                    MeshType.Octree, res,
                                                    cutCells:=True,
                                                    poissonIterations:=0,
                                                    smoothLaplacian:=False,
                                                    rules:=rules,
                                                    stitchTolerance:=0.0)

        report.MaxDetail = True
        report.OctreeDepth = depth
        report.CellSize = cellSize
        report.LeafBudget = maxCells
        report.SolidVoxelSize = plan.SolidVoxelSize
        report.SourceTriangles = domain.Triangles.Count
        report.StitchTolerance = 0.0

        Return report
    End Function

    ''' <summary>网格生成 → FVM 拓扑 → Patch 分类 → 平滑 → zip 落盘（<see cref="Convert"/> 与
    ''' <see cref="ConvertMaxDetail"/> 共用的执行体）。</summary>
    Private Shared Function RunGenerator(watch As Stopwatch,
                                         domain As GeometryPreprocessor.FluidDomain,
                                         modelFile As String,
                                         outputZip As String,
                                         type As MeshType,
                                         res As Double,
                                         cutCells As Boolean,
                                         poissonIterations As Integer,
                                         smoothLaplacian As Boolean,
                                         rules As List(Of PatchRule),
                                         Optional stitchTolerance As Double = 0.0001) As PipelineReport

        Dim options As New MeshGenerateOptions With {
            .Domain = domain,
            .Resolution = res,
            .CutCells = cutCells,
            .PoissonIterations = poissonIterations}

        ' ---- 2. 网格生成 ----
        Dim generator As IMeshGenerator

        Select Case type
            Case MeshType.Delaunay : generator = New DelaunayTetraGenerator
            Case MeshType.Structured : generator = New StructuredMeshGenerator
            Case MeshType.BoundaryLayer : generator = New BoundaryLayerExtruder
            Case Else : generator = New OctreeCutCellGenerator
        End Select

        Dim raw As RawCellMesh = generator.Generate(options)

        ' ---- 3. FVM 拓扑 ----
        Dim mesh As EulerianMesh = FvmTopologyBuilder.Build(raw)

        ' ---- 4. Patch 分类 ----
        Dim patches As List(Of EulerianPatch) = PatchClassifier.Classify(mesh, domain, rules)
        mesh.Patches.AddRange(patches)

        ' ---- 5. 可选 Laplacian 平滑 ----
        If smoothLaplacian AndAlso type = MeshType.Delaunay Then
            Call LaplacianSmoother.Smooth(mesh, 3, 0.5)
        End If

        watch.Stop()

        Dim report As New PipelineReport With {
            .SourceModel = modelFile,
            .MeshKind = generator.Name,
            .Mesh = mesh,
            .Patches = patches,
            .ElapsedMs = watch.ElapsedMilliseconds,
            .SourceTriangles = domain.Triangles.Count,
            .StitchTolerance = stitchTolerance}

        ' ---- 6. zip 输出 ----
        If Not String.IsNullOrEmpty(outputZip) Then
            Call EulerianMeshPackage.Save(mesh, outputZip, generator.Name)
            report.OutputFile = outputZip
        End If

        Return report
    End Function
End Class
