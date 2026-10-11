
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

    ''' <summary>
    ''' 原始精度物理尺寸直接体素化：绕开 resolution 整数量化限制，
    ''' 按用户指定的物理体素尺寸直接调用 Voxelizer.Voxelize(scene, voxelSize)。
    ''' voxelSize 参数由调用方给定（0 时退化为按 resolution 量化），
    ''' 可取任意小值（网格分辨率随之任意高）。
    ''' </summary>
    Physical

    ''' <summary>
    ''' SDF 连续场重采样体素化：先以内部基准分辨率计算连续符号距离场
    ''' （SDFVolume，携带亚体素表面位置），再以任意目标分辨率对连续场
    ''' 三线性插值重采样（solid ⇔ SDF &lt; 0）。
    ''' 实现「CFD 网格分辨率与体素化分辨率解耦」——目标网格可比 SDF
    ''' 计算网格更细，获得真正的连续精度边界。
    ''' </summary>
    SdfRaw
End Enum
