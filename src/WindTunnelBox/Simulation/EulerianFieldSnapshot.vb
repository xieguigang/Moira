Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports Moira.CFDEngine.Snapshot
Imports Moira.FVMEngine.Eulerian
Imports Math = System.Math

' /********************************************************************************/
'
'   EulerianFieldSnapshot.vb
'
'   FVM 解 → 结构笛卡尔格 的采样与快照层（WindTunnelBox 的"数据适配层"）。
'
'   为什么需要这一层：
'       Eulerian 网格是**非结构**的（多面体 / 四面体 / 六面体 / 棱柱），
'       解是逐单元的；而本项目的三条可视化管线（点云热图 stride 降采样、
'       矢量箭头、流线 RK2 三线性插值）全部建立在 IFlowFieldData 的
'       **结构化笛卡尔契约**（Nx/Ny/Nz + idx = (i*Ny+j)*Nz+k）之上。
'       重采样把这两个世界桥接起来，使渲染侧零改造复用。
'
'   采样策略：最近单元中心（零阶）。对分片常数（cell-centered）单元场是
'   **精确**的，不引入插值误差，代价是单元边界呈阶梯状。
'
'   性能关键：
'       "结构格 → 单元"的映射只取决于网格与结构格设置，**与解无关**。
'       因此这里在构造时一次性算好 _nearest 映射并缓存，逐帧采样时
'       只剩一次 O(N) 的数组拷贝，不再做 NearestCell 的环搜索。
'
' /********************************************************************************/

''' <summary>
''' 某一时刻风洞解的**不可变**结构笛卡尔快照。
''' 后台求解线程生成并发布，UI 线程只读消费，
''' 避免渲染读到"算到一半"的场数组。
''' </summary>
Public NotInheritable Class EulerianFieldSnapshot

    ''' <summary>结构格分辨率。</summary>
    Public ReadOnly Property Nx As Integer
    Public ReadOnly Property Ny As Integer
    Public ReadOnly Property Nz As Integer

    ''' <summary>结构格间距（真实物理长度；渲染侧按等间距索引空间绘制）。</summary>
    Public ReadOnly Property SpacingX As Double
    Public ReadOnly Property SpacingY As Double
    Public ReadOnly Property SpacingZ As Double

    ''' <summary>速度分量（逐格点，idx = (i*Ny+j)*Nz+k）。</summary>
    Public ReadOnly Property Ux As Single()
    Public ReadOnly Property Uy As Single()
    Public ReadOnly Property Uz As Single()

    ''' <summary>压力（Pa；SIMPLE 解可差一个常数）。</summary>
    Public ReadOnly Property P As Single()

    ''' <summary>域外掩膜：1 = 该格点不在流体域内（渲染为固体灰），0 = 流体。</summary>
    Public ReadOnly Property Outside As Byte()

    ''' <summary>求解步号。</summary>
    Public ReadOnly Property StepIndex As Integer

    ''' <summary>物理时间（s；稳态为 0）。</summary>
    Public ReadOnly Property Time As Double

    ''' <summary>最近一次归一化连续性残差。</summary>
    Public ReadOnly Property Residual As Double

    ''' <summary>连续性误差 Σ|φ_f|（m³/s）。</summary>
    Public ReadOnly Property ContinuityError As Double

    ''' <summary>气动力系数（未计算时为 0）。</summary>
    Public ReadOnly Property Cd As Double
    Public ReadOnly Property Cl As Double
    Public ReadOnly Property Cm As Double

    ''' <summary>全场最大速度模（m/s）。</summary>
    Public ReadOnly Property MaxSpeed As Double

    ''' <summary>参与积分的壁面 patch 名（逗号分隔）。</summary>
    Public ReadOnly Property ForcePatches As String = ""

    Friend Sub New(nx As Integer, ny As Integer, nz As Integer,
                   spacingX As Double, spacingY As Double, spacingZ As Double,
                   ux As Single(), uy As Single(), uz As Single(), p As Single(),
                   outside As Byte(),
                   stepIndex As Integer, time As Double,
                   residual As Double, continuityError As Double,
                   cd As Double, cl As Double, cm As Double,
                   maxSpeed As Double, forcePatches As String)

        Me.Nx = nx : Me.Ny = ny : Me.Nz = nz
        Me.SpacingX = spacingX : Me.SpacingY = spacingY : Me.SpacingZ = spacingZ
        Me.Ux = ux : Me.Uy = uy : Me.Uz = uz : Me.P = p
        Me.Outside = outside
        Me.StepIndex = stepIndex
        Me.Time = time
        Me.Residual = residual
        Me.ContinuityError = continuityError
        Me.Cd = cd : Me.Cl = cl : Me.Cm = cm
        Me.MaxSpeed = maxSpeed
        Me.ForcePatches = If(forcePatches, "")
    End Sub

    ''' <summary>结构格格点总数。</summary>
    Public ReadOnly Property CellCount As Integer
        Get
            Return Nx * Ny * Nz
        End Get
    End Property

    ''' <summary>一行状态文本（供状态栏与画布叠加显示）。</summary>
    Public ReadOnly Property Summary As String
        Get
            Return $"step={StepIndex}  res={Residual.ToString("E2", Globalization.CultureInfo.InvariantCulture)}  " &
                   $"cont={ContinuityError.ToString("E2", Globalization.CultureInfo.InvariantCulture)}  " &
                   $"Cd={Cd.ToString("F4", Globalization.CultureInfo.InvariantCulture)}  " &
                   $"Cl={Cl.ToString("F4", Globalization.CultureInfo.InvariantCulture)}  " &
                   $"|u|max={MaxSpeed.ToString("F3", Globalization.CultureInfo.InvariantCulture)}"
        End Get
    End Property

End Class

''' <summary>
''' 把 <see cref="EulerianWindTunnelCase"/> 的逐单元解重采样到结构笛卡尔格。
''' </summary>
''' <remarks>
''' 构造时完成两件固定工作（与解无关，故只做一次）：
'''   ① 由网格包围盒自动定结构格（<see cref="EulerianToCartesianResampler.AutoConfigure"/>）；
'''   ② 建立"结构格 idx → 单元号"的固定映射（域外记 −1）。
''' 之后每帧 <see cref="Sample"/> 只做一次线性拷贝。
''' </remarks>
Public NotInheritable Class EulerianFieldSampler

    Private ReadOnly _mesh As EulerianFvMesh
    Private ReadOnly _resampler As EulerianToCartesianResampler

    ''' <summary>固定映射：结构格 idx → 单元号；−1 = 域外。</summary>
    Private ReadOnly _nearest As Integer()

    ''' <summary>结构格的真实世界原点（= 网格包围盒最小角）。</summary>
    Private _ox As Double, _oy As Double, _oz As Double

    Private ReadOnly _uxBuf As Single()
    Private ReadOnly _uyBuf As Single()
    Private ReadOnly _uzBuf As Single()
    Private ReadOnly _pBuf As Single()
    Private ReadOnly _outsideBuf As Byte()

    ''' <param name="mesh">FVM 网格视图。</param>
    ''' <param name="targetCells">
    ''' 结构格格点上限。重采样是 O(N) 且每帧执行一次，
    ''' 25 万格点约 4 MB×4 个场、单次拷贝在毫秒量级，足以支撑 30 ms 刷新节拍。
    ''' </param>
    Public Sub New(mesh As EulerianFvMesh, Optional targetCells As Integer = 250000)
        If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

        _mesh = mesh
        _resampler = New EulerianToCartesianResampler(mesh.Mesh)

        ' ★ 不用 EulerianToCartesianResampler.AutoConfigure —— 实测它对真实网格
        '   返回 1x1x1（F1 网格包围盒 (4.0, 8.9, 2.3)、h=0.1364，本应得到 30x66x18）。
        '   这里按同样策略显式定格：由包围盒 + 平均单元尺度出发，
        '   超出格点预算就把步长翻倍，直到装得下。
        Call ConfigureGrid(_resampler, mesh.Mesh, targetCells)

        Dim n As Integer = _resampler.CellCount

        _nearest = New Integer(n - 1) {}
        _uxBuf = New Single(n - 1) {}
        _uyBuf = New Single(n - 1) {}
        _uzBuf = New Single(n - 1) {}
        _pBuf = New Single(n - 1) {}
        _outsideBuf = New Byte(n - 1) {}

        Call BuildNearestMap()
    End Sub

    ''' <summary>
    ''' 显式设定结构格：原点 = 包围盒最小角，步长 = 平均单元尺度（逐步翻倍以压进预算）。
    ''' </summary>
    Private Sub ConfigureGrid(rs As EulerianToCartesianResampler, mesh As EulerianMesh, targetCells As Integer)
        Dim bmin As EulerianVertex = Nothing
        Dim bmax As EulerianVertex = Nothing

        Call mesh.GetBounds(bmin, bmax)

        Dim ex As Double = Math.Abs(bmax.X - bmin.X)
        Dim ey As Double = Math.Abs(bmax.Y - bmin.Y)
        Dim ez As Double = Math.Abs(bmax.Z - bmin.Z)
        Dim h As Double = Math.Pow(Math.Max(mesh.TotalVolume, 1.0E-300) / Math.Max(1, mesh.CellCount), 1.0 / 3.0)

        If h <= 0 Then
            h = Math.Max(Math.Max(ex, Math.Max(ey, ez)) / 64.0, 1.0E-6)
        End If

        Dim nx As Integer = 1, ny As Integer = 1, nz As Integer = 1

        For attempt As Integer = 0 To 64
            nx = Math.Max(1, CInt(Math.Floor(ex / h)) + 1)
            ny = Math.Max(1, CInt(Math.Floor(ey / h)) + 1)
            nz = Math.Max(1, CInt(Math.Floor(ez / h)) + 1)

            If CLng(nx) * CLng(ny) * CLng(nz) <= targetCells Then
                Exit For
            End If

            h *= 2.0
        Next

        ' 极端退化网格（包围盒为 0）时兜一个最小格
        If nx <= 1 AndAlso ny <= 1 AndAlso nz <= 1 Then
            h = Math.Max(h, 1.0E-6)
            nx = 2 : ny = 2 : nz = 2
        End If

        ' ★ 关键：绝不能改 rs.OriginX/Y/Z！
        '   重采样器的分箱索引 _binIndex 是**在构造函数里按 Origin = 0 建立的**，
        '   而 NearestCell 又用 (x − Origin) 反算箱号。一旦把 Origin 设成 bmin，
        '   两侧就差了 |bmin|/binSize 个箱子，导致绝大多数查询找不到单元
        '   （实测 F1 网格 81% 的格点被误判为"域外"）。
        '   因此：resampler 的 Origin 保持 0，真实原点由采样器自己记（_ox/_oy/_oz），
        '   查询时传**真实世界坐标**，这样与按 0 建立的分箱完全一致。
        _ox = bmin.X : _oy = bmin.Y : _oz = bmin.Z

        rs.SpacingX = h : rs.SpacingY = h : rs.SpacingZ = h
        rs.Nx = nx : rs.Ny = ny : rs.Nz = nz
    End Sub

    ''' <summary>一次性建立"结构格 → 单元"映射与域外掩膜。</summary>
    Private Sub BuildNearestMap()
        Dim nx As Integer = _resampler.Nx
        Dim ny As Integer = _resampler.Ny
        Dim nz As Integer = _resampler.Nz
        Dim ox As Double = _ox
        Dim oy As Double = _oy
        Dim oz As Double = _oz
        Dim hx As Double = _resampler.SpacingX
        Dim hy As Double = _resampler.SpacingY
        Dim hz As Double = _resampler.SpacingZ

        For i As Integer = 0 To nx - 1
            Dim x As Double = ox + i * hx

            For j As Integer = 0 To ny - 1
                Dim y As Double = oy + j * hy

                For k As Integer = 0 To nz - 1
                    Dim z As Double = oz + k * hz
                    Dim idx As Integer = (i * ny + j) * nz + k
                    Dim c As Integer = _resampler.NearestCell(x, y, z)

                    _nearest(idx) = c

                    If c < 0 Then
                        _outsideBuf(idx) = 1
                    End If
                Next
            Next
        Next
    End Sub

    ''' <summary>结构格分辨率。</summary>
    Public ReadOnly Property Nx As Integer
        Get
            Return _resampler.Nx
        End Get
    End Property

    Public ReadOnly Property Ny As Integer
        Get
            Return _resampler.Ny
        End Get
    End Property

    Public ReadOnly Property Nz As Integer
        Get
            Return _resampler.Nz
        End Get
    End Property

    ''' <summary>结构格间距。</summary>
    Public ReadOnly Property Spacing As Double
        Get
            Return _resampler.SpacingX
        End Get
    End Property

    ''' <summary>采样当前解，生成一份不可变快照。</summary>
    Public Function Sample(c As EulerianWindTunnelCase) As EulerianFieldSnapshot
        If c Is Nothing Then Throw New ArgumentNullException(NameOf(c))

        Dim n As Integer = _nearest.Length
        Dim nCells As Integer = _mesh.CellCount
        Dim u As Double() = If(c.Fields.U IsNot Nothing, c.Fields.U.Values, Nothing)
        Dim p As Double() = If(c.Fields.P IsNot Nothing, c.Fields.P.Values, Nothing)
        Dim hasU As Boolean = u IsNot Nothing AndAlso u.Length >= 3 * nCells
        Dim hasP As Boolean = p IsNot Nothing AndAlso p.Length >= nCells
        Dim maxS2 As Double = 0

        For idx As Integer = 0 To n - 1
            Dim cc As Integer = _nearest(idx)

            If cc < 0 Then
                _uxBuf(idx) = 0.0F
                _uyBuf(idx) = 0.0F
                _uzBuf(idx) = 0.0F
                _pBuf(idx) = 0.0F
                Continue For
            End If

            Dim vx As Double = If(hasU, u(cc), 0.0R)
            Dim vy As Double = If(hasU, u(nCells + cc), 0.0R)
            Dim vz As Double = If(hasU, u(2 * nCells + cc), 0.0R)

            _uxBuf(idx) = CSng(vx)
            _uyBuf(idx) = CSng(vy)
            _uzBuf(idx) = CSng(vz)
            _pBuf(idx) = CSng(If(hasP, p(cc), 0.0R))

            Dim s2 As Double = vx * vx + vy * vy + vz * vz

            If s2 > maxS2 Then maxS2 = s2
        Next

        ' ---- 诊断量 ----
        Dim residual As Double = 0
        Dim continuity As Double = 0

        If c.Incompressible IsNot Nothing Then
            Dim hist As List(Of Double) = c.Incompressible.ResidualHistory

            If hist IsNot Nothing AndAlso hist.Count > 0 Then
                residual = hist(hist.Count - 1)
            End If

            continuity = c.Incompressible.ContinuityError
        ElseIf c.Compressible IsNot Nothing Then
            Dim hist As List(Of Double) = c.Compressible.ResidualHistory

            If hist IsNot Nothing AndAlso hist.Count > 0 Then
                residual = hist(hist.Count - 1)
                If Double.IsNaN(residual) Then residual = 0
            End If
        End If

        Dim f As EulerianForceReport = c.LastForces
        Dim cd As Double = 0, cl As Double = 0, cm As Double = 0
        Dim patches As String = ""

        If f IsNot Nothing Then
            cd = f.Cd : cl = f.Cl : cm = f.Cm
            patches = f.Patches
        End If

        Return New EulerianFieldSnapshot(
            _resampler.Nx, _resampler.Ny, _resampler.Nz,
            _resampler.SpacingX, _resampler.SpacingY, _resampler.SpacingZ,
            CType(_uxBuf.Clone(), Single()), CType(_uyBuf.Clone(), Single()),
            CType(_uzBuf.Clone(), Single()), CType(_pBuf.Clone(), Single()),
            CType(_outsideBuf.Clone(), Byte()),
            c.StepCount, If(c.Incompressible IsNot Nothing, c.Incompressible.Time, 0.0R),
            residual, continuity, cd, cl, cm,
            Math.Sqrt(maxS2), patches)
    End Function

    ''' <summary>结构格与网格的描述文本（加载后显示给用户）。</summary>
    Public ReadOnly Property Description As String
        Get
            Dim sb As New StringBuilder(128)

            sb.Append($"resample grid = {_resampler.Nx}x{_resampler.Ny}x{_resampler.Nz} " &
                      $"(h={_resampler.SpacingX.ToString("G4", Globalization.CultureInfo.InvariantCulture)}, " &
                      $"{(_resampler.CellCount / 1000.0).ToString("F0", Globalization.CultureInfo.InvariantCulture)}k pts)")

            Return sb.ToString()
        End Get
    End Property

End Class
