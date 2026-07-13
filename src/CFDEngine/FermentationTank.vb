Imports Microsoft.VisualBasic.MachineLearning.TensorFlow

' /********************************************************************************/
'
'   FermentationTank.vb
'
'   发酵罐 —— 模拟容器与主时间步进循环
'
'   作用：
'       把流体场、搅拌器、求解器组合起来，提供一个完整的 "发酵罐搅拌" 模拟。
'       每个 Step(dt) 调用执行一个时间步的完整 Navier-Stokes 求解。
'
'   时间步进流程（算子分裂法）：
'       1. 应用搅拌器（添加外力 / 强制速度）
'       2. 扩散速度（隐式，模拟粘性）
'       3. 平流速度（半拉格朗日，模拟流体随自身运动）
'       4. 压力投影（强制不可压缩）
'       5. 再次应用搅拌器（保持搅拌器区域速度）
'       6. 平流密度（示踪剂随流体运动）
'       7. 扩散密度（可选，模拟分子扩散）
'
'   物理参数：
'       Viscosity (ν)  —— 运动粘度，水约 1e-6 m²/s，本引擎用网格单位
'       Diffusion      —— 示踪剂扩散系数
'       TimeStep (dt)  —— 时间步长，半拉格朗日法对 dt 不敏感（无条件稳定）
'
' /********************************************************************************/

''' <summary>
''' 发酵罐模拟 —— 容器 + 搅拌器 + 流体 + 求解器的组合。
''' </summary>
Public Class FermentationTank

#Region "组件"

    ''' <summary>流体场（速度、压力、密度）</summary>
    Public ReadOnly Property Field As FluidField

    ''' <summary>搅拌器</summary>
    Public ReadOnly Property Stirrer As Stirrer

    ''' <summary>稳定流体求解器</summary>
    Public ReadOnly Property Solver As StableFluidsSolver

#End Region

#Region "物理参数"

    ''' <summary>
    ''' 运动粘度 ν（网格单位）。
    ''' 越大流体越 "黏稠"（如蜂蜜），越小越 "稀"（如水）。
    ''' 教学默认 0.0001。
    ''' </summary>
    Public Property Viscosity As Double = 0.0001

    ''' <summary>
    ''' 示踪剂扩散系数（网格单位）。
    ''' 控制密度场的分子扩散速度。
    ''' </summary>
    Public Property Diffusion As Double = 0.00001

    ''' <summary>
    ''' 当前模拟时间（累计）。
    ''' </summary>
    Public Property Time As Double = 0.0

    ''' <summary>
    ''' 已执行的时间步数。
    ''' </summary>
    Public Property StepCount As Integer = 0

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建发酵罐模拟。
    ''' </summary>
    ''' <param name="nx">X 方向格子数</param>
    ''' <param name="ny">Y 方向格子数</param>
    ''' <param name="nz">Z 方向格子数</param>
    ''' <param name="stirrer">搅拌器（若 Nothing 则不放置搅拌器）</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   Optional stirrer As Stirrer = Nothing)

        Me.Field = New FluidField(nx, ny, nz)
        Me.Stirrer = stirrer
        Me.Solver = New StableFluidsSolver()

    End Sub

#End Region

#Region "主时间步进"

    ''' <summary>
    ''' 执行一个时间步的完整 CFD 求解。
    ''' 这是整个引擎的核心调用。
    ''' </summary>
    ''' <param name="dt">时间步长</param>
    Public Sub StepForward(dt As Double)

        Dim nx = Field.Nx
        Dim ny = Field.Ny
        Dim nz = Field.Nz

        ' ---- 临时缓冲场（双缓冲，避免平流时污染源场）----
        Dim u0 = Field.U
        Dim v0 = Field.V
        Dim w0 = Field.W
        Dim d0 = Field.Density

        Dim u1 As New Tensor(nx, ny, nz)
        Dim v1 As New Tensor(nx, ny, nz)
        Dim w1 As New Tensor(nx, ny, nz)
        Dim d1 As New Tensor(nx, ny, nz)

        ' ---- Step 1: 应用搅拌器（强制搅拌器区域速度）----
        If Stirrer IsNot Nothing Then
            Stirrer.ApplyToField(Field)
        End If

        ' 速度边界
        Solver.SetVelocityBoundary(u0, 0)
        Solver.SetVelocityBoundary(v0, 1)
        Solver.SetVelocityBoundary(w0, 2)

        ' ---- Step 2: 扩散速度（隐式）----
        ' 求解 u1 - dt*ν*∇²u1 = u0
        If Viscosity > 0 Then
            Solver.Diffuse(u0, Viscosity, dt, u1)
            Solver.Diffuse(v0, Viscosity, dt, v1)
            Solver.Diffuse(w0, Viscosity, dt, w1)
        Else
            Array.Copy(u0.Data, u1.Data, u0.Length)
            Array.Copy(v0.Data, v1.Data, v0.Length)
            Array.Copy(w0.Data, w1.Data, w0.Length)
        End If

        ' 扩散后再次应用搅拌器（保持搅拌器区域）
        If Stirrer IsNot Nothing Then
            Stirrer.ApplyToFieldInternal(u1, v1, w1)
        End If

        Solver.SetVelocityBoundary(u1, 0)
        Solver.SetVelocityBoundary(v1, 1)
        Solver.SetVelocityBoundary(w1, 2)

        ' ---- Step 3: 平流速度（半拉格朗日）----
        ' 用 (u1,v1,w1) 作为速度场，把 (u1,v1,w1) 自身平流到 (u0,v0,w0)
        Solver.Advect(u1, u1, v1, w1, dt, u0)
        Solver.Advect(v1, u1, v1, w1, dt, v0)
        Solver.Advect(w1, u1, v1, w1, dt, w0)

        Solver.SetVelocityBoundary(u0, 0)
        Solver.SetVelocityBoundary(v0, 1)
        Solver.SetVelocityBoundary(w0, 2)

        ' ---- Step 4: 压力投影（强制不可压缩）----
        Solver.Project(u0, v0, w0, Field.Pressure, dt)

        ' ---- Step 5: 再次应用搅拌器（确保搅拌器始终驱动）----
        If Stirrer IsNot Nothing Then
            Stirrer.ApplyToField(Field)
        End If

        ' ---- Step 6: 平流密度（示踪剂随流体运动）----
        Solver.Advect(d0, u0, v0, w0, dt, d1)
        Solver.SetScalarBoundary(d1)

        ' ---- Step 7: 扩散密度（可选）----
        If Diffusion > 0 Then
            Solver.Diffuse(d1, Diffusion, dt, d0)
            Solver.SetScalarBoundary(d0)
        Else
            Array.Copy(d1.Data, d0.Data, d0.Length)
        End If

        ' 清理临时缓冲
        u1.Dispose()
        v1.Dispose()
        w1.Dispose()
        d1.Dispose()

        ' 更新统计
        Time += dt
        StepCount += 1

    End Sub

#End Region

#Region "查询接口"

    ''' <summary>
    ''' 获取指定体素的全部基础物理量。
    ''' </summary>
    Public Function GetVoxel(i As Integer, j As Integer, k As Integer) As VoxelData
        Return Field.GetVoxel(i, j, k)
    End Function

    ''' <summary>
    ''' 获取指定体素的速度向量。
    ''' </summary>
    Public Function GetVelocity(i As Integer, j As Integer, k As Integer) As (u As Double, v As Double, w As Double)
        Return Field.GetVelocity(i, j, k)
    End Function

    ''' <summary>
    ''' 获取整个速度场 U 分量的 Tensor（只读引用）。
    ''' </summary>
    Public Function GetVelocityU() As Tensor
        Return Field.U
    End Function

    ''' <summary>
    ''' 获取整个速度场 V 分量的 Tensor。
    ''' </summary>
    Public Function GetVelocityV() As Tensor
        Return Field.V
    End Function

    ''' <summary>
    ''' 获取整个速度场 W 分量的 Tensor。
    ''' </summary>
    Public Function GetVelocityW() As Tensor
        Return Field.W
    End Function

    ''' <summary>
    ''' 获取整个压力场 Tensor。
    ''' </summary>
    Public Function GetPressure() As Tensor
        Return Field.Pressure
    End Function

    ''' <summary>
    ''' 获取整个密度/示踪剂场 Tensor。
    ''' </summary>
    Public Function GetDensity() As Tensor
        Return Field.Density
    End Function

#End Region

End Class

