
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