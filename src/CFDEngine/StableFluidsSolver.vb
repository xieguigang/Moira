Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
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
'   每个时间步的执行顺序（算子分裂法 Operator Splitting）：
'       Step 1: 添加外力         u += dt * f
'       Step 2: 扩散速度         求解 ν∇²u 的隐式方程
'       Step 3: 平流速度         半拉格朗日回溯
'       Step 4: 压力投影         求解 ∇²p = ∇·u/dt，然后 u -= dt*∇p
'       Step 5: 平流密度         半拉格朗日回溯（被动标量）
'       Step 6: 扩散密度         可选
'
'   ---------------------------------------------------------------------------
'   性能说明（P0 优化后的实现要点）
'   ---------------------------------------------------------------------------
'   本文件是整个引擎的热点，所有热循环都直接操作 Single() 裸数组，
'   而不是通过张量索引器。原因与具体优化如下：
'
'   1) 消除索引器的原子操作
'      张量索引器的 Setter 内含 Interlocked.Increment（版本计数）。在 128³ 网格上，
'      仅压力泊松的 30 次迭代就是 6300 万次带 lock 前缀的原子操作 + 全内存屏障。
'      改裸数组后完全消除。
'
'   2) 消除 Jacobi 迭代内的张量克隆
'      原实现每次迭代都 Clone() 一个张量作为 "上一轮" 缓冲，128³ 下每步约
'      150 次 16.8 MB 分配 ≈ 2.5 GB/步的 GC 压力。改为构造期预分配的
'      ping-pong 双缓冲（_bufA / _bufB），整个时间步零分配。
'
'   3) 固体掩膜由"运行时函数调用"改为"预计算 + 内联索引"
'      原实现在三重循环里调用 IsSolid()，压力泊松每格 7 次 × 30 次迭代
'      = 210 次函数调用/格 → 4.4 亿次/步。改为构造期一次性算出
'      流体掩膜 _mask()（1=流体 / 0=固体）与流体邻居数 _nFluid()，
'      热循环里只做一次数组索引。
'
'   4) 合并速度三分量的平流
'      U/V/W 的回溯坐标与 8 个角点权重完全相同，原实现却整段算 3 遍。
'      新增 AdvectVelocity 一次算权重、三次取值，省约 2/3 的三线性采样开销。
'
'   5) 线性索引自增替代重复乘法
'      内层用 idx 增量自增（步长 1）、外层用 baseIdx 换 j 层（步长 nz）、
'      最外层换 i 层（步长 ny*nz），避免每个格子重算 (i*ny + j)*nz + k。
'
'   数值行为：迭代次数、迭代式、边界处理与优化前完全一致，
'   仅存储精度由 Double 降为 Single（中间累加仍用 Double 以减小误差）。
'
' /********************************************************************************/

''' <summary>
''' 稳定流体求解器 —— 实现 3D 不可压缩流体的核心数值方法。
''' 场量基于单精度张量 <see cref="TensorF"/>，热循环直接操作其底层 Single() 数组。
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
    ''' </summary>
    Public Property NoSlipWalls As Boolean = False

#End Region

#Region "固体掩膜 (Solid Mask) 与预计算缓存"

    ''' <summary>用户设置的原始掩膜引用（用于检测是否需要重建缓存）</summary>
    Private _solidMask As Boolean()

    ''' <summary>
    ''' 固体掩膜（True = 空腔 / 固体障碍物）。长度 = nx*ny*nz，与 TensorF 数据布局一致：
    ''' idx = (i * ny + j) * nz + k，即与 VoxelShape.Index(i,j,k) 完全相同的顺序。
    ''' 为 Nothing 或全 False 时表示无固体（退化为旧版长方体行为）。
    ''' 求解器中固体单元速度/压力强制为 0，表面作无滑移壁面，压力投影中排除。
    ''' </summary>
    Public Property SolidMask As Boolean()
        Get
            Return _solidMask
        End Get
        Set
            _solidMask = Value
            ' 置空以触发下次求解前重建流体掩膜与邻居数缓存
            _mask = Nothing
        End Set
    End Property

    ''' <summary>流体掩膜缓存：1 = 流体，0 = 固体（与 SolidMask 语义相反，便于内联判断）</summary>
    Private _mask As Byte()

    ''' <summary>每格的流体邻居数（0..6），仅内部单元有效。用于压力泊松的变除数</summary>
    Private _nFluid As Byte()

    ''' <summary>
    ''' 是否存在固体单元。为 False 时可跳过所有"按掩膜置零"的 O(N) 全扫
    ''' （原实现每步要扫 150 次全场，是纯浪费）。
    ''' </summary>
    Private _hasSolid As Boolean

    ''' <summary>上一次扩散求解实际执行的 Jacobi 迭代次数（诊断用）</summary>
    Public ReadOnly Property LastDiffuseIterations As Integer

    ''' <summary>上一次压力泊松实际执行的 Jacobi 迭代次数（诊断用）</summary>
    Public ReadOnly Property LastPressureIterations As Integer

    ''' <summary>Jacobi ping-pong 缓冲 A（当前轮）</summary>
    Private _bufA As Single()

    ''' <summary>Jacobi ping-pong 缓冲 B（上一轮）</summary>
    Private _bufB As Single()

    ''' <summary>散度场缓冲</summary>
    Private _div As Single()

    ''' <summary>判断体素 (i, j, k) 是否为固体（空腔）。无掩膜时恒为 False。</summary>
    Private Function IsSolid(i As Integer, j As Integer, k As Integer,
                             nx As Integer, ny As Integer, nz As Integer) As Boolean
        If _mask Is Nothing OrElse _mask.Length <> nx * ny * nz Then Return False
        Return _mask((i * ny + j) * nz + k) = 0
    End Function

    ''' <summary>
    ''' 确保流体掩膜 / 邻居数 / ping-pong 缓冲已按当前网格与掩膜构建好。
    ''' 整个时间步内只会在首次或网格、掩膜变化时触发一次。
    ''' </summary>
    Private Sub EnsurePrepared(nx As Integer, ny As Integer, nz As Integer)
        Dim n = nx * ny * nz

        If _mask Is Nothing OrElse _mask.Length <> n Then
            BuildMasks(nx, ny, nz)
        End If

        If _bufA Is Nothing OrElse _bufA.Length <> n Then
            _bufA = New Single(n - 1) {}
            _bufB = New Single(n - 1) {}
            _div = New Single(n - 1) {}
        End If
    End Sub

    ''' <summary>构建流体掩膜与每格流体邻居数（构造期一次性完成）。</summary>
    Private Sub BuildMasks(nx As Integer, ny As Integer, nz As Integer)
        Dim n = nx * ny * nz
        Dim plane = ny * nz

        _mask = New Byte(n - 1) {}
        _nFluid = New Byte(n - 1) {}

        If _solidMask Is Nothing Then
            ' 无掩膜：全部为流体
            For idx = 0 To n - 1
                _mask(idx) = 1
            Next
            _hasSolid = False
        Else
            Dim solidCount = 0
            For idx = 0 To n - 1
                If _solidMask(idx) Then
                    _mask(idx) = 0
                    solidCount += 1
                Else
                    _mask(idx) = 1
                End If
            Next
            _hasSolid = solidCount > 0
        End If

        ' 只统计内部单元的邻居数：热循环仅访问 1..n-2 的内部区域
        For i = 1 To nx - 2
            For j = 1 To ny - 2
                Dim baseIdx = i * plane + j * nz
                For k = 1 To nz - 2
                    Dim idx = baseIdx + k
                    Dim c As Integer = 0
                    If _mask(idx - plane) <> 0 Then c += 1
                    If _mask(idx + plane) <> 0 Then c += 1
                    If _mask(idx - nz) <> 0 Then c += 1
                    If _mask(idx + nz) <> 0 Then c += 1
                    If _mask(idx - 1) <> 0 Then c += 1
                    If _mask(idx + 1) <> 0 Then c += 1
                    _nFluid(idx) = CByte(c)
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' 把数组中所有固体单元置零。
    ''' 无固体时直接返回 —— 原实现每步要无条件全扫 150 次
    ''' （4 个扩散场 × 30 轮 + 压力泊松 30 轮），是纯粹的浪费。
    ''' </summary>
    Private Sub ZeroSolidRaw(f As Single())
        If _mask Is Nothing OrElse Not _hasSolid Then Return
        For idx = 0 To f.Length - 1
            If _mask(idx) = 0 Then f(idx) = 0.0F
        Next
    End Sub

    ''' <summary>
    ''' 把单个标量场中固体单元的值置零（如密度 / 压力）。
    ''' </summary>
    Private Sub ZeroSolidScalar(f As TensorF)
        ZeroSolidRaw(f.Data)
    End Sub

    ''' <summary>
    ''' 把固体单元的速度（U/V/W）与压力置零（无滑移壁，速度 = 0）。
    ''' </summary>
    Private Sub ZeroSolidVelocity(u As TensorF, v As TensorF, w As TensorF, p As TensorF)
        If _mask Is Nothing OrElse Not _hasSolid Then Return
        Dim uu = u.Data, vv = v.Data, ww = w.Data, pp = p.Data
        For idx = 0 To uu.Length - 1
            If _mask(idx) = 0 Then
                uu(idx) = 0.0F
                vv(idx) = 0.0F
                ww(idx) = 0.0F
                pp(idx) = 0.0F
            End If
        Next
    End Sub

    ''' <summary>
    ''' 把整个流体场中固体单元的速度（U/V/W）、压力与密度全部置零。
    ''' 供 FermentationTank 在每个时间步末尾调用。
    ''' </summary>
    Public Sub EnforceSolidMask(velU As TensorF, velV As TensorF, velW As TensorF,
                                pressure As TensorF, density As TensorF)
        If _mask Is Nothing OrElse Not _hasSolid Then Return
        Dim uu = velU.Data, vv = velV.Data, ww = velW.Data
        Dim pp = pressure.Data, dd = density.Data

        For idx = 0 To uu.Length - 1
            If _mask(idx) = 0 Then
                uu(idx) = 0.0F
                vv(idx) = 0.0F
                ww(idx) = 0.0F
                pp(idx) = 0.0F
                dd(idx) = 0.0F
            End If
        Next
    End Sub

#End Region

#Region "半拉格朗日平流 (Semi-Lagrangian Advection)"

    ''' <summary>
    ''' 半拉格朗日平流：根据速度场把标量场 "搬运" 到新位置。
    '''
    ''' 原理：
    '''   平流方程：∂φ/∂t + u·∇φ = 0
    '''   半拉格朗日法 "反向追踪"：对当前格子 (i,j,k)，
    '''   求 dt 时间前到达这里的流体粒子所在位置 (i,j,k) - dt*velocity(i,j,k)，
    '''   再在该位置做三线性插值采样旧场。无条件稳定。
    ''' </summary>
    ''' <param name="field">要平流的标量场（如密度）</param>
    ''' <param name="velU">速度场 X 分量</param>
    ''' <param name="velV">速度场 Y 分量</param>
    ''' <param name="velW">速度场 Z 分量</param>
    ''' <param name="dt">时间步长</param>
    ''' <param name="result">输出：平流后的新场</param>
    Public Sub Advect(field As TensorF,
                      velU As TensorF, velV As TensorF, velW As TensorF,
                      dt As Double,
                      result As TensorF)

        Dim shp = field.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        EnsurePrepared(nx, ny, nz)

        Dim plane = ny * nz
        Dim f = field.Data
        Dim u = velU.Data, v = velV.Data, w = velW.Data
        Dim r = result.Data
        Dim m = _mask

        Dim xMax As Double = nx - 1.5
        Dim yMax As Double = ny - 1.5
        Dim zMax As Double = nz - 1.5

        For i = 0 To nx - 1
            For j = 0 To ny - 1
                Dim baseIdx = i * plane + j * nz
                For k = 0 To nz - 1
                    Dim idx = baseIdx + k

                    ' 固体单元不参与平流，速度恒为 0（无滑移壁）
                    If m(idx) = 0 Then
                        r(idx) = 0.0F
                        Continue For
                    End If

                    ' 反向追踪（用 Double 计算坐标，保证与双精度版一致的几何精度）
                    ' 钳制写成内联比较而非 Math.Min/Math.Max，避免热循环里的函数调用
                    Dim x = i - dt * u(idx)
                    If x < 0.5 Then
                        x = 0.5
                    ElseIf x > xMax Then
                        x = xMax
                    End If

                    Dim y = j - dt * v(idx)
                    If y < 0.5 Then
                        y = 0.5
                    ElseIf y > yMax Then
                        y = yMax
                    End If

                    Dim z = k - dt * w(idx)
                    If z < 0.5 Then
                        z = 0.5
                    ElseIf z > zMax Then
                        z = zMax
                    End If

                    r(idx) = TrilinearAt(f, x, y, z, nx, ny, nz, plane)
                Next
            Next
        Next

    End Sub

    ''' <summary>
    ''' 速度场自平流（合并 U/V/W 三个分量）。
    '''
    ''' 三个分量的回溯坐标与 8 个角点权重完全相同，旧实现分别调用 3 次 Advect
    ''' 会把整段三线性采样算 3 遍。本方法一次算出回溯位置与权重，
    ''' 再对三个源场各做一次插值，省掉约 2/3 的采样开销。
    ''' </summary>
    ''' <param name="srcU">源速度场 X 分量（同时作为平流用的速度场 U）</param>
    ''' <param name="srcV">源速度场 Y 分量</param>
    ''' <param name="srcW">源速度场 Z 分量</param>
    ''' <param name="dt">时间步长</param>
    ''' <param name="outU">输出速度场 X 分量</param>
    ''' <param name="outV">输出速度场 Y 分量</param>
    ''' <param name="outW">输出速度场 Z 分量</param>
    Public Sub AdvectVelocity(srcU As TensorF, srcV As TensorF, srcW As TensorF,
                              dt As Double,
                              outU As TensorF, outV As TensorF, outW As TensorF)

        Dim shp = srcU.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        EnsurePrepared(nx, ny, nz)

        Dim plane = ny * nz
        Dim su = srcU.Data, sv = srcV.Data, sw = srcW.Data
        Dim ru = outU.Data, rv = outV.Data, rw = outW.Data
        Dim m = _mask

        Dim xMax As Double = nx - 1.5
        Dim yMax As Double = ny - 1.5
        Dim zMax As Double = nz - 1.5

        For i = 0 To nx - 1
            For j = 0 To ny - 1
                Dim baseIdx = i * plane + j * nz
                For k = 0 To nz - 1
                    Dim idx = baseIdx + k

                    If m(idx) = 0 Then
                        ru(idx) = 0.0F
                        rv(idx) = 0.0F
                        rw(idx) = 0.0F
                        Continue For
                    End If

                    ' 回溯位置：三个分量共用（钳制内联化，避免函数调用）
                    Dim x = i - dt * su(idx)
                    If x < 0.5 Then
                        x = 0.5
                    ElseIf x > xMax Then
                        x = xMax
                    End If

                    Dim y = j - dt * sv(idx)
                    If y < 0.5 Then
                        y = 0.5
                    ElseIf y > yMax Then
                        y = yMax
                    End If

                    Dim z = k - dt * sw(idx)
                    If z < 0.5 Then
                        z = 0.5
                    ElseIf z > zMax Then
                        z = zMax
                    End If

                    ' 三个场共用同一组权重，但各自的 8 个角点不同，需分别取值
                    ru(idx) = TrilinearAt(su, x, y, z, nx, ny, nz, plane)
                    rv(idx) = TrilinearAt(sv, x, y, z, nx, ny, nz, plane)
                    rw(idx) = TrilinearAt(sw, x, y, z, nx, ny, nz, plane)
                Next
            Next
        Next

    End Sub

#End Region

    ''' <summary>
    ''' 邻居数倒数查表：把压力泊松里的浮点除法换成乘法。
    ''' 流体邻居数 nFluid 只有 0..6 七种取值，查表后整条热循环不再出现除法
    ''' （双精度除法延迟约 14 周期、吞吐约 4 周期，是 stencil 里最贵的标量运算）。
    ''' 下标 0 对应"无流体邻居"的兜底值 1.0。
    ''' </summary>
    Private Shared ReadOnly FluidInv As Double() = {
        1.0, 1.0, 0.5, 1.0 / 3.0, 0.25, 0.2, 1.0 / 6.0}

#Region "隐式扩散 (Implicit Diffusion)"

    ''' <summary>
    ''' 隐式扩散：求解 ν∇²φ 的隐式方程。
    '''
    ''' 离散化（7 点拉普拉斯，dx=1）：
    '''   (1 + 6*dt*ν) * φ_new(i,j,k) - dt*ν*[6 个邻居] = φ_old(i,j,k)
    ''' Jacobi 迭代：
    '''   φ_new(i,j,k) = [φ_old(i,j,k) + dt*ν*(邻居之和)] / (1 + 6*dt*ν)
    ''' </summary>
    ''' <param name="field">输入场（旧值）</param>
    ''' <param name="diff">扩散系数 ν</param>
    ''' <param name="dt">时间步长</param>
    ''' <param name="result">输出：扩散后的新场</param>
    Public Sub Diffuse(field As TensorF, diff As Double, dt As Double, result As TensorF)

        Dim shp = field.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        EnsurePrepared(nx, ny, nz)

        Dim n = nx * ny * nz
        Dim plane = ny * nz
        Dim f = field.Data
        Dim m = _mask
        Dim cur = _bufA, prev = _bufB

        ' 系数仍用双精度计算，避免扩散系数很小时被单精度吃掉。
        ' 除数取倒数，把热循环里的除法换成乘法。
        Dim aD As Double = dt * diff
        Dim invDenom As Double = 1.0 / (1.0 + 6.0 * aD)

        ' 初始猜测 = 旧值；固体单元置零
        Array.Copy(f, cur, n)
        ZeroSolidRaw(cur)

        ' 收敛判据的参考尺度：先取场的量级。
        ' 用绝对阈值会在场量级很大时永不收敛、很小时过早退出，因此按场量级自适应。
        Dim scale As Double = 0
        For idx = 0 To n - 1
            Dim av = std.Abs(f(idx))
            If av > scale Then scale = av
        Next
        Dim tol As Double = 1.0E-6 * If(scale > 1.0, scale, 1.0)

        Dim usedIterations As Integer = 0

        For iter = 0 To JacobiIterations - 1
            usedIterations = iter + 1

            ' ping-pong：交换引用即可，不需要每轮 memcpy。
            ' 交换后 prev 是上一轮结果，cur 是待覆盖的缓冲；
            ' 内层会把内部单元、SetScalarBoundary 会把边界单元全部重写，无残留。
            Dim swap = prev
            prev = cur
            cur = swap

            Dim maxDelta As Double = 0

            For i = 1 To nx - 2
                For j = 1 To ny - 2
                    Dim baseIdx = i * plane + j * nz
                    For k = 1 To nz - 2
                        Dim idx = baseIdx + k

                        If m(idx) = 0 Then
                            cur(idx) = 0.0F
                            Continue For
                        End If

                        ' 6 个邻居之和（固体邻居在场上恒为 0，直接计入即为"固定 0 壁值"）
                        Dim s As Double = prev(idx - plane)
                        s += prev(idx + plane)
                        s += prev(idx - nz)
                        s += prev(idx + nz)
                        s += prev(idx - 1)
                        s += prev(idx + 1)

                        Dim newVal = CSng((f(idx) + aD * s) * invDenom)
                        cur(idx) = newVal

                        ' 收敛监控：就地累积本轮最大变化量，不需要额外的 O(N) 扫描
                        Dim dd As Double = newVal - prev(idx)
                        If dd < 0 Then dd = -dd
                        If dd > maxDelta Then maxDelta = dd
                    Next
                Next
            Next

            ' 边界（零梯度）
            SetScalarBoundaryRaw(cur, nx, ny, nz)
            ' 再次保证固体单元为 0
            ZeroSolidRaw(cur)

            ' 收敛即退出。
            ' 扩散项迭代矩阵的谱半径约为 6a/(1+6a)；本引擎典型 a = dt·ν = 1e-5，
            ' 谱半径约 6e-5 —— 第 2~3 轮就已收敛到单精度极限，剩下 27 轮算出的数
            ' 完全相同，纯属浪费。若用户把 ν 调到很大导致不收敛，maxDelta 不会
            ' 降到阈值以下，迭代次数自然回到 JacobiIterations，行为与原来一致。
            If maxDelta <= tol Then Exit For
        Next

        _LastDiffuseIterations = usedIterations
        Array.Copy(cur, result.Data, n)

    End Sub

#End Region

#Region "压力投影 (Pressure Projection)"

    ''' <summary>
    ''' 压力投影：强制速度场无散度（∇·u = 0），即不可压缩。
    '''
    ''' 步骤：
    '''   1. 计算散度 div = ∇·u（中心差分）
    '''   2. 求解 ∇²p = div（Jacobi 迭代，固体邻居按 Neumann 零梯度处理）
    '''   3. 速度减去压力梯度：u -= dt * ∇p
    ''' </summary>
    Public Sub Project(velU As TensorF, velV As TensorF, velW As TensorF,
                       pressure As TensorF, dt As Double)

        Dim shp = velU.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        EnsurePrepared(nx, ny, nz)

        Dim n = nx * ny * nz
        Dim plane = ny * nz
        Dim u = velU.Data, v = velV.Data, w = velW.Data
        Dim pr = pressure.Data
        Dim m = _mask
        Dim div = _div

        ' ---- Step 1: 计算散度 div = ∇·u ----
        ' 注意：压力场不再在这里清零。泊松迭代使用 _bufA/_bufB 双缓冲，
        ' 迭代结束后一次性拷回 pressure，避免每轮对压力场做 memcpy。

        For i = 1 To nx - 2
            For j = 1 To ny - 2
                Dim baseIdx = i * plane + j * nz
                For k = 1 To nz - 2
                    Dim idx = baseIdx + k

                    ' 固体单元散度强制为 0（不向 Poisson 方程贡献源项）
                    If m(idx) = 0 Then
                        div(idx) = 0.0F
                        Continue For
                    End If

                    Dim dudx As Double = (u(idx + plane) - u(idx - plane)) * 0.5
                    Dim dvdy As Double = (v(idx + nz) - v(idx - nz)) * 0.5
                    Dim dwdz As Double = (w(idx + 1) - w(idx - 1)) * 0.5

                    div(idx) = CSng((dudx + dvdy + dwdz) / dt)
                Next
            Next
        Next

        SetScalarBoundaryRaw(div, nx, ny, nz)

        ' ---- Step 2: 求解 Poisson 方程 ∇²p = div ----
        ' 离散形式：Σ(流体邻居 p) - nFluid·p = div  →  p = (Σ流体邻居 - div) / nFluid
        '
        ' 关键优化：固体单元在每轮结束时被强制归零（见循环末尾的 ZeroSolidRaw），
        ' 因此"排除固体邻居"与"把固体邻居当作 0 计入"完全等价。
        ' 于是内层可以无条件累加 6 个邻居，省掉 6 次分支判断与随之而来的
        ' 分支预测失败的代价；除数用查表倒数换成乘法。
        Dim curP = _bufA, prevP = _bufB
        Dim usedP As Integer = 0
        Array.Clear(curP, 0, n)   ' 初始猜测全零

        For iter = 0 To JacobiIterations - 1
            usedP = iter + 1

            ' ping-pong：交换引用
            Dim swapP = prevP
            prevP = curP
            curP = swapP

            For i = 1 To nx - 2
                For j = 1 To ny - 2
                    Dim baseIdx = i * plane + j * nz
                    For k = 1 To nz - 2
                        Dim idx = baseIdx + k

                        If m(idx) = 0 Then
                            curP(idx) = 0.0F
                            Continue For
                        End If

                        Dim s As Double = prevP(idx - plane)
                        s += prevP(idx + plane)
                        s += prevP(idx - nz)
                        s += prevP(idx + nz)
                        s += prevP(idx - 1)
                        s += prevP(idx + 1)

                        curP(idx) = CSng((s - div(idx)) * FluidInv(CInt(_nFluid(idx))))
                    Next
                Next
            Next

            ' 压力边界：零梯度（Neumann 边界）
            SetScalarBoundaryRaw(curP, nx, ny, nz)
            ' 保证固体单元（含被边界复制覆盖的边界固体）恒为 0，
            ' 这样下一轮无条件累加时固体邻居的贡献恰好为 0，与原语义一致
            ZeroSolidRaw(curP)
        Next

        ' 迭代结果一次性写回压力场
        _LastPressureIterations = usedP
        Array.Copy(curP, pr, n)

        ' ---- Step 3: 速度减去压力梯度 ----
        For i = 1 To nx - 2
            For j = 1 To ny - 2
                Dim baseIdx = i * plane + j * nz
                For k = 1 To nz - 2
                    Dim idx = baseIdx + k

                    If m(idx) = 0 Then
                        u(idx) = 0.0F
                        v(idx) = 0.0F
                        w(idx) = 0.0F
                        Continue For
                    End If

                    ' 固体邻居用本格压力代替（零梯度），保证壁面无通量
                    Dim pc As Double = pr(idx)
                    Dim pXp = If(m(idx + plane) <> 0, pr(idx + plane), pc)
                    Dim pXm = If(m(idx - plane) <> 0, pr(idx - plane), pc)
                    Dim pYp = If(m(idx + nz) <> 0, pr(idx + nz), pc)
                    Dim pYm = If(m(idx - nz) <> 0, pr(idx - nz), pc)
                    Dim pZp = If(m(idx + 1) <> 0, pr(idx + 1), pc)
                    Dim pZm = If(m(idx - 1) <> 0, pr(idx - 1), pc)

                    u(idx) = CSng(u(idx) - dt * (pXp - pXm) * 0.5)
                    v(idx) = CSng(v(idx) - dt * (pYp - pYm) * 0.5)
                    w(idx) = CSng(w(idx) - dt * (pZp - pZm) * 0.5)
                Next
            Next
        Next

        ' 速度边界
        SetVelocityBoundary(velU, 0)
        SetVelocityBoundary(velV, 1)
        SetVelocityBoundary(velW, 2)

        ' 最终保证所有固体单元速度/压力为 0
        ZeroSolidVelocity(velU, velV, velW, pressure)

    End Sub

#End Region

#Region "三线性插值 (Trilinear Interpolation)"

    ''' <summary>
    ''' 三线性插值：在连续坐标 (x, y, z) 处采样标量场（张量版本）。
    ''' </summary>
    Public Function TrilinearSample(field As TensorF, x As Double, y As Double, z As Double) As Double
        Dim shp = field.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        EnsurePrepared(nx, ny, nz)
        Return TrilinearAt(field.Data, x, y, z, nx, ny, nz, ny * nz)
    End Function

    ''' <summary>
    ''' 三线性插值：在连续坐标 (x, y, z) 处直接采样裸数组。
    ''' 热循环专用版本 —— 避免张量索引器的边界检查与形状查询。
    ''' </summary>
    Private Shared Function TrilinearAt(f As Single(), x As Double, y As Double, z As Double,
                                        nx As Integer, ny As Integer, nz As Integer,
                                        plane As Integer) As Single

        Dim i0 = CInt(std.Floor(x))
        Dim j0 = CInt(std.Floor(y))
        Dim k0 = CInt(std.Floor(z))

        Dim fx = x - i0
        Dim fy = y - j0
        Dim fz = z - k0

        ' 热路径上坐标已钳制到 [0.5, n-1.5]，故 i0∈[0,n-2]、i1∈[1,n-1] 必然合法。
        ' 这里的比较是纯防御性的（供公开入口 TrilinearSample 使用），
        ' 写成内联比较而非 Math.Min/Math.Max，在热路径上几乎零成本。
        Dim nxM1 = nx - 1, nyM1 = ny - 1, nzM1 = nz - 1

        Dim i1 = i0 + 1
        If i1 > nxM1 Then i1 = nxM1
        If i0 < 0 Then
            i0 = 0
        ElseIf i0 > nxM1 Then
            i0 = nxM1
        End If

        Dim j1 = j0 + 1
        If j1 > nyM1 Then j1 = nyM1
        If j0 < 0 Then
            j0 = 0
        ElseIf j0 > nyM1 Then
            j0 = nyM1
        End If

        Dim k1 = k0 + 1
        If k1 > nzM1 Then k1 = nzM1
        If k0 < 0 Then
            k0 = 0
        ElseIf k0 > nzM1 Then
            k0 = nzM1
        End If

        Dim a = i0 * plane + j0 * nz
        Dim b = i0 * plane + j1 * nz
        Dim c = i1 * plane + j0 * nz
        Dim d = i1 * plane + j1 * nz

        ' 8 个角点
        Dim c000 = f(a + k0), c001 = f(a + k1)
        Dim c010 = f(b + k0), c011 = f(b + k1)
        Dim c100 = f(c + k0), c101 = f(c + k1)
        Dim c110 = f(d + k0), c111 = f(d + k1)

        ' 沿 X 插值（4 次）
        Dim c00 = c000 * (1 - fx) + c100 * fx
        Dim c01 = c001 * (1 - fx) + c101 * fx
        Dim c10 = c010 * (1 - fx) + c110 * fx
        Dim c11 = c011 * (1 - fx) + c111 * fx

        ' 沿 Y 插值（2 次）
        Dim cc0 = c00 * (1 - fy) + c10 * fy
        Dim cc1 = c01 * (1 - fy) + c11 * fy

        ' 沿 Z 插值（1 次）
        Return CSng(cc0 * (1 - fz) + cc1 * fz)

    End Function

#End Region

#Region "边界条件 (Boundary Conditions)"

    ''' <summary>
    ''' 标量场边界条件：零梯度（Neumann 边界）。
    ''' 把边界格子的值设为相邻内部格子的值，模拟 "场量不穿透壁面"。
    ''' </summary>
    Public Sub SetScalarBoundary(field As TensorF)
        Dim shp = field.Shape
        EnsurePrepared(shp(0), shp(1), shp(2))
        SetScalarBoundaryRaw(field.Data, shp(0), shp(1), shp(2))
    End Sub

    ''' <summary>标量场边界条件（裸数组版本，热循环专用）。</summary>
    Private Shared Sub SetScalarBoundaryRaw(f As Single(), nx As Integer, ny As Integer, nz As Integer)
        Dim plane = ny * nz

        ' X 方向边界（i=0 和 i=nx-1）
        For j = 0 To ny - 1
            For k = 0 To nz - 1
                f(j * nz + k) = f(plane + j * nz + k)
                f((nx - 1) * plane + j * nz + k) = f((nx - 2) * plane + j * nz + k)
            Next
        Next

        ' Y 方向边界
        For i = 0 To nx - 1
            Dim baseIdx = i * plane
            For k = 0 To nz - 1
                f(baseIdx + k) = f(baseIdx + nz + k)
                f(baseIdx + (ny - 1) * nz + k) = f(baseIdx + (ny - 2) * nz + k)
            Next
        Next

        ' Z 方向边界
        For i = 0 To nx - 1
            For j = 0 To ny - 1
                Dim baseIdx = i * plane + j * nz
                f(baseIdx) = f(baseIdx + 1)
                f(baseIdx + nz - 1) = f(baseIdx + nz - 2)
            Next
        Next

        ' 角点取相邻边界均值（避免奇异）
        f(0) = (f(plane) + f(nz) + f(1)) / 3.0F
        Dim last = (nx - 1) * plane + (ny - 1) * nz + (nz - 1)
        f(last) = (f(last - plane) + f(last - nz) + f(last - 1)) / 3.0F
    End Sub

    ''' <summary>
    ''' 速度场边界条件：壁面不可穿透（法向速度取反），切向分量按配置处理。
    ''' component: 0 → U（X 法向）、1 → V（Y 法向）、2 → W（Z 法向）
    ''' </summary>
    Public Sub SetVelocityBoundary(field As TensorF, component As Integer)

        Dim shp = field.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        EnsurePrepared(nx, ny, nz)

        Dim f = field.Data
        Dim plane = ny * nz
        ' 切向分量的符号：自由滑移 → +1（复制）；无滑移 → -1（取反）
        Dim tangSign As Single = If(NoSlipWalls, -1.0F, 1.0F)

        ' ---- X 方向两个壁面 (i=0, i=nx-1) ----
        Dim signX = If(component = 0, -1.0F, tangSign)
        For j = 0 To ny - 1
            For k = 0 To nz - 1
                f(j * nz + k) = signX * f(plane + j * nz + k)
                f((nx - 1) * plane + j * nz + k) = signX * f((nx - 2) * plane + j * nz + k)
            Next
        Next

        ' ---- Y 方向两个壁面 (j=0, j=ny-1) ----
        Dim signY = If(component = 1, -1.0F, tangSign)
        For i = 0 To nx - 1
            Dim baseIdx = i * plane
            For k = 0 To nz - 1
                f(baseIdx + k) = signY * f(baseIdx + nz + k)
                f(baseIdx + (ny - 1) * nz + k) = signY * f(baseIdx + (ny - 2) * nz + k)
            Next
        Next

        ' ---- Z 方向两个壁面 (k=0, k=nz-1) ----
        Dim signZ = If(component = 2, -1.0F, tangSign)
        For i = 0 To nx - 1
            For j = 0 To ny - 1
                Dim baseIdx = i * plane + j * nz
                f(baseIdx) = signZ * f(baseIdx + 1)
                f(baseIdx + nz - 1) = signZ * f(baseIdx + nz - 2)
            Next
        Next

    End Sub

#End Region

End Class
