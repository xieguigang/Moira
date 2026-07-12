Imports std = System.Math

' /********************************************************************************/
'
'   StableFluidsSolver.vb
'
'   稳定流体求解器 (Stable Fluids Solver)
'
'   基于 Jos Stam 1999 年提出的 "Stable Fluids" 算法实现：
'       "Stable Fluids", Proceedings of SIGGRAPH 1999.
'
'   该算法的核心思想：
'       1. 半拉格朗日平流 (Semi-Lagrangian Advection) —— 无条件稳定
'       2. 隐式扩散 (Implicit Diffusion) —— 无条件稳定
'       3. 压力投影 (Pressure Projection) —— 强制不可压缩性
'
'   求解的方程是不可压缩 Navier-Stokes 方程：
'       ∂u/∂t + (u·∇)u = -∇p/ρ + ν∇²u + f
'       ∇·u = 0   (不可压缩约束)
'
'   其中：
'       u = (U, V, W) 是速度向量场
'       p 是压力
'       ρ 是密度（本引擎假设常密度，故 ρ 被吸收进压力）
'       ν 是运动粘度
'       f 是外力（如搅拌器）
'
'   每个时间步的执行顺序（算子分裂法 Operator Splitting）：
'       Step 1: 添加外力         u += dt * f
'       Step 2: 扩散速度         求解 ν∇²u 的隐式方程
'       Step 3: 平流速度         半拉格朗日回溯
'       Step 4: 压力投影         求解 ∇²p = ∇·u/dt，然后 u -= dt*∇p
'       Step 5: 平流密度         半拉格朗日回溯（被动标量）
'       Step 6: 扩散密度         可选
'
'   说明：
'       本实现使用网格单位 (dx = dy = dz = 1)，物理尺度通过参数吸收。
'       这在教学实现中非常常见，能让公式更简洁。
'       Jacobi 迭代用于求解线性方程组，虽然收敛慢但实现简单、易于理解。
'
' /********************************************************************************/

Namespace CFDEngine

    ''' <summary>
    ''' 稳定流体求解器 —— 实现 3D 不可压缩流体的核心数值方法。
    ''' 所有场量操作基于 Tensor 对象。
    ''' </summary>
    Public Class StableFluidsSolver

#Region "求解器参数"

        ''' <summary>
        ''' Jacobi 迭代的最大迭代次数。
        ''' 次数越多越精确但越慢。教学默认 20~40 次。
        ''' </summary>
        Public Property JacobiIterations As Integer = 30

        ''' <summary>
        ''' 是否在边界使用无滑移边界条件 (no-slip)。
        ''' True:  边界速度法向分量取反、切向分量取反（完全无滑移）
        ''' False: 边界速度仅法向分量取反（自由滑移 free-slip）
        ''' 教学默认 False（自由滑移更稳定）。
        ''' </summary>
        Public Property NoSlipWalls As Boolean = False

#End Region

#Region "半拉格朗日平流 (Semi-Lagrangian Advection)"

        ''' <summary>
        ''' 半拉格朗日平流：根据速度场把标量场 "搬运" 到新位置。
        '''
        ''' 原理：
        '''   平流方程：∂φ/∂t + u·∇φ = 0
        '''   物理含义：场量 φ 随流体一起运动。
        '''   半拉格朗日法的做法是 "反向追踪"：
        '''     对于当前格子 (i,j,k)，问 "dt 时间前，到达这里的流体粒子在哪里？"
        '''     粒子的上一位置 = (i,j,k) - dt * velocity(i,j,k)
        '''     然后在该位置做三线性插值采样旧场，作为当前格子的新值。
        '''
        ''' 这种方法无条件稳定（不会因为 dt 太大而爆炸），
        ''' 是 Stable Fluids 算法的关键所在。
        ''' </summary>
        ''' <param name="field">要平流的标量场（如密度）</param>
        ''' <param name="velU">速度场 X 分量</param>
        ''' <param name="velV">速度场 Y 分量</param>
        ''' <param name="velW">速度场 Z 分量</param>
        ''' <param name="dt">时间步长</param>
        ''' <param name="result">输出：平流后的新场</param>
        Public Sub Advect(field As Tensor,
                          velU As Tensor, velV As Tensor, velW As Tensor,
                          dt As Double,
                          result As Tensor)

            Dim nx = field.Shape(0)
            Dim ny = field.Shape(1)
            Dim nz = field.Shape(2)

            ' 遍历所有格子，反向追踪粒子位置
            For i = 0 To nx - 1
                For j = 0 To ny - 1
                    For k = 0 To nz - 1

                        ' 当前格子中心的速度
                        Dim cu = velU(i, j, k)
                        Dim cv = velV(i, j, k)
                        Dim cw = velW(i, j, k)

                        ' 反向追踪：dt 时间前粒子的位置（网格坐标）
                        Dim x = i - dt * cu
                        Dim y = j - dt * cv
                        Dim z = k - dt * cw

                        ' 把位置限制在网格有效范围内（留出边界用于插值）
                        x = Clamp(x, 0.5, nx - 1.5)
                        y = Clamp(y, 0.5, ny - 1.5)
                        z = Clamp(z, 0.5, nz - 1.5)

                        ' 三线性插值采样旧场
                        result(i, j, k) = TrilinearSample(field, x, y, z)

                    Next
                Next
            Next

        End Sub

#End Region

#Region "隐式扩散 (Implicit Diffusion)"

        ''' <summary>
        ''' 隐式扩散：求解 ν∇²φ 的隐式方程。
        '''
        ''' 原理：
        '''   扩散方程：∂φ/∂t = ν∇²φ
        '''   显式格式：φ_new = φ_old + dt*ν*∇²φ_old  （有稳定性限制 dt < dx²/(6ν)）
        '''   隐式格式：φ_new - dt*ν*∇²φ_new = φ_old  （无条件稳定）
        '''
        '''   离散化（7 点拉普拉斯，dx=1）：
        '''     φ_new(i,j,k) - dt*ν*[φ_new(i±1,j,k)+φ_new(i,j±1,k)+φ_new(i,j,k±1) - 6φ_new(i,j,k)] = φ_old(i,j,k)
        '''   整理得：
        '''     (1 + 6*dt*ν) * φ_new(i,j,k) - dt*ν*[6 个邻居] = φ_old(i,j,k)
        '''   Jacobi 迭代：
        '''     φ_new(i,j,k) = [φ_old(i,j,k) + dt*ν*(邻居之和)] / (1 + 6*dt*ν)
        '''
        ''' Jacobi 迭代法：用上一轮的值计算本轮，反复迭代直到收敛。
        ''' 简单直观，适合教学。
        ''' </summary>
        ''' <param name="field">输入场（旧值）</param>
        ''' <param name="diff">扩散系数 ν</param>
        ''' <param name="dt">时间步长</param>
        ''' <param name="result">输出：扩散后的新场</param>
        Public Sub Diffuse(field As Tensor, diff As Double, dt As Double, result As Tensor)

            Dim nx = field.Shape(0)
            Dim ny = field.Shape(1)
            Dim nz = field.Shape(2)

            ' 系数：a = dt * ν，分母系数 = 1 + 6a
            Dim a = dt * diff
            Dim denom = 1.0 + 6.0 * a

            ' 先把 result 初始化为 field（作为迭代的初始猜测）
            Array.Copy(field.Data, result.Data, field.Length)

            ' Jacobi 迭代
            For iter = 0 To JacobiIterations - 1

                ' 需要一个临时缓冲，因为 Jacobi 用 "上一轮" 的值计算 "本轮"
                Dim prev As Tensor = CType(result.Clone(), Tensor)

                For i = 1 To nx - 2
                    For j = 1 To ny - 2
                        For k = 1 To nz - 2

                            ' 6 个邻居之和（用上一轮的值 prev）
                            Dim sumNeighbors As Double = 0
                            sumNeighbors += prev(i - 1, j, k)
                            sumNeighbors += prev(i + 1, j, k)
                            sumNeighbors += prev(i, j - 1, k)
                            sumNeighbors += prev(i, j + 1, k)
                            sumNeighbors += prev(i, j, k - 1)
                            sumNeighbors += prev(i, j, k + 1)

                            ' Jacobi 更新公式
                            result(i, j, k) = (field(i, j, k) + a * sumNeighbors) / denom

                        Next
                    Next
                Next

                ' 处理边界（零梯度：把边界值设为相邻内部值）
                SetScalarBoundary(result)

                prev.Dispose()

            Next

        End Sub

#End Region

#Region "压力投影 (Pressure Projection)"

        ''' <summary>
        ''' 压力投影：强制速度场无散度（∇·u = 0），即不可压缩。
        '''
        ''' 原理：
        '''   不可压缩约束：∇·u = ∂U/∂x + ∂V/∂y + ∂W/∂z = 0
        '''   但经过平流/扩散后，速度场一般不再满足此约束。
        '''   投影法：找到一个压力场 p，使得 u - dt*∇p 满足无散度。
        '''   代入约束得 Poisson 方程：∇²p = ∇·u / dt
        '''
        ''' 步骤：
        '''   1. 计算散度 div = ∇·u（中心差分）
        '''   2. 求解 ∇²p = div（Jacobi 迭代）
        '''   3. 速度减去压力梯度：u -= dt * ∇p
        '''
        ''' 这一步是 "让水不可压缩" 的关键，也是搅拌产生压力波的来源。
        ''' </summary>
        Public Sub Project(velU As Tensor, velV As Tensor, velW As Tensor,
                           pressure As Tensor, dt As Double)

            Dim nx = velU.Shape(0)
            Dim ny = velU.Shape(1)
            Dim nz = velU.Shape(2)

            ' ---- Step 1: 计算散度 div = ∇·u ----
            ' 同时把压力场清零，作为 Poisson 求解的初始猜测
            Dim div As New Tensor(nx, ny, nz)
            Array.Clear(pressure.Data, 0, pressure.Length)

            For i = 1 To nx - 2
                For j = 1 To ny - 2
                    For k = 1 To nz - 2
                        ' 中心差分计算散度（dx=1）
                        Dim dudx = (velU(i + 1, j, k) - velU(i - 1, j, k)) * 0.5
                        Dim dvdy = (velV(i, j + 1, k) - velV(i, j - 1, k)) * 0.5
                        Dim dwdz = (velW(i, j, k + 1) - velW(i, j, k - 1)) * 0.5
                        div(i, j, k) = (dudx + dvdy + dwdz) / dt
                    Next
                Next
            Next

            ' 散度边界（零梯度）
            SetScalarBoundary(div)

            ' ---- Step 2: 求解 Poisson 方程 ∇²p = div ----
            ' 离散形式：p(i-1)+p(i+1)+p(j-1)+p(j+1)+p(k-1)+p(k+1) - 6p = div
            ' Jacobi 迭代：p_new = (邻居之和 - div) / 6
            For iter = 0 To JacobiIterations - 1

                Dim prev As Tensor = CType(pressure.Clone(), Tensor)

                For i = 1 To nx - 2
                    For j = 1 To ny - 2
                        For k = 1 To nz - 2
                            Dim sumP As Double = 0
                            sumP += prev(i - 1, j, k)
                            sumP += prev(i + 1, j, k)
                            sumP += prev(i, j - 1, k)
                            sumP += prev(i, j + 1, k)
                            sumP += prev(i, j, k - 1)
                            sumP += prev(i, j, k + 1)
                            pressure(i, j, k) = (sumP - div(i, j, k)) / 6.0
                        Next
                    Next
                Next

                ' 压力边界：零梯度（Neumann 边界）
                SetScalarBoundary(pressure)

                prev.Dispose()

            Next

            ' ---- Step 3: 速度减去压力梯度 ----
            ' u -= dt * ∇p
            For i = 1 To nx - 2
                For j = 1 To ny - 2
                    For k = 1 To nz - 2
                        Dim dpdx = (pressure(i + 1, j, k) - pressure(i - 1, j, k)) * 0.5
                        Dim dpdy = (pressure(i, j + 1, k) - pressure(i, j - 1, k)) * 0.5
                        Dim dpdz = (pressure(i, j, k + 1) - pressure(i, j, k - 1)) * 0.5
                        velU(i, j, k) -= dt * dpdx
                        velV(i, j, k) -= dt * dpdy
                        velW(i, j, k) -= dt * dpdz
                    Next
                Next
            Next

            ' 速度边界
            SetVelocityBoundary(velU, 0)
            SetVelocityBoundary(velV, 1)
            SetVelocityBoundary(velW, 2)

        End Sub

#End Region

#Region "三线性插值 (Trilinear Interpolation)"

        ''' <summary>
        ''' 三线性插值：在连续坐标 (x, y, z) 处采样标量场。
        '''
        ''' 原理：
        '''   找到包含 (x,y,z) 的格子立方体的 8 个角点，
        '''   先沿 X 方向线性插值 4 次 → 得到 4 个值，
        '''   再沿 Y 方向插值 2 次 → 得到 2 个值，
        '''   最后沿 Z 方向插值 1 次 → 得到最终值。
        '''
        ''' 这是半拉格朗日平流中 "在反向追踪位置采样旧场" 的关键操作。
        ''' </summary>
        Public Function TrilinearSample(field As Tensor, x As Double, y As Double, z As Double) As Double

            Dim nx = field.Shape(0)
            Dim ny = field.Shape(1)
            Dim nz = field.Shape(2)

            ' 取下界整数索引
            Dim i0 = CInt(std.Floor(x))
            Dim j0 = CInt(std.Floor(y))
            Dim k0 = CInt(std.Floor(z))

            ' 上界索引
            Dim i1 = i0 + 1
            Dim j1 = j0 + 1
            Dim k1 = k0 + 1

            ' 小数部分（插值权重）
            Dim fx = x - i0
            Dim fy = y - j0
            Dim fz = z - k0

            ' 钳制到合法范围
            i0 = ClampInt(i0, 0, nx - 1) : i1 = ClampInt(i1, 0, nx - 1)
            j0 = ClampInt(j0, 0, ny - 1) : j1 = ClampInt(j1, 0, ny - 1)
            k0 = ClampInt(k0, 0, nz - 1) : k1 = ClampInt(k1, 0, nz - 1)

            ' 8 个角点的值
            Dim c000 = field(i0, j0, k0)
            Dim c001 = field(i0, j0, k1)
            Dim c010 = field(i0, j1, k0)
            Dim c011 = field(i0, j1, k1)
            Dim c100 = field(i1, j0, k0)
            Dim c101 = field(i1, j0, k1)
            Dim c110 = field(i1, j1, k0)
            Dim c111 = field(i1, j1, k1)

            ' 沿 X 插值（4 次）
            Dim c00 = c000 * (1 - fx) + c100 * fx
            Dim c01 = c001 * (1 - fx) + c101 * fx
            Dim c10 = c010 * (1 - fx) + c110 * fx
            Dim c11 = c011 * (1 - fx) + c111 * fx

            ' 沿 Y 插值（2 次）
            Dim c0 = c00 * (1 - fy) + c10 * fy
            Dim c1 = c01 * (1 - fy) + c11 * fy

            ' 沿 Z 插值（1 次）
            Return c0 * (1 - fz) + c1 * fz

        End Function

#End Region

#Region "边界条件 (Boundary Conditions)"

        ''' <summary>
        ''' 标量场边界条件：零梯度（Neumann 边界）。
        ''' 把边界格子的值设为相邻内部格子的值，模拟 "场量不穿透壁面"。
        ''' </summary>
        Public Sub SetScalarBoundary(field As Tensor)

            Dim nx = field.Shape(0)
            Dim ny = field.Shape(1)
            Dim nz = field.Shape(2)

            ' X 方向边界（i=0 和 i=nx-1）
            For j = 0 To ny - 1
                For k = 0 To nz - 1
                    field(0, j, k) = field(1, j, k)
                    field(nx - 1, j, k) = field(nx - 2, j, k)
                Next
            Next

            ' Y 方向边界
            For i = 0 To nx - 1
                For k = 0 To nz - 1
                    field(i, 0, k) = field(i, 1, k)
                    field(i, ny - 1, k) = field(i, ny - 2, k)
                Next
            Next

            ' Z 方向边界
            For i = 0 To nx - 1
                For j = 0 To ny - 1
                    field(i, j, 0) = field(i, j, 1)
                    field(i, j, nz - 1) = field(i, j, nz - 2)
                Next
            Next

            ' 角点取相邻边界均值（避免奇异）
            field(0, 0, 0) = (field(1, 0, 0) + field(0, 1, 0) + field(0, 0, 1)) / 3.0
            field(nx - 1, ny - 1, nz - 1) = (field(nx - 2, ny - 1, nz - 1) + field(nx - 1, ny - 2, nz - 1) + field(nx - 1, ny - 1, nz - 2)) / 3.0

        End Sub

        ''' <summary>
        ''' 速度场边界条件：壁面不可穿透（法向速度取反），切向分量按配置处理。
        '''
        ''' component 参数说明：
        '''   0 → U（X 方向速度），壁面是 X=0 和 X=nx-1 两个面，法向 = U
        '''   1 → V（Y 方向速度），壁面是 Y=0 和 Y=ny-1 两个面，法向 = V
        '''   2 → W（Z 方向速度），壁面是 Z=0 和 Z=nz-1 两个面，法向 = W
        '''
        ''' 对于 "法向壁面"：取反（保证不穿透，u·n = 0）
        ''' 对于 "切向壁面"：自由滑移时复制内部值；无滑移时取反
        ''' </summary>
        ''' <param name="field">速度场某一分量</param>
        ''' <param name="component">分量索引 0/1/2</param>
        Public Sub SetVelocityBoundary(field As Tensor, component As Integer)

            Dim nx = field.Shape(0)
            Dim ny = field.Shape(1)
            Dim nz = field.Shape(2)

            ' 切向分量的符号：自由滑移 → +1（复制）；无滑移 → -1（取反）
            Dim tangSign As Double = If(NoSlipWalls, -1.0, 1.0)

            ' ---- X 方向两个壁面 (i=0, i=nx-1) ----
            For j = 0 To ny - 1
                For k = 0 To nz - 1
                    If component = 0 Then
                        ' U 是这两个壁面的法向 → 取反（不可穿透）
                        field(0, j, k) = -field(1, j, k)
                        field(nx - 1, j, k) = -field(nx - 2, j, k)
                    Else
                        ' U 是切向 → 按滑移条件
                        field(0, j, k) = tangSign * field(1, j, k)
                        field(nx - 1, j, k) = tangSign * field(nx - 2, j, k)
                    End If
                Next
            Next

            ' ---- Y 方向两个壁面 (j=0, j=ny-1) ----
            For i = 0 To nx - 1
                For k = 0 To nz - 1
                    If component = 1 Then
                        field(i, 0, k) = -field(i, 1, k)
                        field(i, ny - 1, k) = -field(i, ny - 2, k)
                    Else
                        field(i, 0, k) = tangSign * field(i, 1, k)
                        field(i, ny - 1, k) = tangSign * field(i, ny - 2, k)
                    End If
                Next
            Next

            ' ---- Z 方向两个壁面 (k=0, k=nz-1) ----
            For i = 0 To nx - 1
                For j = 0 To ny - 1
                    If component = 2 Then
                        field(i, j, 0) = -field(i, j, 1)
                        field(i, j, nz - 1) = -field(i, j, nz - 2)
                    Else
                        field(i, j, 0) = tangSign * field(i, j, 1)
                        field(i, j, nz - 1) = tangSign * field(i, j, nz - 2)
                    End If
                Next
            Next

        End Sub

#End Region

#Region "辅助函数"

        ''' <summary>把浮点数钳制到 [min, max] 区间</summary>
        Private Function Clamp(x As Double, min As Double, max As Double) As Double
            If x < min Then Return min
            If x > max Then Return max
            Return x
        End Function

        ''' <summary>把整数钳制到 [min, max] 区间</summary>
        Private Function ClampInt(x As Integer, min As Integer, max As Integer) As Integer
            If x < min Then Return min
            If x > max Then Return max
            Return x
        End Function

#End Region

    End Class

End Namespace
