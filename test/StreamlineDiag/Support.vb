' /********************************************************************************/
'
'   Support.vb —— 诊断程序的数据/采样辅助类型
'
'   · BoxSampler      ：逐字复刻 src\WindTunnelBox\Simulation\EulerianFieldSnapshot.vb
'                       中的 EulerianFieldSampler（结构化重采样 + 域外掩膜）
'   · DiagFlowField   ：逐字复刻 src\WindTunnelBox\Rendering\WindTunnelFlowField.vb
'                       中的 WindTunnelFlowField（IFlowFieldData 适配器）
'   · BoxSnapshot     ：不可变结构化快照（对应 EulerianFieldSnapshot）
'   · FieldStats      ：重采样速度场统计
'
' /********************************************************************************/

Imports Moira.CFDEngine.Eulerian
Imports Moira.CFDEngine.Snapshot
Imports Moira.FVMEngine.Eulerian
Imports Moira.CDFDxCanvas.Rendering
Imports Math = System.Math

''' <summary>
''' 不可变结构化快照（对应 WindTunnelBox 的 EulerianFieldSnapshot）。
''' </summary>
Public NotInheritable Class BoxSnapshot

    Public ReadOnly Nx As Integer
    Public ReadOnly Ny As Integer
    Public ReadOnly Nz As Integer
    Public ReadOnly Ux As Single()
    Public ReadOnly Uy As Single()
    Public ReadOnly Uz As Single()
    Public ReadOnly Outside As Byte()
    Public ReadOnly MaxSpeed As Double

    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   ux As Single(), uy As Single(), uz As Single(), outside As Byte())

        Me.Nx = nx : Me.Ny = ny : Me.Nz = nz
        Me.Ux = ux : Me.Uy = uy : Me.Uz = uz
        Me.Outside = outside

        Dim m As Double = 0

        For i As Integer = 0 To ux.Length - 1
            Dim s As Double = ux(i) * ux(i) + uy(i) * uy(i) + uz(i) * uz(i)

            If s > m Then m = s
        Next

        Me.MaxSpeed = Math.Sqrt(m)
    End Sub

    Public ReadOnly Property Count As Integer
        Get
            Return Nx * Ny * Nz
        End Get
    End Property

    ''' <summary>
    ''' 对照实验：对速度三分量做 <paramref name="iterations"/> 轮 Jacobi 平滑
    ''' （域外节点固定为 0 并作为边界；加权平均保持极值不越界）。
    ''' 用于验证"若速度场不是分片常数，流线是否还会是直线"。
    ''' </summary>
    Public Function Smoothed(iterations As Integer) As BoxSnapshot
        Dim n As Integer = Count
        Dim ax(n - 1) As Single, ay(n - 1) As Single, az(n - 1) As Single
        Array.Copy(Ux, ax, n) : Array.Copy(Uy, ay, n) : Array.Copy(Uz, az, n)

        Dim bx(n - 1) As Single, by(n - 1) As Single, bz(n - 1) As Single

        For it As Integer = 1 To iterations
            For i As Integer = 0 To Nx - 1
                For j As Integer = 0 To Ny - 1
                    For k As Integer = 0 To Nz - 1
                        Dim idx As Integer = (i * Ny + j) * Nz + k

                        If Outside(idx) <> 0 Then Continue For

                        Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
                        Dim w As Double = 0

                        For d As Integer = -1 To 1 Step 2
                            Dim ii As Integer = i + d
                            Dim jj As Integer = j + d
                            Dim kk As Integer = k + d

                            If ii >= 0 AndAlso ii < Nx Then
                                Dim q As Integer = (ii * Ny + j) * Nz + k
                                Dim ww As Double = If(Outside(q) <> 0, 0.5, 1.0)

                                sx += ax(q) * ww : sy += ay(q) * ww : sz += az(q) * ww : w += ww
                            End If

                            If jj >= 0 AndAlso jj < Ny Then
                                Dim q As Integer = (i * Ny + jj) * Nz + k
                                Dim ww As Double = If(Outside(q) <> 0, 0.5, 1.0)

                                sx += ax(q) * ww : sy += ay(q) * ww : sz += az(q) * ww : w += ww
                            End If

                            If kk >= 0 AndAlso kk < Nz Then
                                Dim q As Integer = (i * Ny + j) * Nz + kk
                                Dim ww As Double = If(Outside(q) <> 0, 0.5, 1.0)

                                sx += ax(q) * ww : sy += ay(q) * ww : sz += az(q) * ww : w += ww
                            End If
                        Next

                        ' 自身权重 1（相当于轻度平滑，保留原场结构）
                        If w > 0 Then
                            bx(idx) = CSng((sx + ax(idx)) / (w + 1.0))
                            by(idx) = CSng((sy + ay(idx)) / (w + 1.0))
                            bz(idx) = CSng((sz + az(idx)) / (w + 1.0))
                        Else
                            bx(idx) = ax(idx) : by(idx) = ay(idx) : bz(idx) = az(idx)
                        End If
                    Next
                Next
            Next

            Array.Copy(bx, ax, n) : Array.Copy(by, ay, n) : Array.Copy(bz, az, n)
        Next

        Return New BoxSnapshot(Nx, Ny, Nz, ax, ay, az, Outside)
    End Function

End Class

''' <summary>
''' 逐字复刻 WindTunnelBox\Simulation\EulerianFieldSnapshot.vb 的
''' <c>EulerianFieldSampler</c>（含其 ConfigureGrid / BuildNearestMap / Sample）。
''' </summary>
Public NotInheritable Class BoxSampler

    Private ReadOnly _mesh As EulerianFvMesh
    Private ReadOnly _resampler As EulerianToCartesianResampler
    Private ReadOnly _nearest As Integer()

    Private _ox As Double, _oy As Double, _oz As Double

    Private ReadOnly _uxBuf As Single()
    Private ReadOnly _uyBuf As Single()
    Private ReadOnly _uzBuf As Single()
    Private ReadOnly _pBuf As Single()
    Private ReadOnly _outsideBuf As Byte()

    Public Sub New(mesh As EulerianFvMesh, Optional targetCells As Integer = 250000)
        If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

        _mesh = mesh
        _resampler = New EulerianToCartesianResampler(mesh.Mesh)

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

            If CLng(nx) * CLng(ny) * CLng(nz) <= targetCells Then Exit For

            h *= 2.0
        Next

        If nx <= 1 AndAlso ny <= 1 AndAlso nz <= 1 Then
            h = Math.Max(h, 1.0E-6)
            nx = 2 : ny = 2 : nz = 2
        End If

        ' ★ 与源码一致：绝不修改 rs.OriginX/Y/Z（分箱按 Origin = 0 建立）
        _ox = bmin.X : _oy = bmin.Y : _oz = bmin.Z

        rs.SpacingX = h : rs.SpacingY = h : rs.SpacingZ = h
        rs.Nx = nx : rs.Ny = ny : rs.Nz = nz
    End Sub

    Private Sub BuildNearestMap()
        Dim nx As Integer = _resampler.Nx
        Dim ny As Integer = _resampler.Ny
        Dim nz As Integer = _resampler.Nz
        Dim hx As Double = _resampler.SpacingX
        Dim hy As Double = _resampler.SpacingY
        Dim hz As Double = _resampler.SpacingZ

        For i As Integer = 0 To nx - 1
            Dim x As Double = _ox + i * hx

            For j As Integer = 0 To ny - 1
                Dim y As Double = _oy + j * hy

                For k As Integer = 0 To nz - 1
                    Dim z As Double = _oz + k * hz
                    Dim idx As Integer = (i * ny + j) * nz + k
                    Dim c As Integer = _resampler.NearestCell(x, y, z)

                    _nearest(idx) = c

                    If c < 0 Then _outsideBuf(idx) = 1
                Next
            Next
        Next
    End Sub

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

    Public ReadOnly Property Spacing As Double
        Get
            Return _resampler.SpacingX
        End Get
    End Property

    Public ReadOnly Property OriginX As Double
        Get
            Return _ox
        End Get
    End Property

    Public ReadOnly Property OriginY As Double
        Get
            Return _oy
        End Get
    End Property

    Public ReadOnly Property OriginZ As Double
        Get
            Return _oz
        End Get
    End Property

    Public ReadOnly Property CellOf(idx As Integer) As Integer
        Get
            Return _nearest(idx)
        End Get
    End Property

    Public ReadOnly Property OutsideCount As Integer
        Get
            Dim c As Integer = 0

            For i As Integer = 0 To _outsideBuf.Length - 1
                If _outsideBuf(i) <> 0 Then c += 1
            Next

            Return c
        End Get
    End Property

    ' ---- 映射块状度指标 ----

    Public Function SameCellFractionX() As Double
        Return SameCellFraction(1, 0, 0)
    End Function

    Public Function SameCellFractionY() As Double
        Return SameCellFraction(0, 1, 0)
    End Function

    Public Function SameCellFractionZ() As Double
        Return SameCellFraction(0, 0, 1)
    End Function

    Private Function SameCellFraction(di As Integer, dj As Integer, dk As Integer) As Double
        Dim same As Long = 0, total As Long = 0

        For i As Integer = 0 To Nx - 1 - di
            For j As Integer = 0 To Ny - 1 - dj
                For k As Integer = 0 To Nz - 1 - dk
                    Dim a As Integer = _nearest((i * Ny + j) * Nz + k)
                    Dim b As Integer = _nearest(((i + di) * Ny + (j + dj)) * Nz + (k + dk))

                    If a < 0 OrElse b < 0 Then Continue For

                    total += 1

                    If a = b Then same += 1
                Next
            Next
        Next

        If total = 0 Then Return 0

        Return same / CDbl(total)
    End Function

    Public Function Sample(c As EulerianWindTunnelCase) As BoxSnapshot
        If c Is Nothing Then Throw New ArgumentNullException(NameOf(c))

        Dim n As Integer = _nearest.Length
        Dim nCells As Integer = _mesh.CellCount
        Dim u As Double() = If(c.Fields.U IsNot Nothing, c.Fields.U.Values, Nothing)
        Dim p As Double() = If(c.Fields.P IsNot Nothing, c.Fields.P.Values, Nothing)
        Dim hasU As Boolean = u IsNot Nothing AndAlso u.Length >= 3 * nCells
        Dim hasP As Boolean = p IsNot Nothing AndAlso p.Length >= nCells

        For idx As Integer = 0 To n - 1
            Dim cc As Integer = _nearest(idx)

            If cc < 0 Then
                _uxBuf(idx) = 0.0F
                _uyBuf(idx) = 0.0F
                _uzBuf(idx) = 0.0F
                _pBuf(idx) = 0.0F
                Continue For
            End If

            _uxBuf(idx) = CSng(If(hasU, u(cc), 0.0R))
            _uyBuf(idx) = CSng(If(hasU, u(nCells + cc), 0.0R))
            _uzBuf(idx) = CSng(If(hasU, u(2 * nCells + cc), 0.0R))
            _pBuf(idx) = CSng(If(hasP, p(cc), 0.0R))
        Next

        Return New BoxSnapshot(_resampler.Nx, _resampler.Ny, _resampler.Nz,
                               CType(_uxBuf.Clone(), Single()),
                               CType(_uyBuf.Clone(), Single()),
                               CType(_uzBuf.Clone(), Single()),
                               CType(_outsideBuf.Clone(), Byte()))
    End Function

End Class

''' <summary>
''' 逐字复刻 WindTunnelBox\Rendering\WindTunnelFlowField.vb 的
''' <c>WindTunnelFlowField</c>（IFlowFieldData 适配器）。
''' </summary>
Public NotInheritable Class DiagFlowField : Implements IFlowFieldData

    ReadOnly m_ux As Single()
    ReadOnly m_uy As Single()
    ReadOnly m_uz As Single()
    ReadOnly m_outside As Byte()
    ReadOnly m_nx As Integer
    ReadOnly m_ny As Integer
    ReadOnly m_nz As Integer
    ReadOnly m_ox As Single
    ReadOnly m_oy As Single
    ReadOnly m_oz As Single

    Public Sub New(snap As BoxSnapshot,
                   Optional originX As Single = 0.0F,
                   Optional originY As Single = 0.0F,
                   Optional originZ As Single = 0.0F)

        m_nx = snap.Nx : m_ny = snap.Ny : m_nz = snap.Nz
        m_ux = snap.Ux : m_uy = snap.Uy : m_uz = snap.Uz
        m_outside = snap.Outside
        m_ox = originX : m_oy = originY : m_oz = originZ
    End Sub

    Public ReadOnly Property Nx As Integer Implements IFlowFieldData.Nx
        Get
            Return m_nx
        End Get
    End Property

    Public ReadOnly Property Ny As Integer Implements IFlowFieldData.Ny
        Get
            Return m_ny
        End Get
    End Property

    Public ReadOnly Property Nz As Integer Implements IFlowFieldData.Nz
        Get
            Return m_nz
        End Get
    End Property

    Public ReadOnly Property OriginX As Single Implements IFlowFieldData.OriginX
        Get
            Return m_ox
        End Get
    End Property

    Public ReadOnly Property OriginY As Single Implements IFlowFieldData.OriginY
        Get
            Return m_oy
        End Get
    End Property

    Public ReadOnly Property OriginZ As Single Implements IFlowFieldData.OriginZ
        Get
            Return m_oz
        End Get
    End Property

    Public ReadOnly Property SpacingX As Single Implements IFlowFieldData.SpacingX
        Get
            Return 1.0F
        End Get
    End Property

    Public ReadOnly Property SpacingY As Single Implements IFlowFieldData.SpacingY
        Get
            Return 1.0F
        End Get
    End Property

    Public ReadOnly Property SpacingZ As Single Implements IFlowFieldData.SpacingZ
        Get
            Return 1.0F
        End Get
    End Property

    Public ReadOnly Property CenterOffset As Single Implements IFlowFieldData.CenterOffset
        Get
            Return 0.0F
        End Get
    End Property

    Public Function UxAt(idx As Integer) As Single Implements IFlowFieldData.UxAt
        Return m_ux(idx)
    End Function

    Public Function UyAt(idx As Integer) As Single Implements IFlowFieldData.UyAt
        Return m_uy(idx)
    End Function

    Public Function UzAt(idx As Integer) As Single Implements IFlowFieldData.UzAt
        Return m_uz(idx)
    End Function

    Public Function IsActive(idx As Integer) As Boolean Implements IFlowFieldData.IsActive
        If m_outside Is Nothing Then Return True
        Return m_outside(idx) = 0
    End Function

    Public Function IsSeedable(idx As Integer) As Boolean Implements IFlowFieldData.IsSeedable
        If m_outside Is Nothing Then Return True
        Return m_outside(idx) = 0
    End Function

End Class

''' <summary>重采样速度场统计（用于判断零阶重采样的"阶梯"程度）。</summary>
Public NotInheritable Class FieldStats

    Public Active As Integer
    Public SpeedMin As Double, SpeedMean As Double, SpeedMax As Double
    Public UxMin As Double, UxMean As Double, UxMax As Double
    Public UyMin As Double, UyMean As Double, UyMax As Double
    Public UzMin As Double, UzMean As Double, UzMax As Double

    Public dUxX As Double, dUyX As Double, dUzX As Double
    Public ZeroUxX As Double, ZeroUyX As Double, ZeroUzX As Double
    Public ZeroUxY As Double, ZeroUxZ As Double

    Public SlowCount As Integer
    Public CrossCount As Integer

    Public Shared Function Compute(s As BoxSnapshot) As FieldStats
        Dim st As New FieldStats
        Dim n As Integer = s.Count
        Dim act As Integer = 0

        st.SpeedMin = Double.MaxValue : st.UxMin = Double.MaxValue
        st.UyMin = Double.MaxValue : st.UzMin = Double.MaxValue
        st.SpeedMax = Double.MinValue : st.UxMax = Double.MinValue
        st.UyMax = Double.MinValue : st.UzMax = Double.MinValue

        For idx As Integer = 0 To n - 1
            If s.Outside(idx) <> 0 Then Continue For

            act += 1

            Dim ux As Double = s.Ux(idx), uy As Double = s.Uy(idx), uz As Double = s.Uz(idx)
            Dim sp As Double = Math.Sqrt(ux * ux + uy * uy + uz * uz)

            st.SpeedMin = Math.Min(st.SpeedMin, sp) : st.SpeedMax = Math.Max(st.SpeedMax, sp)
            st.UxMin = Math.Min(st.UxMin, ux) : st.UxMax = Math.Max(st.UxMax, ux)
            st.UyMin = Math.Min(st.UyMin, uy) : st.UyMax = Math.Max(st.UyMax, uy)
            st.UzMin = Math.Min(st.UzMin, uz) : st.UzMax = Math.Max(st.UzMax, uz)
            st.SpeedMean += sp : st.UxMean += ux : st.UyMean += uy : st.UzMean += uz

            If Math.Abs(ux) < 0.9 * 30.0 Then st.SlowCount += 1
            If Math.Abs(uy) > 1.0 OrElse Math.Abs(uz) > 1.0 Then st.CrossCount += 1
        Next

        If act = 0 Then Return st

        st.Active = act
        st.SpeedMean /= act : st.UxMean /= act : st.UyMean /= act : st.UzMean /= act

        ' 相邻节点差分
        Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
        Dim zx As Long = 0, zy As Long = 0, zz As Long = 0
        Dim cntX As Long = 0

        For i As Integer = 0 To s.Nx - 2
            For j As Integer = 0 To s.Ny - 1
                For k As Integer = 0 To s.Nz - 1
                    Dim a As Integer = (i * s.Ny + j) * s.Nz + k
                    Dim b As Integer = ((i + 1) * s.Ny + j) * s.Nz + k

                    If s.Outside(a) <> 0 OrElse s.Outside(b) <> 0 Then Continue For

                    Dim dx As Double = Math.Abs(s.Ux(b) - s.Ux(a))
                    Dim dy As Double = Math.Abs(s.Uy(b) - s.Uy(a))
                    Dim dz As Double = Math.Abs(s.Uz(b) - s.Uz(a))

                    sx += dx : sy += dy : sz += dz
                    If dx = 0 Then zx += 1
                    If dy = 0 Then zy += 1
                    If dz = 0 Then zz += 1
                    cntX += 1
                Next
            Next
        Next

        If cntX > 0 Then
            st.dUxX = sx / cntX : st.dUyX = sy / cntX : st.dUzX = sz / cntX
            st.ZeroUxX = zx / CDbl(cntX) : st.ZeroUyX = zy / CDbl(cntX) : st.ZeroUzX = zz / CDbl(cntX)
        End If

        st.ZeroUxY = ZeroFraction(s, 1, 0)
        st.ZeroUxZ = ZeroFraction(s, 2, 0)

        Return st
    End Function

    ''' <summary>axis: 0 = +x, 1 = +y, 2 = +z；比较 |ΔUx| 为 0 的比例。</summary>
    Private Shared Function ZeroFraction(s As BoxSnapshot, axis As Integer, comp As Integer) As Double
        Dim same As Long = 0, total As Long = 0

        For i As Integer = 0 To s.Nx - 1
            For j As Integer = 0 To s.Ny - 1
                For k As Integer = 0 To s.Nz - 1
                    Dim ii As Integer = i, jj As Integer = j, kk As Integer = k

                    Select Case axis
                        Case 1 : jj += 1
                        Case 2 : kk += 1
                        Case Else : ii += 1
                    End Select

                    If ii >= s.Nx OrElse jj >= s.Ny OrElse kk >= s.Nz Then Continue For

                    Dim a As Integer = (i * s.Ny + j) * s.Nz + k
                    Dim b As Integer = (ii * s.Ny + jj) * s.Nz + kk

                    If s.Outside(a) <> 0 OrElse s.Outside(b) <> 0 Then Continue For

                    total += 1

                    If s.Ux(b) = s.Ux(a) Then same += 1
                Next
            Next
        Next

        If total = 0 Then Return 0

        Return same / CDbl(total)
    End Function

End Class
