Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   GhiaBenchmark.vb
'
'   Ghia, Ghia & Shin (1982) 顶盖驱动方腔基准解（129×129 谱方法）。
'   供 LBM 结果做中心线对比（3D 展向中面 ≈ 2D 解，立方腔偏差 ~1-3%）。
'
'   坐标约定：y = 0 底部（静止），y = 1 顶盖；u 对 U_wall 归一。
'
' /********************************************************************************/

''' <summary>Ghia 基准中心线数据与 L2 对比工具。</summary>
Public Module GhiaBenchmark

    ''' <summary>Re = 100：垂直中心线 y → u/U_wall。</summary>
    Public ReadOnly Y100 As Double() = {
        0.0000, 0.0547, 0.0625, 0.0703, 0.1016, 0.1719, 0.2813, 0.4531,
        0.5000, 0.6172, 0.7344, 0.8516, 0.9531, 0.9609, 0.9688, 0.9766, 1.0000}

    ''' <summary>Re = 100：垂直中心线 u/U_wall。</summary>
    Public ReadOnly U100 As Double() = {
        0.00000, -0.03717, -0.04192, -0.04775, -0.06434, -0.10150, -0.15662,
        -0.21090, -0.20581, -0.13641, 0.00332, 0.23151, 0.68717, 0.73722,
        0.78866, 0.84123, 1.00000}

    ''' <summary>Re = 100：水平中心线 x → v/U_wall。</summary>
    Public ReadOnly X100 As Double() = {
        0.0000, 0.0625, 0.0703, 0.1016, 0.1719, 0.2813, 0.4531, 0.5000,
        0.6172, 0.7344, 0.8516, 0.9531, 0.9609, 0.9688, 0.9766, 1.0000}

    ''' <summary>Re = 100：水平中心线（流传版数据，仅存档勿用于验收）。</summary>
    ''' <remarks>该流传版 v 序列与 u 列相同（近墙 ±0.84/1.0 违反侧壁无滑移），
    ''' 系镜像混淆；引擎的流向验证改由 u 剖面 + 主涡结构（顺时针）承担。</remarks>
    Public ReadOnly V100 As Double() = {
        0.00000, -0.03717, -0.04192, -0.06434, -0.10150, -0.15662, -0.21090,
        -0.20581, -0.13641, 0.00332, 0.23151, 0.68717, 0.73722, 0.78866,
        0.84123, 1.00000}

    ''' <summary>
    ''' 计算模拟中心线相对 Ghia 的 L2 相对误差：
    ''' err = ||u_sim − u_ref||₂ / ||u_ref||₂（线性插值到 Ghia 网格）。
    ''' </summary>
    ''' <param name="sim">模拟采样 (n,2)：(:,0)=坐标 0..1，(:,1)=归一速度</param>
    ''' <param name="refX">基准坐标</param>
    ''' <param name="refU">基准速度</param>
    Public Function L2Error(sim As Double(,), refX As Double(), refU As Double()) As Double
        Dim num = 0.0R, den = 0.0R
        For i = 0 To refX.Length - 1
            Dim x = refX(i)
            Dim u = L2Interp(sim, x)
            num += (u - refU(i)) ^ 2
            den += refU(i) ^ 2
        Next
        Return System.Math.Sqrt(num / den)
    End Function

    ''' <summary>分段线性插值（sim 第一列升序）——供 CLI 打印对比表。</summary>
    Public Function L2Interp(sim As Double(,), x As Double) As Double
        Dim n = sim.GetLength(0)
        If x <= sim(0, 0) Then Return sim(0, 1)
        If x >= sim(n - 1, 0) Then Return sim(n - 1, 1)
        For i = 0 To n - 2
            If x >= sim(i, 0) AndAlso x <= sim(i + 1, 0) Then
                Dim t = (x - sim(i, 0)) / (sim(i + 1, 0) - sim(i, 0))
                Return sim(i, 1) * (1.0R - t) + sim(i + 1, 1) * t
            End If
        Next
        Return sim(n - 1, 1)
    End Function

End Module
