Imports System.IO
Imports System.Diagnostics
Imports Moira.CFDEngine.Eulerian

''' <summary>
''' Eulerian 网格生成一键流水线入口：
''' LoadModel → 几何预处理 → 网格生成（四类算法）→ FVM 拓扑 → Patch 分类 →
''' （可选 Laplacian 平滑）→ zip 压缩包输出。
''' </summary>
Public Class EulerianPipeline

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

        Public Overrides Function ToString() As String
            Return $"{MeshKind}: {Mesh.ToString}, patches={Patches.Count}, {ElapsedMs} ms" &
                   If(OutputFile IsNot Nothing, $" -> {OutputFile}", "")
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
    Public Shared Function Convert(modelFile As String,
                                   outputZip As String,
                                   type As MeshType,
                                   Optional resolution As Double = 0.05,
                                   Optional domainScale As Double = 2.0,
                                   Optional cutCells As Boolean = False,
                                   Optional poissonIterations As Integer = 0,
                                   Optional smoothLaplacian As Boolean = True,
                                   Optional rules As List(Of PatchRule) = Nothing) As PipelineReport

        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim res As Double = resolution

        ' ---- 1. 几何预处理 ----
        Dim domain As GeometryPreprocessor.FluidDomain =
            GeometryPreprocessor.FromModelFile(modelFile, domainScale:=domainScale)
        Dim modelExtent As Double = (domain.ModelMax - domain.ModelMin).Length

        If res <= 0 Then
            ' 默认：模型跨越约 20 个单元
            res = modelExtent * 0.05
        End If

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
            .ElapsedMs = watch.ElapsedMilliseconds}

        ' ---- 6. zip 输出 ----
        If Not String.IsNullOrEmpty(outputZip) Then
            Call EulerianMeshPackage.Save(mesh, outputZip, generator.Name)
            report.OutputFile = outputZip
        End If

        Return report
    End Function
End Class
