Imports System.IO
Imports System.Text.Json
Imports Moira.CFDEngine
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
    ''' 从体素模型 json 一步构建风洞场景：加载 → 放大定位计算域
    ''' （与 <see cref="WindTunnelSceneBuilder.BuildScene"/> 的下游管线一致）。
    ''' </summary>
    ''' <param name="filePath">Voxelizer CLI 导出的 json 文件路径</param>
    ''' <param name="domainScale">计算空间相对模型 grid 的放大倍数（默认 2）</param>
    ''' <param name="groundClearance">模型最低固体体素离地面 (j=0) 的高度（默认 0，贴地）</param>
    Public Function BuildSceneFromJson(filePath As String,
                                       Optional domainScale As Double = 2.0,
                                       Optional groundClearance As Integer = 0) As WindTunnelScene

        Dim model = FromVoxelFile(filePath)
        Dim domain = VoxelModelLoader.BuildDomain(model, domainScale, groundClearance)

        Return New WindTunnelScene(filePath, model.Width, VoxelizerKind.Standard,
                                   model, domain, domainScale, groundClearance)

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
