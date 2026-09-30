' /********************************************************************************/
'
'   WindTunnelSceneBuilder.vb
'
'   风洞试验场景构建器 —— 三维模型文件 → 体素化 → CFDEngine 计算空间
'
'   作用：
'       基于 Landscape.vbproj 提供的三维模型统一加载与体素化 API，
'       将任意支持的 3D 模型文件（STL / glTF / GLB / OBJ / DAE / 3DS / 3MF）
'       转换为 CFDEngine 可直接消费的计算空间，用于 WindTunnel 风洞试验：
'
'           模型文件 --ModelLoader.LoadModel--> SceneModel
'                   --Voxelizer / SDFVoxelizer--> Landscape VoxelModel (True=固体)
'                   --语义反转转换--------------> CFDEngine VoxelModel (True=流体)
'                   --VoxelModelLoader.BuildDomain--> 放大定位后的风洞计算域
'
'   类型消歧说明（重要）：
'       VoxelModel 同时存在于两个库中且布尔语义相反：
'           - Microsoft.VisualBasic.Imaging.Landscape.Voxelization.VoxelModel
'               True  = 固体区域
'           - Moira.CFDEngine 根命名空间 VoxelModel（VoxelModelLoader.vb）
'               True  = 活动流体体素（Shape 为 VoxelShape，固体为 False 障碍）
'       本模块通过 Imports 别名 LxVoxel 指代 Landscape 版本，
'       未加别名的 VoxelModel 一律指 CFDEngine 版本。
'
' /********************************************************************************/

Imports System.IO
Imports Microsoft.VisualBasic.Imaging.Landscape.Data
Imports Microsoft.VisualBasic.Imaging.Landscape.Voxelization
Imports LxVoxel = Microsoft.VisualBasic.Imaging.Landscape.Voxelization.VoxelModel

''' <summary>
''' 体素化器实现选择。
''' </summary>
Public Enum VoxelizerKind

    ''' <summary>
    ''' 标准列扫描射线投射体素化（Voxelizer.Voxelize），速度快，默认选项。
    ''' 要求模型水密 (watertight)。
    ''' </summary>
    Standard

    ''' <summary>
    ''' SDF 符号距离场体素化（SDFVoxelizer.Voxelize），带亚采样抗锯齿，
    ''' 边界质量更好但更耗时。
    ''' </summary>
    Sdf
End Enum

''' <summary>
''' 风洞试验场景构建器。
'''
''' 提供从 3D 模型文件到 CFDEngine 计算空间的一站式转换：
'''   - <see cref="FromModelFile"/>：加载 + 体素化，返回 CFDEngine 语义的 VoxelModel
'''   - <see cref="BuildScene"/>：加载 + 体素化 + 放大定位，一步构建 <see cref="WindTunnelScene"/>
''' </summary>
Public Module WindTunnelSceneBuilder

#Region "加载与体素化"

    ''' <summary>
    ''' 从文件加载 3D 模型并体素化，转换为 CFDEngine 语义的体素模型
    ''' （True = 活动流体，固体障碍为 False）。
    ''' </summary>
    ''' <param name="filePath">3D 模型文件路径（按扩展名自动识别格式）</param>
    ''' <param name="resolution">最长边上的体素分辨率（建议 32~64，避免放大后计算域过大）</param>
    ''' <param name="voxelizer">体素化器实现（默认标准 Voxelizer）</param>
    ''' <param name="subSamples">SDF 体素化器的每轴亚采样数（仅 voxelizer=Sdf 时生效）</param>
    Public Function FromModelFile(filePath As String,
                                  Optional resolution As Integer = 48,
                                  Optional voxelizer As VoxelizerKind = VoxelizerKind.Standard,
                                  Optional subSamples As Integer = 2) As VoxelModel

        If String.IsNullOrEmpty(filePath) Then
            Throw New ArgumentException("模型文件路径不能为空", NameOf(filePath))
        End If
        If Not File.Exists(filePath) Then
            Throw New FileNotFoundException($"找不到模型文件：{filePath}", filePath)
        End If
        If resolution < 4 Then
            Throw New ArgumentException("resolution 不能小于 4", NameOf(resolution))
        End If

        ' ---- 1. 统一加载为 SceneModel（按扩展名自动检测格式）----
        Dim scene As SceneModel = filePath.LoadModel
        If scene Is Nothing OrElse scene.Surfaces Is Nothing OrElse scene.Surfaces.Length = 0 Then
            Throw New InvalidDataException($"模型加载失败或不含几何表面：{filePath}")
        End If

        ' ---- 2. 体素化（得到 Landscape 语义：True = 固体）----
        Dim lx As LxVoxel
        Select Case voxelizer
            Case VoxelizerKind.Sdf
                lx = SDFVoxelizer.Voxelize(scene, resolution, subSamples)
            Case Else
                lx = Voxelizer.Voxelize(scene, resolution)
        End Select

        If lx Is Nothing OrElse lx.Shape Is Nothing Then
            Throw New InvalidDataException($"模型体素化失败（返回空体素网格）：{filePath}")
        End If

        ' ---- 3. 语义反转：Landscape True(固体) → CFDEngine False(障碍) ----
        Return ToCfdVoxelModel(lx, filePath)

    End Function

    ''' <summary>
    ''' 将 Landscape 体素模型（True=固体）转换为 CFDEngine 语义的体素模型
    ''' （True=流体），并补齐固体体素数 / 固体包围盒等元数据。
    ''' </summary>
    ''' <param name="lx">Landscape 体素化结果</param>
    ''' <param name="sourceModel">源模型文件路径（元数据记录用）</param>
    Public Function ToCfdVoxelModel(lx As LxVoxel, Optional sourceModel As String = Nothing) As VoxelModel
        If lx Is Nothing Then Throw New ArgumentNullException(NameOf(lx))
        If lx.Shape Is Nothing OrElse lx.Shape.Length <> lx.Width * lx.Height * lx.Depth Then
            Throw New InvalidDataException(
                $"Landscape 体素模型数据非法：Shape.Length 与 {lx.Width}x{lx.Height}x{lx.Depth} 不一致")
        End If

        Dim w = lx.Width, h = lx.Height, d = lx.Depth

        ' Landscape 布尔数组 → 0/1 整数数组（固体=1），
        ' 交给 VoxelModelLoader.FromVoxelArray 做 "1=固体→False 障碍" 的反转映射。
        ' 顺带统计固体体素数与固体包围盒（约定与 VoxelModelLoader.Load 一致）。
        Dim n = w * h * d
        Dim data(n - 1) As Integer
        Dim solidCount As Integer = 0
        Dim minX = Integer.MaxValue, minY = Integer.MaxValue, minZ = Integer.MaxValue
        Dim maxX = -1, maxY = -1, maxZ = -1

        For x = 0 To w - 1
            For y = 0 To h - 1
                For z = 0 To d - 1
                    ' 索引公式两边一致：index = (x * Height + y) * Depth + z
                    Dim idx = (x * h + y) * d + z
                    If lx.Shape(idx) Then
                        data(idx) = 1
                        solidCount += 1
                        If x < minX Then minX = x
                        If y < minY Then minY = y
                        If z < minZ Then minZ = z
                        If x > maxX Then maxX = x
                        If y > maxY Then maxY = y
                        If z > maxZ Then maxZ = z
                    End If
                Next
            Next
        Next
        If solidCount = 0 Then
            minX = -1 : minY = -1 : minZ = -1
        End If

        Dim shape As VoxelShape = VoxelModelLoader.FromVoxelArray(w, h, d, data, solidValue:=1)

        ' Landscape 的体素为等轴测：VoxelSize 三向相同
        Dim vs = If(lx.VoxelSize > 0, lx.VoxelSize, 1.0)
        Dim voxelSize As Double() = {vs, vs, vs}

        Return New VoxelModel With {
            .Shape = shape,
            .Width = w,
            .Height = h,
            .Depth = d,
            .VoxelSize = voxelSize,
            .SourceModel = If(String.IsNullOrEmpty(sourceModel), lx.ToString, sourceModel),
            .SolidVoxelCount = solidCount,
            .SolidBounds = (minX, minY, minZ, maxX, maxY, maxZ)
        }
    End Function

#End Region

#Region "风洞场景一步构建"

    ''' <summary>
    ''' 加载 + 体素化 + 放大定位，一步构建风洞试验场景。
    ''' </summary>
    ''' <param name="filePath">3D 模型文件路径</param>
    ''' <param name="resolution">最长边上的体素分辨率（默认 48）</param>
    ''' <param name="voxelizer">体素化器实现（默认标准 Voxelizer）</param>
    ''' <param name="subSamples">SDF 体素化器的每轴亚采样数（仅 voxelizer=Sdf 时生效）</param>
    ''' <param name="domainScale">计算空间相对模型 grid 的放大倍数（默认 2）</param>
    ''' <param name="groundClearance">模型最低固体体素离地面 (j=0) 的高度（默认 0，贴地）</param>
    Public Function BuildScene(filePath As String,
                               Optional resolution As Integer = 48,
                               Optional voxelizer As VoxelizerKind = VoxelizerKind.Standard,
                               Optional subSamples As Integer = 2,
                               Optional domainScale As Double = 2.0,
                               Optional groundClearance As Integer = 0) As WindTunnelScene

        Dim model = FromModelFile(filePath, resolution, voxelizer, subSamples)
        Dim domain = VoxelModelLoader.BuildDomain(model, domainScale, groundClearance)

        Return New WindTunnelScene(filePath, resolution, voxelizer, model, domain, domainScale, groundClearance)

    End Function

#End Region

End Module
