Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   FvmTank.vb
'
'   发酵罐几何构建器（FVM 有限体积路径）—— 标准 Rushton 通气搅拌罐的体素化。
'
'   ★ 与 CFDEngine 的 FermentationTank（StableFluids 时间步进容器）职责不同：
'       CFDEngine.FermentationTank = 容器 + StableFluidsSolver + StepForward(dt)
'       本类                        = 几何体素化 + 统一混合精度 FluidField 初始化
'     两者不可合并，故本类以 FvmTank 命名以消除同名类型冲突。
'
'   几何约定（体素坐标，z 自下而上）：
'       - 圆柱罐体：中心 (cx, cy)，半径 R，液面高度 H（顶部 2 层留作
'         自由液面出流区）
'       - 4 块挡板：宽 T/12，贴壁
'       - Rushton 涡轮：直径 D = T/3，离底 C = T/3，桨叶为薄板体素
'       - 气体分布环：半径 T/4，位于桨正下方
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine

''' <summary>
''' 通气搅拌发酵罐：几何体素化 + 混合精度场初始化。
''' 提供 Snapshot 元数据所需的 Field / Viscosity / Diffusion / Stirrer 属性。
''' </summary>
Public Class FvmTank

    ''' <summary>罐直径 T（物理单位 m）。</summary>
    Public ReadOnly Property TankDiameter As Double

    ''' <summary>液位高度 H（物理单位 m）。</summary>
    Public ReadOnly Property LiquidHeight As Double

    ''' <summary>桨叶直径 D（物理单位 m）。</summary>
    Public ReadOnly Property ImpellerDiameter As Double

    ''' <summary>桨离底高度 C（物理单位 m）。</summary>
    Public ReadOnly Property ImpellerClearance As Double

    ''' <summary>网格 X 方向体素数。</summary>
    Public ReadOnly Property Nx As Integer
    ''' <summary>网格 Y 方向体素数。</summary>
    Public ReadOnly Property Ny As Integer
    ''' <summary>网格 Z 方向体素数。</summary>
    Public ReadOnly Property Nz As Integer

    ''' <summary>体素边长 dx（物理单位 m）。</summary>
    Public ReadOnly Property Dx As Double

    ''' <summary>罐中心 X（体素坐标）。</summary>
    Public ReadOnly Property CenterX As Double
    ''' <summary>罐中心 Y（体素坐标）。</summary>
    Public ReadOnly Property CenterY As Double
    ''' <summary>罐半径（体素坐标）。</summary>
    Public ReadOnly Property Radius As Double
    ''' <summary>液面 Z 上界（体素坐标，开区间）。</summary>
    Public ReadOnly Property LiquidTop As Integer

    ''' <summary>桨中心 Z（体素坐标）。</summary>
    Public ReadOnly Property ImpellerZ As Double
    ''' <summary>桨叶半径（体素坐标）。</summary>
    Public ReadOnly Property ImpellerRadius As Double
    ''' <summary>桨叶半高（体素坐标）。</summary>
    Public ReadOnly Property ImpellerHalfHeight As Double

    ''' <summary>分布环半径（体素坐标）。</summary>
    Public ReadOnly Property SpargerRadius As Double
    ''' <summary>分布环 Z（体素坐标）。</summary>
    Public ReadOnly Property SpargerZ As Integer

    ''' <summary>挡板数。</summary>
    Public ReadOnly Property BaffleCount As Integer

    ''' <summary>混合精度流体场（Single 主存储 + 惰性 Double 镜像）。</summary>
    Public ReadOnly Property Field As FluidField

    ''' <summary>运动粘度 ν（物理单位 m²/s）。</summary>
    Public ReadOnly Property Viscosity As Double

    ''' <summary>示踪剂/溶质扩散系数（物理单位 m²/s）。</summary>
    Public ReadOnly Property Diffusion As Double

    ''' <summary>搅拌器（Snapshot 元数据）。</summary>
    Public ReadOnly Property Stirrer As Stirrer

    ''' <summary>体素几何掩膜（含固体 / 桨盘区 / 分布环扩展掩膜）。</summary>
    Public ReadOnly Property VoxelShape As VoxelShape

    ''' <summary>
    ''' 构建标准通气 Rushton 发酵罐。
    ''' </summary>
    ''' <param name="nx">X 体素数（罐直径方向）</param>
    ''' <param name="ny">Y 体素数</param>
    ''' <param name="nz">Z 体素数（含液面上方出流层）</param>
    ''' <param name="viscosity">液相运动粘度 m²/s</param>
    ''' <param name="diffusion">溶质扩散系数 m²/s</param>
    ''' <param name="rpm">搅拌转速 rev/min</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   Optional viscosity As Double = 0.000001R,
                   Optional diffusion As Double = 0.000000002R,
                   Optional rpm As Double = 120.0R)

        Me.Nx = nx
        Me.Ny = ny
        Me.Nz = nz
        Me.Viscosity = viscosity
        Me.Diffusion = diffusion

        ' ---- 物理几何（SI）----
        TankDiameter = 1.0R                        ' T = 1 m
        LiquidHeight = TankDiameter * 1.2R          ' H = 1.2 T
        ImpellerDiameter = TankDiameter / 3.0R     ' Rushton D = T/3
        ImpellerClearance = TankDiameter / 3.0R    ' C = T/3
        BaffleCount = 4

        ' ---- 网格映射 ----
        Dx = TankDiameter / nx                       ' 立方体素
        CenterX = (nx - 1) / 2.0R
        CenterY = (ny - 1) / 2.0R
        Radius = (TankDiameter / 2.0R) / Dx          ' 体素单位半径
        LiquidTop = CInt(System.Math.Floor(LiquidHeight / Dx))   ' 液面层数

        ImpellerZ = (ImpellerClearance / Dx) + 1.0R  ' 桨中心高度（体素）
        ImpellerRadius = (ImpellerDiameter / 2.0R) / Dx
        ImpellerHalfHeight = System.Math.Max(1.0R, ImpellerDiameter / 5.0R / Dx)
        SpargerRadius = (TankDiameter / 4.0R) / Dx
        SpargerZ = CInt(System.Math.Floor(ImpellerZ - 2.5R))

        ' ---- 搅拌器（元数据） ----
        Stirrer = New Stirrer With {
            .CenterX = CenterX,
            .CenterY = CenterY,
            .ZCenter = ImpellerZ,
            .Radius = ImpellerRadius,
            .Height = ImpellerHalfHeight * 2.0R,
            .AngularVelocity = rpm / 60.0R * 2.0R * System.Math.PI,
            .AxialVelocity = 0.0R
        }

        ' ---- 体素化 ----
        Dim n = nx * ny * nz
        Dim active = New Boolean(n - 1) {}
        Dim solids = New Boolean(n - 1) {}
        Dim impellerZone = New Boolean(n - 1) {}
        Dim spargerZone = New Boolean(n - 1) {}

        Dim baffleWidth = TankDiameter / 12.0R / Dx   ' 挡板宽 T/12（体素）

        For i = 0 To nx - 1
            For j = 0 To ny - 1
                Dim x = i - CenterX
                Dim y = j - CenterY
                Dim rr = System.Math.Sqrt(x * x + y * y)

                For k = 0 To nz - 1
                    Dim idx = i * (ny * nz) + j * nz + k

                    ' 圆柱罐内（壁面留 1 体素壁厚），液面以下为液体
                    Dim inCylinder = rr <= Radius - 0.51R
                    Dim belowSurface = k <= LiquidTop

                    If Not (inCylinder AndAlso belowSurface) Then Continue For
                    active(idx) = True

                    ' 桨叶区（薄环带：桨半径外圈为叶片作用区）
                    Dim inImpellerBand =
                        rr <= ImpellerRadius AndAlso rr >= ImpellerRadius * 0.25R AndAlso
                        System.Math.Abs(k - ImpellerZ) <= ImpellerHalfHeight
                    If inImpellerBand Then impellerZone(idx) = True

                    ' 桨毂/轴（细圆柱固体）：中心 r < 0.6 体素视为轴
                    If rr <= 0.6R AndAlso belowSurface Then solids(idx) = True

                    ' 分布环：薄圆环
                    Dim dRing = System.Math.Abs(rr - SpargerRadius)
                    If dRing <= 0.75R AndAlso System.Math.Abs(k - SpargerZ) <= 0.51R Then
                        spargerZone(idx) = True
                    End If
                Next

                ' 4 块挡板（贴壁、全液高）
                If _baffleAt(i, j, baffleWidth) Then
                    For k = 0 To LiquidTop
                        Dim idx = i * (ny * nz) + j * nz + k
                        solids(idx) = True
                        active(idx) = False
                    Next
                End If
            Next
        Next

        VoxelShape = New VoxelShape(nx, ny, nz, active, solids, impellerZone, spargerZone)
        Field = New FluidField(VoxelShape)

        ' 液相初始密度（Single 主存储；Double 镜像在首次访问时惰性生成）
        Dim rho0 As Single = 1000.0F
        Dim rho = Field.Density.Data
        For idx = 0 To Field.TotalVoxels - 1
            rho(idx) = rho0
        Next
    End Sub

    ''' <summary>4 块挡板位置判定（沿 +X/+Y/−X/−Y 四方向贴壁条带）。</summary>
    Private Function _baffleAt(i As Integer, j As Integer, baffleWidth As Double) As Boolean
        Dim x = i - CenterX
        Dim y = j - CenterY
        Dim rr = System.Math.Sqrt(x * x + y * y)
        ' 挡板位于半径外 20% 的贴壁环带内
        If rr < Radius * 0.8R Then Return False
        ' 条带方向判定：与坐标轴对齐的 4 条
        Dim along = baffleWidth * 0.5R
        If System.Math.Abs(y) <= along Then Return True    ' +X / -X 两块
        If System.Math.Abs(x) <= along Then Return True    ' +Y / -Y 两块
        Return False
    End Function

End Class
