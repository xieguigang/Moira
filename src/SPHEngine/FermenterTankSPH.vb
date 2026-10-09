' /********************************************************************************/
'
'   FermenterTankSPH.vb
'
'   发酵罐搅拌仿真场景（3D SPH）
'
'   几何（世界单位 = 米，Z 轴朝上）：
'       - 圆柱形罐体：半径 TankRadius，高 TankHeight
'       - 发酵液液面：FillFraction * TankHeight（默认 3/4 罐高）
'       - 搅拌桨：罐底附近（默认 0.15 罐高）的 Rushton 式圆盘涡轮，
'         半径默认 0.35 * TankRadius，绕罐轴以给定转速旋转
'       - 搅拌轴：从桨盘向上的细圆柱
'
'   计算链路：
'       FluidEngine3D（physics-netcore5，优化后的双密度 SPH）
'           -> SphState3D（粒子：位置 / 速度 / 密度 / 压力）
'           -> SphFieldSampler（核加权采样）
'           -> FluidField + VoxelShape.Cylinder（规则体素场）
'           -> VtiSnapshotRecorder（.vti + animation.pvd + frames.json + metadata.json）
'
' /********************************************************************************/

Imports std = System.Math
Imports Microsoft.VisualBasic.Imaging.Physics
Imports Moira.CFDEngine
Imports Moira.CFDEngine.Snapshot
Imports Moira.CFDEngine.Snapshot.JSON

''' <summary>
''' A stirred fermentation tank simulated with the optimized 3D SPH solver.
''' </summary>
Public Class FermenterTankSPH

#Region "几何"

    ''' <summary>inner radius of the vessel [m]</summary>
    Public ReadOnly Property TankRadius As Double
    ''' <summary>height of the vessel [m]</summary>
    Public ReadOnly Property TankHeight As Double
    ''' <summary>liquid level as a fraction of the vessel height (default 3/4)</summary>
    Public ReadOnly Property FillFraction As Double
    ''' <summary>height of the fermentation broth [m]</summary>
    Public ReadOnly Property LiquidHeight As Double
        Get
            Return TankHeight * FillFraction
        End Get
    End Property

    ''' <summary>particle spacing of the initial fill [m]</summary>
    Public ReadOnly Property Spacing As Double
    ''' <summary>SPH smoothing radius (2 * spacing) [m]</summary>
    Public ReadOnly Property SmoothingRadius As Double

#End Region

#Region "组件"

    ''' <summary>the voxel model of the tank interior</summary>
    Public ReadOnly Property Shape As VoxelShape
    ''' <summary>the sampled grid field that is handed to the snapshot exporter</summary>
    Public ReadOnly Property Field As FluidField
    ''' <summary>the SPH solver</summary>
    Public ReadOnly Property Engine As FluidEngine3D
    ''' <summary>the rotating impeller (moving boundary)</summary>
    Public ReadOnly Property Impeller As SphImpeller3D
    ''' <summary>particle state to voxel grid sampler</summary>
    Public ReadOnly Property Sampler As SphFieldSampler

    ''' <summary>voxel size [m]</summary>
    Public ReadOnly Property VoxelSizeX As Double
    ''' <summary>voxel size [m]</summary>
    Public ReadOnly Property VoxelSizeZ As Double

#End Region

    ''' <summary>number of SPH particles</summary>
    Public ReadOnly Property ParticleCount As Integer
        Get
            Return Engine.Count
        End Get
    End Property

    ''' <summary>simulated time [s] (kept in double precision for clean frame stamps)</summary>
    Public ReadOnly Property Time As Double
        Get
            Return _time
        End Get
    End Property

    Private _time As Double = 0.0

    ''' <summary>number of executed output steps</summary>
    Public ReadOnly Property StepCount As Integer
        Get
            Return Engine.StepCount
        End Get
    End Property

    ''' <summary>the solver description written into metadata.json</summary>
    Public Property SolverName As String =
        "3D SPH (Clavet double density, symmetric pressure, CFL sub stepping)"

#Region "构造"

    ''' <summary>
    ''' build the fermenter scenario.
    ''' </summary>
    ''' <param name="tankRadius">inner radius [m]</param>
    ''' <param name="tankHeight">vessel height [m]</param>
    ''' <param name="fillFraction">liquid level / vessel height (0.75 = 3/4)</param>
    ''' <param name="particles">target number of SPH particles</param>
    ''' <param name="rpm">impeller rotation speed</param>
    ''' <param name="nx">voxels along x and y</param>
    ''' <param name="voxelSizeZ">
    ''' voxel size along z; 0 derives it from x so that the voxels stay cubic
    ''' </param>
    ''' <param name="viscosity">SPH velocity relaxation rate [1/s]</param>
    ''' <param name="soundSpeedFactor">artificial sound speed / reference velocity</param>
    ''' <param name="impellerRadiusRatio">impeller radius / tank radius</param>
    ''' <param name="impellerHeightRatio">impeller height / vessel height</param>
    ''' <param name="seed">RNG seed of the initial particle jitter</param>
    Public Sub New(Optional tankRadius As Double = 0.15,
                   Optional tankHeight As Double = 0.45,
                   Optional fillFraction As Double = 0.75,
                   Optional particles As Integer = 15000,
                   Optional rpm As Double = 150.0,
                   Optional nx As Integer = 40,
                   Optional voxelSizeZ As Double = 0.0,
                   Optional viscosity As Single = 4.0F,
                   Optional soundSpeedFactor As Single = 8.0F,
                   Optional impellerRadiusRatio As Double = 0.35,
                   Optional impellerHeightRatio As Double = 0.09,
                   Optional seed As Integer = 1024)

        Me.TankRadius = tankRadius
        Me.TankHeight = tankHeight
        Me.FillFraction = fillFraction

        ' ---- 体素网格 ----
        Dim dx = 2 * tankRadius / nx
        Dim nz As Integer

        If voxelSizeZ > 0 Then
            nz = std.Max(4, CInt(std.Round(tankHeight / voxelSizeZ)))
        Else
            nz = std.Max(4, CInt(std.Round(tankHeight / dx)))
        End If

        Dim dz = tankHeight / nz
        Me.VoxelSizeX = dx
        Me.VoxelSizeZ = dz

        Me.Shape = VoxelShape.Cylinder(nx, nx, nz,
                                       radius:=nx / 2.0,
                                       centerX:=(nx - 1) / 2.0,
                                       centerY:=(nx - 1) / 2.0,
                                       bottomZ:=0,
                                       topZ:=nz - 1)
        Me.Field = New FluidField(Shape)

        ' ---- 粒子间距 ----
        Dim liquidVolume = std.PI * tankRadius * tankRadius * LiquidHeight
        Dim s = std.Pow(liquidVolume / std.Max(1, particles), 1.0 / 3.0)
        Me.Spacing = s
        Me.SmoothingRadius = 2.0 * s

        ' ---- 边界与求解器 ----
        Dim boundary As New CylinderBoundary3D(CSng(tankRadius), CSng(tankRadius),
                                               CSng(tankRadius), 0.0F, CSng(tankHeight)) With {
            .WallFriction = 0.2F
        }

        Dim engine As New FluidEngine3D(boundary, CSng(SmoothingRadius))
        Dim impeller As New SphImpeller3D(CSng(tankRadius), CSng(tankRadius),
                                          CSng(tankHeight * impellerHeightRatio),
                                          CSng(tankRadius * impellerRadiusRatio),
                                          CSng(std.Max(2.0 * s, tankHeight * 0.02)),
                                          0.0F)
        impeller.SetRpm(CSng(rpm))
        impeller.ShaftRadius = CSng(tankRadius * 0.06)
        impeller.ShaftTop = CSng(tankHeight)
        impeller.RadialPumping = 0.35F
        impeller.Influence = CSng(std.Max(2.5 * s, tankRadius * 0.05))
        impeller.BlendRate = 40.0F

        ' ---- 填充粒子 ----
        Call FillLiquid(engine, seed)

        engine.Gravity = 9.81F
        engine.GravityDirection = New Vector3(0, 0, -1)
        engine.ParticleSpacing = CSng(s)
        engine.WallPadding = CSng(0.5 * s)
        engine.ViscosityStrength = viscosity
        engine.SoundSpeedFactor = soundSpeedFactor
        engine.Impeller = impeller

        ' 参考速度 = 自由下落速度 + 桨尖线速度（用于推导人工声速）
        Dim tip = std.Abs(impeller.AngularVelocity * impeller.Radius)
        engine.ReferenceSpeed = CSng(std.Sqrt(2 * 9.81 * LiquidHeight) + tip)
        engine.MaxVelocity = CSng(3.0 * engine.ReferenceSpeed)
        engine.MaxAccel = CSng(30.0 * 9.81)
        engine.MaxSubSteps = 128
        engine.CflFactor = 0.35F

        ' 静止点阵上标定 rest density（只取远离壁面 / 液面的内部粒子，
        ' 避免自由液面与壁面的核亏损把静止密度标定偏低），再按标定结果重建压力刚度
        Dim st = engine.State
        Dim Rf = CSng(tankRadius)
        Dim hf = CSng(SmoothingRadius)
        Dim liquidTop = CSng(LiquidHeight)

        Call engine.CalibrateDensity(
            Function(i)
                Dim dx = st.px(i) - Rf
                Dim dy = st.py(i) - Rf
                Dim rr = dx * dx + dy * dy
                Dim lim = Rf - 1.2F * hf

                Return rr <= lim * lim AndAlso
                       st.pz(i) >= 1.2F * hf AndAlso
                       st.pz(i) <= liquidTop - 1.2F * hf
            End Function)

        Me.Engine = engine
        Me.Impeller = impeller
        Me.Sampler = New SphFieldSampler(Shape, CSng(SmoothingRadius),
                                         0.0F, 0.0F, 0.0F,
                                         CSng(dx), CSng(dx), CSng(dz))

        ' 体素空间的静止填充基准：核支撑在壁面 / 液面处被截断，体素采样值
        ' 会系统性地低于粒子静止密度。这里测一份"初始静止液面"的逐体素核和
        ' 作为参考，使液体内部恒为 1.0、空气为 0，便于可视化时直接切阈值。
        _restMap = Sampler.MeasureRest(Engine.State, Field)
        Call SampleField()
    End Sub

    ''' <summary>per voxel reference kernel sum of the initial broth fill</summary>
    Public ReadOnly Property RestMap As Single()
        Get
            Return _restMap
        End Get
    End Property

    Private ReadOnly _restMap As Single()

    ''' <summary>
    ''' fill the broth volume with a slightly jittered regular particle lattice.
    ''' </summary>
    Private Sub FillLiquid(engine As FluidEngine3D, seed As Integer)
        Dim rnd As New Random(seed)
        Dim s = CSng(Spacing)
        Dim R = CSng(TankRadius)
        Dim pad = CSng(0.55 * Spacing)
        Dim innerR = R - pad
        Dim liquidH = CSng(LiquidHeight)
        Dim jitter = 0.06F * s

        Dim list As New List(Of Single())()
        Dim z As Single = pad

        While z <= liquidH - pad * 0.5F
            Dim y As Single = pad

            While y <= 2 * R - pad
                Dim x As Single = pad

                While x <= 2 * R - pad
                    Dim dx = x - R
                    Dim dy = y - R

                    If dx * dx + dy * dy <= innerR * innerR Then
                        list.Add(New Single() {
                            x + CSng((rnd.NextDouble() - 0.5) * 2 * jitter),
                            y + CSng((rnd.NextDouble() - 0.5) * 2 * jitter),
                            z + CSng((rnd.NextDouble() - 0.5) * 2 * jitter)
                        })
                    End If

                    x += s
                End While

                y += s
            End While

            z += s
        End While

        Dim n = list.Count
        Dim state = engine.State

        Call state.EnsureCapacity(n)

        For i As Integer = 0 To n - 1
            state.px(i) = list(i)(0)
            state.py(i) = list(i)(1)
            state.pz(i) = list(i)(2)
        Next

        Call state.ClearDynamic()
        Call engine.SetParticleCount(n)
    End Sub

#End Region

#Region "推进与采样"

    ''' <summary>advance the simulation by one output step</summary>
    Public Sub StepForward(dt As Double)
        Call Engine.RunStep(CSng(dt))
        _time += dt
    End Sub

    ''' <summary>sample the particles into <see cref="Field"/></summary>
    Public Sub SampleField()
        ' GPU 后端在 FullSync=False 时标量场驻留显存，采样前同步一次
        Dim cuda = TryCast(Engine.Backend, CudaSphBackend)

        If cuda IsNot Nothing AndAlso Not cuda.FullSync Then
            Call cuda.SyncFields(Engine.State)
        End If

        Call Sampler.Sample(Engine.State, Field, Engine.RestDensity, _restMap)
    End Sub

    ''' <summary>
    ''' run the simulation and write the .vti snapshot series.
    ''' </summary>
    ''' <param name="steps">number of output steps</param>
    ''' <param name="dt">simulated time of one output step [s]</param>
    ''' <param name="interval">capture one frame every N steps</param>
    ''' <param name="outputDir">output folder of the snapshot series</param>
    ''' <param name="onStep">optional progress callback (step, time, maxSpeed)</param>
    Public Sub Run(steps As Integer, dt As Double, interval As Integer, outputDir As String,
                   Optional onStep As Action(Of Integer, Double, Double) = Nothing)

        If interval < 1 Then interval = 1

        Dim meta = SnapshotMetadata.FromField(
            Field,
            viscosity:=Engine.ViscosityStrength,
            diffusion:=0.0,
            dt:=dt,
            solver:=SolverName)

        Dim recorder As New VtiSnapshotRecorder(
            outputDir,
            baseName:="frame",
            interval:=interval,
            pvdName:="animation.pvd",
            estimatedFrames:=steps \ interval + 2,
            metadata:=meta)

        ' 初始帧（静止液面）
        Call SampleField()
        Call recorder.Capture(Field, 0, 0.0)

        For s As Integer = 1 To steps
            Call StepForward(dt)

            If (s Mod interval) = 0 OrElse s = steps Then
                Call SampleField()
                Call recorder.Capture(Field, Engine.StepCount, _time)
            End If

            If onStep IsNot Nothing Then
                Call onStep(s, _time, MaxSpeed())
            End If
        Next

        Call recorder.Finish()
    End Sub

#End Region

#Region "统计"

    ''' <summary>largest particle speed [m/s]</summary>
    Public Function MaxSpeed() As Double
        Return Engine.State.MaxSpeed()
    End Function

    ''' <summary>mean normalized density (1 = rest density of the broth)</summary>
    Public Function MeanNormalizedDensity() As Double
        Dim rest = Engine.RestDensity
        If rest <= 0 Then Return 0
        Return Engine.State.MeanDensity() / rest
    End Function

    ''' <summary>mean particle speed [m/s]</summary>
    Public Function MeanSpeed() As Double
        Dim st = Engine.State
        Dim n = st.Count
        If n = 0 Then Return 0

        Dim sum As Double = 0

        For i As Integer = 0 To n - 1
            sum += std.Sqrt(st.vx(i) * st.vx(i) + st.vy(i) * st.vy(i) + st.vz(i) * st.vz(i))
        Next

        Return sum / n
    End Function

    ''' <summary>a one line summary of the current state</summary>
    Public Function Summary() As String
        Return $"t={Time:F3}s  step={StepCount}  particles={ParticleCount}  " &
               $"maxSpeed={MaxSpeed():F3} m/s  meanSpeed={MeanSpeed():F3} m/s  " &
               $"meanRho={MeanNormalizedDensity():F4}  " &
               $"subSteps={Engine.LastSubSteps}  c={Engine.EffectiveSoundSpeed:F2} m/s"
    End Function

#End Region

End Class
