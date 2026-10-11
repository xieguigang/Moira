' /********************************************************************************/
'
'   MrfModel.vb
'
'   MRF（Multiple Reference Frame，多参考系）旋转区 —— 搅拌桨 / 旋转机械的定常近似。
'
'   物理：
'       在旋转参考系内，绝对速度 u 满足
'           ∂u/∂t + ∇·(u u) − ∇·(ν_eff ∇u) = −∇(p/ρ) − (2Ω×u + Ω×(Ω×r))
'       即绝对系下多出两项：
'           · 科氏力    2Ω×u
'           · 离心力    Ω×(Ω×r)
'       这两项是"区域项"，只在 MRF 单元内为非零，作为显式源加入动量方程右端。
'
'   驱动来源（两条互补路径）：
'       1. **桨叶 moving-wall 边界条件**（首选的物理来源）
'          桨叶几何成为 patch，其边界速度为 u_b = Ω×r（见 MovingWallBc），
'          通过黏性与压力把切向动量输运给流体。这也是 MRF 区科氏项能够
'          自洽的前提。
'       2. **虚拟桨盘驱动**（无桨叶几何时的工程近似）
'          <see cref="EulerianMrfZone.DriveStrength"/> &gt; 0 时，在桨区单元内
'          施加"把切向速度向 Ω×r 拉"的显式源 —— 与 FVMEngine 既有体素求解器
'          ComputeImpellerSource 的思路一致，便于在粗网格上快速得到旋流场。
'
'   混合精度契约：全部 double（源项含叉积与坐标运算，属"非热点"一侧）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic.Math.SIMD
Imports std = System.Math

Namespace Eulerian

    ''' <summary>MRF 旋转区：一组绕固定轴以恒定角速度旋转的单元。</summary>
    Public Class EulerianMrfZone

        ''' <summary>所属网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>旋转区单元（升序）。</summary>
        Public ReadOnly Property Cells As Integer()

        ''' <summary>角速度（rad/s，绕 <see cref="AxisDirection"/> 的右手方向）。</summary>
        Public ReadOnly Property Omega As Double

        ''' <summary>轴上一点。</summary>
        Public ReadOnly Property AxisPoint As Moira.CFDEngine.Eulerian.EulerianVertex

        ''' <summary>轴方向（单位向量）。</summary>
        Public ReadOnly Property AxisDirection As Moira.CFDEngine.Eulerian.EulerianVertex

        ''' <summary>角速度矢量 Ω = 轴方向 × ω。</summary>
        Public ReadOnly Property OmegaVector As Moira.CFDEngine.Eulerian.EulerianVertex

        ''' <summary>
        ''' 虚拟桨盘驱动强度（1/s）。0 表示仅使用科氏/离心项（即依赖桨叶壁面驱动）。
        ''' 典型取值：1~10 倍的 |Ω|。
        ''' </summary>
        Public Property DriveStrength As Double = 0

        Private Sub New(mesh As EulerianFvMesh, cells As Integer(), omega As Double,
                        axisPoint As Moira.CFDEngine.Eulerian.EulerianVertex,
                        axisDirection As Moira.CFDEngine.Eulerian.EulerianVertex)

            Me.Mesh = mesh
            Me.Cells = cells
            Me.Omega = omega
            Me.AxisPoint = axisPoint
            Me.AxisDirection = axisDirection
            Me.OmegaVector = axisDirection * omega
        End Sub

        ''' <summary>旋转区单元数。</summary>
        Public ReadOnly Property CellCount As Integer
            Get
                Return Cells.Length
            End Get
        End Property

        ''' <summary>是否是空的（未标记任何单元）。</summary>
        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return Cells.Length = 0
            End Get
        End Property

        ''' <summary>单元到旋转轴的距离。</summary>
        Public Function Radius(cell As Integer) As Double
            Dim p As Moira.CFDEngine.Eulerian.EulerianVertex = Mesh.CellCentre(cell) - AxisPoint
            Dim a As Moira.CFDEngine.Eulerian.EulerianVertex = AxisDirection
            Dim proj As Double = p.X * a.X + p.Y * a.Y + p.Z * a.Z
            Dim perp As Moira.CFDEngine.Eulerian.EulerianVertex =
                New Moira.CFDEngine.Eulerian.EulerianVertex(p.X - proj * a.X, p.Y - proj * a.Y, p.Z - proj * a.Z)

            Return perp.Length
        End Function

        ''' <summary>单元 c 的刚体旋转目标速度 Ω×r。</summary>
        Public Sub TargetVelocity(cell As Integer, ByRef u As Double, ByRef v As Double, ByRef w As Double)
            Dim r As Moira.CFDEngine.Eulerian.EulerianVertex = Mesh.CellCentre(cell) - AxisPoint
            Dim om As Moira.CFDEngine.Eulerian.EulerianVertex = OmegaVector

            u = om.Y * r.Z - om.Z * r.Y
            v = om.Z * r.X - om.X * r.Z
            w = om.X * r.Y - om.Y * r.X
        End Sub

        ''' <summary>
        ''' 由单元区域标记创建一个 MRF 区。
        ''' </summary>
        ''' <param name="mesh">网格</param>
        ''' <param name="cells">旋转区单元数组（通常取自 EulerianCellZones.MrfCells）</param>
        ''' <param name="omega">角速度（rad/s）</param>
        ''' <param name="axisPoint">轴上一点</param>
        ''' <param name="axisDirection">轴方向（任意长度，内部单位化）</param>
        ''' <param name="driveStrength">虚拟桨盘驱动强度（0 = 关闭）</param>
        Public Shared Function Create(mesh As EulerianFvMesh, cells As Integer(), omega As Double,
                                      axisPoint As Moira.CFDEngine.Eulerian.EulerianVertex,
                                      axisDirection As Moira.CFDEngine.Eulerian.EulerianVertex,
                                      Optional driveStrength As Double = 0) As EulerianMrfZone

            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

            Dim dir As Moira.CFDEngine.Eulerian.EulerianVertex = axisDirection.Normalized()

            If dir.Length <= 0 Then
                dir = New Moira.CFDEngine.Eulerian.EulerianVertex(0, 0, 1)
            End If

            Dim zone As New EulerianMrfZone(mesh, If(cells, New Integer(-1) {}), omega, axisPoint, dir)

            zone.DriveStrength = driveStrength

            Return zone
        End Function

        ''' <summary>
        ''' 由 Z 轴圆柱判据创建一个 MRF 区（搅拌桨最常见的情形）。
        ''' </summary>
        Public Shared Function CreateCylinder(mesh As EulerianFvMesh,
                                              centerX As Double, centerY As Double,
                                              radius As Double, zMin As Double, zMax As Double,
                                              omega As Double,
                                              Optional driveStrength As Double = 0) As EulerianMrfZone

            Dim spec As EulerianCellZoneSpec = EulerianCellZoneSpec.Cylinder(
                centerX, centerY, radius, zMin, zMax, "mrf", EulerianCellZoneKind.Mrf)

            Dim zones As EulerianCellZones = EulerianCellZones.Build(mesh, {spec})

            Return Create(mesh, zones.MrfCells, omega,
                          New Moira.CFDEngine.Eulerian.EulerianVertex(centerX, centerY, zMin),
                          New Moira.CFDEngine.Eulerian.EulerianVertex(0, 0, 1),
                          driveStrength)
        End Function

        Public ReadOnly Property Diagnostics As String
            Get
                Return $"MRF zone: {CellCount} cells, omega={Omega.ToString("G6")} rad/s, axis={AxisDirection}, driveStrength={DriveStrength:G4}"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"EulerianMrfZone(cells={CellCount}, omega={Omega:G4})"
        End Function
    End Class

    ''' <summary>MRF 源项装配。</summary>
    Public Module MrfModel

        ''' <summary>
        ''' 把旋转参考系的科氏（2Ω×u）与离心（Ω×(Ω×r)）源项加入动量方程的分量 component。
        ''' 动力学形式：源项按 ρ_P·V_P 加权（单位 N）。
        ''' </summary>
        ''' <param name="rhoCell">逐单元密度（长度 = CellCount）</param>
        Public Sub AddFrameSource(m As FvMatrix, mrf As EulerianMrfZone,
                                  u As EulerianField, component As Integer,
                                  rhoCell As Double())

            If mrf Is Nothing OrElse mrf.IsEmpty Then Return
            If std.Abs(mrf.Omega) < 1.0E-30 Then Return

            Dim mesh As EulerianFvMesh = m.Mesh
            Dim vol As Double() = mesh.CellVolume
            Dim om As Moira.CFDEngine.Eulerian.EulerianVertex = mrf.OmegaVector
            Dim ax As Moira.CFDEngine.Eulerian.EulerianVertex = mrf.AxisPoint
            Dim n As Integer = mesh.CellCount

            For Each c As Integer In mrf.Cells
                Dim r As Moira.CFDEngine.Eulerian.EulerianVertex = mesh.CellCentre(c) - ax

                ' Ω × r
                Dim crx As Double = om.Y * r.Z - om.Z * r.Y
                Dim cry As Double = om.Z * r.X - om.X * r.Z
                Dim crz As Double = om.X * r.Y - om.Y * r.X

                ' u_c（三个分量）
                Dim ux As Double = u.Values(c)
                Dim uy As Double = If(u.Components >= 2, u.Values(n + c), 0)
                Dim uz As Double = If(u.Components >= 3, u.Values(2 * n + c), 0)

                ' 2Ω × u
                Dim cox As Double = 2.0 * (om.Y * uz - om.Z * uy)
                Dim coy As Double = 2.0 * (om.Z * ux - om.X * uz)
                Dim coz As Double = 2.0 * (om.X * uy - om.Y * ux)

                ' Ω × (Ω × r) = Ω × cr
                Dim cex As Double = om.Y * crz - om.Z * cry
                Dim cey As Double = om.Z * crx - om.X * crz
                Dim cez As Double = om.X * cry - om.Y * crx

                Dim s As Double

                Select Case component
                    Case 0
                        s = cox + cex
                    Case 1
                        s = coy + cey
                    Case Else
                        s = coz + cez
                End Select

                ' 算子右端取负：−ρ·(2Ω×u + Ω×(Ω×r))·V
                If s <> 0 Then
                    m.AddSource(c, -rhoCell(c) * vol(c) * s)
                End If
            Next
        End Sub

        ''' <summary>
        ''' 虚拟桨盘驱动：把桨区单元的切向速度向 Ω×r 拉（显式源，不破坏轴向流动）。
        ''' 仅在 <see cref="EulerianMrfZone.DriveStrength"/> &gt; 0 时生效。
        ''' </summary>
        ''' <param name="rhoCell">逐单元密度（长度 = CellCount）</param>
        Public Sub AddDriveSource(m As FvMatrix, mrf As EulerianMrfZone,
                                  u As EulerianField, component As Integer,
                                  rhoCell As Double())

            If mrf Is Nothing OrElse mrf.IsEmpty Then Return
            If mrf.DriveStrength <= 0 Then Return

            Dim mesh As EulerianFvMesh = m.Mesh
            Dim vol As Double() = mesh.CellVolume
            Dim a As Moira.CFDEngine.Eulerian.EulerianVertex = mrf.AxisDirection
            Dim n As Integer = mesh.CellCount
            Dim s As Double = mrf.DriveStrength

            For Each c As Integer In mrf.Cells
                Dim tu, tv, tw As Double

                Call mrf.TargetVelocity(c, tu, tv, tw)

                Dim ux As Double = u.Values(c)
                Dim uy As Double = If(u.Components >= 2, u.Values(n + c), 0)
                Dim uz As Double = If(u.Components >= 3, u.Values(2 * n + c), 0)

                ' 切向分量 = u − (u·â)â
                Dim dot As Double = ux * a.X + uy * a.Y + uz * a.Z
                Dim uxTan As Double = ux - dot * a.X
                Dim uyTan As Double = uy - dot * a.Y
                Dim uzTan As Double = uz - dot * a.Z

                Dim su As Double
                Dim sv As Double
                Dim sw As Double

                Select Case component
                    Case 0
                        su = tu
                        sv = 0
                        sw = 0
                    Case 1
                        su = 0
                        sv = tv
                        sw = 0
                    Case Else
                        su = 0
                        sv = 0
                        sw = tw
                End Select

                Dim target As Double = su + sv + sw
                Dim current As Double = If(component = 0, uxTan, If(component = 1, uyTan, uzTan))

                ' 源 = ρ·s·(target − current)；target 在轴向分量为 0，因此只驱动切向
                m.AddSource(c, rhoCell(c) * vol(c) * s * (target - current))
            Next
        End Sub

    End Module

End Namespace
