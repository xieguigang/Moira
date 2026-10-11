Imports System.IO
Imports System.Text.Json
Imports Moira.CFDEngine
Imports Moira.CFDEngine.Snapshot
Imports Moira.CFDEngine.Snapshot.JSON

' /********************************************************************************/
'
'   VoxelJsonLoader.vb
'
'   体素模型 JSON 加载器 —— 消费 Voxelizer 命令行工具导出的
'   GridInfo（CFDEngine/Snapshot/JSON/Data.vb）形状 json 文件：
'
'       Voxelizer.exe city.stl -o city.voxel.json --resolution 96
'       scene = WindTunnelVoxelJson.BuildSceneFromJson("city.voxel.json")
'       solver = WindTunnelSolver.FromVoxelShape(scene.Domain, ...)
'
'   预体素化工作流：把高分辨率体素化从 WindTunnelBox 的 GUI 启动路径中
'   移出到独立命令行进程，GUI 直接读 json 做模拟计算。
'
'   GridInfo.Mask 语义（与 CFDEngine 约定一致）：
'       1 = 活动流体体素，0 = 固体障碍 —— 本加载器反转回
'       CFDEngine VoxelModel 语义（True = 流体）。
'
' /********************************************************************************/

''' <summary>
''' 体素模型 JSON 加载器（Voxelizer CLI 导出文件 → CFDEngine 计算空间）。
''' </summary>
Public Module WindTunnelVoxelJson

    ''' <summary>
    ''' 从 Voxelizer CLI 导出的 json 文件加载 CFDEngine 语义的体素模型
    ''' （True = 活动流体，固体障碍为 False），并补齐元数据。
    ''' </summary>
    ''' <param name="filePath">GridInfo json 文件路径</param>
    Public Function FromVoxelJson(filePath As String) As VoxelModel
        If String.IsNullOrEmpty(filePath) Then
            Throw New ArgumentException("json 文件路径不能为空", NameOf(filePath))
        End If
        If Not File.Exists(filePath) Then
            Throw New FileNotFoundException($"找不到体素模型 json 文件：{filePath}", filePath)
        End If

        Dim grid As GridInfo = JsonSerializer.Deserialize(Of GridInfo)(File.ReadAllText(filePath))

        If grid Is Nothing OrElse grid.Mask Is Nothing Then
            Throw New InvalidDataException($"体素模型 json 解析失败（缺少 grid.mask）：{filePath}")
        End If

        Dim w = grid.Width, h = grid.Height, d = grid.Depth
        Dim n = w * h * d

        If grid.Mask.Length <> n Then
            Throw New InvalidDataException(
                $"json 数据非法：Mask.Length={grid.Mask.Length} 与 {w}x{h}x{d}={n} 不一致：{filePath}")
        End If

        ' Mask(1=流体, 0=固体) → 整数数组(1=固体)，交给 FromVoxelArray 做反转映射
        Dim data(n - 1) As Integer
        Dim solidCount As Integer = 0
        Dim minX = Integer.MaxValue, minY = Integer.MaxValue, minZ = Integer.MaxValue
        Dim maxX = -1, maxY = -1, maxZ = -1

        For idx As Integer = 0 To n - 1
            If grid.Mask(idx) = 0 Then
                ' 0 = 固体障碍
                data(idx) = 1
                solidCount += 1

                Dim k As Integer = idx Mod d
                Dim remd As Integer = idx \ d
                Dim j As Integer = remd Mod h
                Dim xx As Integer = remd \ h

                If xx < minX Then minX = xx
                If j < minY Then minY = j
                If k < minZ Then minZ = k
                If xx > maxX Then maxX = xx
                If j > maxY Then maxY = j
                If k > maxZ Then maxZ = k
            End If
        Next

        If solidCount = 0 Then
            minX = -1 : minY = -1 : minZ = -1
        End If

        Dim shape As VoxelShape = VoxelModelLoader.FromVoxelArray(w, h, d, data, solidValue:=1)

        ' GridInfo 的 Spacing 为等轴测：VoxelSize 三向相同
        Dim vs As Double = If(grid.Spacing IsNot Nothing AndAlso grid.Spacing.Length = 3,
                              grid.Spacing(0), 1.0)

        Return New VoxelModel With {
            .Shape = shape,
            .Width = w,
            .Height = h,
            .Depth = d,
            .VoxelSize = {vs, vs, vs},
            .SourceModel = filePath,
            .SolidVoxelCount = solidCount,
            .SolidBounds = (minX, minY, minZ, maxX, maxY, maxZ)
        }
    End Function

    ''' <summary>
    ''' LBM 模拟可行域体素预算（默认 1 亿格点）：LbmCoreF 的 f/fc 为 19×N 的
    ''' Single 数组，N 硬上限 ≈ 1.13 亿；1 亿为留有余量的模拟可行预算。
    ''' </summary>
    Public Const DefaultDomainBudget As Long = 100_000_000L

    ''' <summary>
    ''' 从体素模型文件一步构建风洞场景：加载 → 自动收缩 domainScale →
    ''' 放大定位计算域（与 <see cref="WindTunnelSceneBuilder.BuildScene"/> 的
    ''' 下游管线一致）。
    ''' </summary>
    ''' <param name="filePath">Voxelizer CLI 导出的体素模型文件（.voxel.zip / .json）</param>
    ''' <param name="domainScale">期望的计算空间放大倍数（默认 2；超出预算时自动收缩）</param>
    ''' <param name="groundClearance">模型最低固体体素离地面 (j=0) 的高度（默认 0，贴地）</param>
    ''' <param name="budgetVoxels">
    ''' 计算域体素预算（默认 1 亿）。自动收缩后仍超预算时抛出带指引的异常。
    ''' </param>
    Public Function BuildSceneFromJson(filePath As String,
                                       Optional domainScale As Double = 2.0,
                                       Optional groundClearance As Integer = 0,
                                       Optional budgetVoxels As Long = DefaultDomainBudget) As WindTunnelScene

        Dim model = FromVoxelFile(filePath)
        Dim effectiveScale As Double = ResolveDomainScale(model, domainScale, budgetVoxels)
        Dim domain = VoxelModelLoader.BuildDomain(model, effectiveScale, groundClearance)

        Dim scene = New WindTunnelScene(filePath, model.Width, VoxelizerKind.Standard,
                                        model, domain, effectiveScale, groundClearance)
        Return scene

    End Function

    ''' <summary>
    ''' 解析实际可用的 domainScale：从请求值以 0.1 步长下调（下限 1.0），
    ''' 取使放大后域体素数 ≤ <paramref name="budgetVoxels"/> 的最大倍数。
    ''' scale = 1.0 仍超预算时抛出带「重新体素化」指引的异常。
    ''' </summary>
    Public Function ResolveDomainScale(model As VoxelModel,
                                       requestedScale As Double,
                                       budgetVoxels As Long) As Double

        Dim scale As Double = Math.Min(Math.Max(requestedScale, 1.0), 4.0)
        Dim modelVoxels As Long = CLng(model.Width) * model.Height * model.Depth

        ' 逐步下调（步长 0.1）直到域体素数落入预算
        While scale > 1.0 + 0.05
            Dim domainVoxels As Long =
                CLng(Math.Ceiling(model.Width * scale)) *
                CLng(Math.Ceiling(model.Height * scale)) *
                CLng(Math.Ceiling(model.Depth * scale))

            If domainVoxels <= budgetVoxels Then
                Return scale
            End If

            scale = Math.Max(1.0, scale - 0.1)
        End While

        Dim baseVoxels As Long =
            CLng(Math.Ceiling(model.Width)) *
            CLng(Math.Ceiling(model.Height)) *
            CLng(Math.Ceiling(model.Depth))

        If baseVoxels > budgetVoxels Then
            Throw New InvalidOperationException(
                $"体素模型格点数超出模拟预算：{model.Width}x{model.Height}x{model.Depth} = " &
                $"{baseVoxels:N0} > {budgetVoxels:N0}（domainScale 已收缩至下限 1.0）。" &
                "LBM 求解器内存/数组长度无法承载该规模，" &
                "请用更低分辨率重新体素化（Voxelizer CLI：--resolution ≤ 128）后再加载。")
        End If

        Return 1.0
    End Function

    ''' <summary>
    ''' 按扩展名分派加载：*.zip → 二进制体素包（VoxelPackage 流式读取），
    ''' 其余 → GridInfo JSON。
    ''' </summary>
    Public Function FromVoxelFile(filePath As String) As VoxelModel
        If Path.GetExtension(filePath).ToLowerInvariant() = ".zip" Then
            ' 二进制体素包：CFDEngine 流式读取，无 Integer() 中间数组
            Return VoxelPackage.LoadVoxelModel(filePath)
        Else
            Return FromVoxelJson(filePath)
        End If
    End Function

End Module
