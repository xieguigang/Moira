' /********************************************************************************/
'
'   SphFieldSampler.vb
'
'   SPH 粒子场 → CFDEngine 网格场采样器
'
'   作用：
'       SPH 是无网格（粒子）方法，而 CFDEngine 的快照导出链路（FluidField +
'       VTIExporter）面向规则体素网格。本采样器是两者之间的桥：
'
'         粒子 (x, vx, p, rho)  --核加权 gather-->  体素 (U, V, W, Pressure, Density)
'
'   做法：
'       - 每次采样把粒子按平滑半径 h 分箱进扁平均匀网格（复用 UniformGrid3D）
'       - 对每一个活动体素中心，只扫描其 27 个相邻格内的粒子（O(n · k)）
'       - 权重取与引擎密度核相同的 (h-r)^2 核，于是
'             Σ w / restDensity ≈ 1   （液体内部），→ 0（空气/液面以上）
'         正好可以直接当"填充率 / 相分数"用来做可视化阈值
'       - 速度取核加权平均，压力取核加权平均
'
'   约定：
'       体素 (i,j,k) 覆盖的世界坐标为
'           [ox + i*dx, ox + (i+1)*dx) × ... ，中心取 (i+0.5)*dx
'       与 FermenterTankSPH 的几何定义严格一致。
'
' /********************************************************************************/

Imports std = System.Math
Imports par = System.Threading.Tasks.Parallel
Imports Microsoft.VisualBasic.Imaging.Physics
Imports Moira.CFDEngine

''' <summary>
''' Samples a 3D SPH particle state onto the regular voxel grid of a
''' <see cref="FluidField"/> so that the result can be exported by the
''' CFDEngine snapshot module.
''' </summary>
Public Class SphFieldSampler

    ''' <summary>the voxel model of the computational space</summary>
    Public ReadOnly Property Shape As VoxelShape

    ''' <summary>sampling radius (equals to the SPH smoothing radius)</summary>
    Public ReadOnly Property Radius As Single

    ''' <summary>voxel size along x (world units per voxel)</summary>
    Public ReadOnly Property SpacingX As Single
    ''' <summary>voxel size along y (world units per voxel)</summary>
    Public ReadOnly Property SpacingY As Single
    ''' <summary>voxel size along z (world units per voxel)</summary>
    Public ReadOnly Property SpacingZ As Single

    Private ReadOnly grid As UniformGrid3D
    Private ReadOnly h As Single
    Private ReadOnly h2 As Single
    Private ReadOnly k2 As Single
    Private ReadOnly ox As Single, oy As Single, oz As Single
    Private ReadOnly dxS As Single, dyS As Single, dzS As Single

    ''' <summary>
    ''' create the sampler for the given voxel model.
    ''' </summary>
    ''' <param name="shape">voxel model (defines which voxels are sampled)</param>
    ''' <param name="smoothingRadius">SPH smoothing radius (world units)</param>
    ''' <param name="originX">world x of the voxel space origin</param>
    ''' <param name="originY">world y of the voxel space origin</param>
    ''' <param name="originZ">world z of the voxel space origin</param>
    ''' <param name="spacingX">voxel size along x</param>
    ''' <param name="spacingY">voxel size along y</param>
    ''' <param name="spacingZ">voxel size along z</param>
    Public Sub New(shape As VoxelShape, smoothingRadius As Single,
                   originX As Single, originY As Single, originZ As Single,
                   spacingX As Single, spacingY As Single, spacingZ As Single)

        Me.Shape = shape
        Me.Radius = smoothingRadius
        Me.h = smoothingRadius
        Me.h2 = h * h
        Me.ox = originX
        Me.oy = originY
        Me.oz = originZ
        Me.dxS = spacingX
        Me.dyS = spacingY
        Me.dzS = spacingZ
        Me.SpacingX = spacingX
        Me.SpacingY = spacingY
        Me.SpacingZ = spacingZ

        ' 与引擎一致的密度核缩放因子（保证 Σ w / restDensity ≈ 1）
        Me.k2 = CSng(15 / (2 * std.PI * std.Pow(h, 5)))

        Dim maxX = originX + shape.Width * spacingX
        Dim maxY = originY + shape.Height * spacingY
        Dim maxZ = originZ + shape.Depth * spacingZ

        Me.grid = New UniformGrid3D(h,
                                    originX - h, originY - h, originZ - h,
                                    maxX + h, maxY + h, maxZ + h,
                                    1024)
    End Sub

    ''' <summary>
    ''' measure the per voxel rest kernel sum of the given particle fill.
    ''' </summary>
    ''' <returns>
    ''' a flat array (engine layout idx = i*(ny*nz) + j*nz + k) of the raw
    ''' kernel sums; it can be handed back to <see cref="Sample"/> as the
    ''' <c>restMap</c> so that a filled voxel reads exactly 1.
    ''' </returns>
    Public Function MeasureRest(state As SphState3D, field As FluidField) As Single()
        ' 原始核和不做截断（截断只作用于归一化之后的填充率）
        Call Sample(state, field, 1.0F, Nothing, clampFill:=False)

        Dim out = New Single(field.TotalVoxels - 1) {}
        Array.Copy(field.Density.Data, out, out.Length)

        ' 参考值的典型尺度（非零参考值的中位数）：远离参考值异常小的体素
        ' （初始液面之上偶尔被单个粒子扫到的体素）改用全局静止密度归一
        Dim nonzero As New List(Of Single)()

        For Each v In out
            If v > 0.000000001F Then nonzero.Add(v)
        Next

        If nonzero.Count > 0 Then
            nonzero.Sort()
            restTypical = nonzero(nonzero.Count \ 2)
        Else
            restTypical = 1.0F
        End If

        Return out
    End Function

    ''' <summary>typical (median) per voxel reference kernel sum of the rest fill</summary>
    Private restTypical As Single = 1.0F

    ''' <summary>
    ''' sample the particle state into the given fluid field.
    ''' </summary>
    ''' <param name="state">the SPH particle state</param>
    ''' <param name="field">target field (cleared and refilled in place)</param>
    ''' <param name="restDensity">
    ''' global normalizer of the density output (used when <paramref name="restMap"/>
    ''' is Nothing or when the per voxel reference is 0).
    ''' </param>
    ''' <param name="restMap">
    ''' optional per voxel reference kernel sum (see <see cref="MeasureRest"/>);
    ''' when given, the density output becomes the local fill fraction, so that
    ''' a voxel that is filled exactly like the initial broth reads 1.
    ''' </param>
    ''' <param name="clampFill">
    ''' clamp the density output to 4 (protects against splashes landing on
    ''' voxels with a tiny reference). must be off when measuring the reference.
    ''' </param>
    Public Sub Sample(state As SphState3D, field As FluidField,
                      Optional restDensity As Single = 1.0F,
                      Optional restMap As Single() = Nothing,
                      Optional clampFill As Boolean = True)
        Call field.Clear()

        Dim n = state.Count
        If n = 0 Then Return

        Call grid.Build(state, predicted:=False)

        Dim px = state.px, py = state.py, pz = state.pz
        Dim vx = state.vx, vy = state.vy, vz = state.vz
        Dim press = state.press
        Dim cellStart = grid.CellStart
        Dim entries = grid.Entries
        Dim gnx = grid.Nx, gny = grid.Ny, gnz = grid.Nz
        Dim hh = h, hh2 = h2, kk = k2
        Dim rest = If(restDensity > 0, restDensity, 1.0F)

        Dim nx = field.Nx, ny = field.Ny, nz = field.Nz
        Dim shape = Me.Shape
        Dim u = field.U, v = field.V, w = field.W
        Dim p = field.Pressure, d = field.Density

        Call par.For(0, nz, Sub(k)
                                Dim wz = oz + (k + 0.5F) * dzS

                                For j As Integer = 0 To ny - 1
                                    Dim wy = oy + (j + 0.5F) * dyS

                                    For i As Integer = 0 To nx - 1
                                        If Not shape.IsActive(i, j, k) Then Continue For

                                        Dim wx = ox + (i + 0.5F) * dxS
                                        Dim cx = grid.CoordX(wx)
                                        Dim cy = grid.CoordY(wy)
                                        Dim cz = grid.CoordZ(wz)

                                        Dim sumW As Single = 0
                                        Dim su As Single = 0, sv As Single = 0, sw As Single = 0
                                        Dim sp As Single = 0

                                        For ddx As Integer = -1 To 1
                                            Dim ax = cx + ddx
                                            If ax < 0 OrElse ax >= gnx Then Continue For
                                            For ddy As Integer = -1 To 1
                                                Dim ay = cy + ddy
                                                If ay < 0 OrElse ay >= gny Then Continue For
                                                For ddz As Integer = -1 To 1
                                                    Dim az = cz + ddz
                                                    If az < 0 OrElse az >= gnz Then Continue For

                                                    Dim c = (ax * gny + ay) * gnz + az
                                                    Dim s0 = cellStart(c)
                                                    Dim e0 = cellStart(c + 1)

                                                    For t As Integer = s0 To e0 - 1
                                                        Dim q = entries(t)

                                                        Dim rx = px(q) - wx
                                                        Dim ry = py(q) - wy
                                                        Dim rz = pz(q) - wz
                                                        Dim r2 = rx * rx + ry * ry + rz * rz

                                                        If r2 >= hh2 Then Continue For

                                                        Dim ur = hh - std.Sqrt(r2)
                                                        Dim weight = ur * ur * kk

                                                        sumW += weight
                                                        su += weight * vx(q)
                                                        sv += weight * vy(q)
                                                        sw += weight * vz(q)
                                                        sp += weight * press(q)
                                                    Next
                                                Next
                                            Next
                                        Next

                                        If sumW <= 0.0000000001F Then Continue For

                                        Dim inv = 1.0F / sumW

                                        u(i, j, k) = su * inv
                                        v(i, j, k) = sv * inv
                                        w(i, j, k) = sw * inv
                                        p(i, j, k) = sp * inv

                                        Dim fill As Single

                                        If restMap Is Nothing Then
                                            fill = sumW / rest
                                        Else
                                            Dim vidx = i * (ny * nz) + j * nz + k
                                            Dim ref = restMap(vidx)

                                            ' 参考值过小（初始为空气、后来被液体填充）时退回全局归一，
                                            ' 避免出现除以极小值导致的荒谬填充率
                                            If ref > 0.25F * restTypical Then
                                                fill = sumW / ref
                                            Else
                                                fill = sumW / rest
                                            End If
                                        End If

                                        If clampFill AndAlso fill > 4.0F Then fill = 4.0F
                                        d(i, j, k) = fill
                                    Next
                                Next
                            End Sub)
    End Sub

End Class
