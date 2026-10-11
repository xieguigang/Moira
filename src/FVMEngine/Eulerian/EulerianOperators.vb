' /********************************************************************************/
'
'   EulerianOperators.vb
'
'   离散算子层 —— 把散度 / 梯度 / 拉普拉斯映射到面通量求和（docs\FVM_Eulerian.md §四）。
'
'   约定（与 EulerianFvMesh 的几何量一致）：
'       S_f 恒自 Owner 指向 Neighbour（边界面指向单元外部），
'       因此"面通量 F_f = u_f·S_f"在 Owner 单元天然是出流为正。
'       单元方程统一写成          a_P·φ_P + Σ_N a_N·φ_N = b_P
'       （即所有隐式项进矩阵，所有已知项移到右端 b）。
'
'   已实现算子：
'     插值   : InterpolateInternalFaces / InterpolateAllFaces
'     梯度   : CellGradient（Gauss-Green，2 阶）/ CellGradientLeastSquares（非正交网格更稳）
'     散度   : FvcDiv（Σ_f faceValue_f，显式）
'     拉普拉斯: FvmLaplacian（正交隐式 + 非正交显式延迟修正 + 边界 BC 系数）
'     对流   : FvmDiv（迎风 / 中心 / 线性迎风 / QUICK，后两者为有界延迟修正）
'     时间项 : FvmDdt（Euler 隐式）
'     源项   : FvmSp（隐式线性源）、FvcSource（显式源）
'     通量   : FaceVolumetricFlux / ConstrainedFaceFlux（constrainHbyA 语义）/ LaplacianFaceFlux
'
'   为什么对流采用"迎风隐式 + 高阶延迟修正"：
'       非结构多面体网格上直接隐式实现 QUICK/TVD 会让矩阵失去对角占优，
'       在 SIMPLE 外迭代下极易发散（docs\FVM_Eulerian.md §4.1 的稳定性列）。
'       延迟修正把高阶项放到右端，用上一轮场值显式计算，兼顾精度与稳健性；
'       并对重构出的面值做宿主单元上下界钳制（单调性保护）。
'
'   混合精度契约：
'       本文件全部 double（系数组装 + 边界处理），是"非热点"路径。
'       热点（内迭代线性求解）在 EulerianFvmCompute 中以 float32 + CUDA 执行。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports Microsoft.VisualBasic.Math.SIMD
Imports std = System.Math

Namespace Eulerian

    ''' <summary>对流离散格式。</summary>
    Public Enum EulerianConvectionScheme

        ''' <summary>一阶迎风（无条件稳定，数值扩散大；默认）。</summary>
        Upwind = 0

        ''' <summary>中心差分（2 阶，易振荡；需 Péclet &lt; 2）。</summary>
        Central = 1

        ''' <summary>线性迎风（2 阶，梯度重构 + 上下界钳制）。</summary>
        LinearUpwind = 2

        ''' <summary>QUICK（3 阶，梯度重构取 0.5 系数 + 上下界钳制）。</summary>
        Quick = 3
    End Enum

    ''' <summary>有限体积离散算子集合（全部作用于 <see cref="EulerianFvMesh"/> + <see cref="EulerianField"/>）。</summary>
    Public Module EulerianOperators

#Region "面插值"

        ''' <summary>
        ''' 线性插值到内部面：φ_f = w_f·φ_P + (1−w_f)·φ_Q。
        ''' </summary>
        ''' <param name="phi">场</param>
        ''' <param name="component">分量</param>
        ''' <param name="faceValues">输出，长度 = <see cref="EulerianFvMesh.InternalFaceCount"/></param>
        Public Sub InterpolateInternalFaces(phi As EulerianField, component As Integer, faceValues As Double())
            Dim m As EulerianFvMesh = phi.Mesh
            Dim n As Integer = m.InternalFaceCount

            If faceValues Is Nothing OrElse faceValues.Length < n Then
                Throw New ArgumentException($"faceValues 长度必须 ≥ {n}", NameOf(faceValues))
            End If

            Dim w As Double() = m.InterpWeight
            Dim ifc As Integer() = m.InternalFaces
            Dim ow As Integer() = m.InternalOwner
            Dim nb As Integer() = m.InternalNeighbour
            Dim v As Double() = phi.Values
            Dim off As Integer = component * m.CellCount

            For i As Integer = 0 To n - 1
                Dim wi As Double = w(i)

                faceValues(i) = wi * v(off + ow(i)) + (1.0 - wi) * v(off + nb(i))
            Next
        End Sub

        ''' <summary>分配并返回内部面插值数组。</summary>
        Public Function InternalFaceValues(phi As EulerianField, component As Integer) As Double()
            Dim a(phi.Mesh.InternalFaceCount - 1) As Double

            Call InterpolateInternalFaces(phi, component, a)
            Return a
        End Function

        ''' <summary>
        ''' 插值到**全部面**（内部面线性插值；边界面取 <see cref="EulerianPatchField"/> 的边界值，
        ''' 因此调用前需先执行 <see cref="EulerianField.UpdateBoundaries"/>）。
        ''' </summary>
        Public Sub InterpolateAllFaces(phi As EulerianField, component As Integer, faceValues As Double())
            Dim m As EulerianFvMesh = phi.Mesh
            Dim nF As Integer = m.FaceCount

            If faceValues Is Nothing OrElse faceValues.Length < nF Then
                Throw New ArgumentException($"faceValues 长度必须 ≥ {nF}", NameOf(faceValues))
            End If

            Call InterpolateInternalFaces(phi, component, faceValues)

            For p As Integer = 0 To m.PatchCount - 1
                Dim geo As EulerianPatchGeometry = m.Patches(p)
                Dim pf As EulerianPatchField = phi.Boundaries(p)

                For k As Integer = 0 To geo.FaceCount - 1
                    faceValues(geo.FaceIndices(k)) = pf.Value(k, component)
                Next
            Next
        End Sub

#End Region

#Region "扩散系数（面值）"

        ''' <summary>常数扩散系数 → 全部面同值（长度 = FaceCount）。</summary>
        Public Function UniformFaceGamma(mesh As EulerianFvMesh, gamma As Double) As Double()
            Dim n As Integer = mesh.FaceCount
            Dim a(n - 1) As Double

            For i As Integer = 0 To n - 1
                a(i) = gamma
            Next

            Return a
        End Function

        ''' <summary>
        ''' 场扩散系数 → 面值：内部面线性插值；边界面取 Owner 单元值。
        ''' （对存在强间断的两相流，调用方可改用调和平均，见 VofModel）
        ''' </summary>
        Public Function FaceGamma(mesh As EulerianFvMesh, gamma As EulerianField) As Double()
            Dim nF As Integer = mesh.FaceCount
            Dim a(nF - 1) As Double
            Dim w As Double() = mesh.InterpWeight
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour
            Dim g As Double() = gamma.Values

            For i As Integer = 0 To ifc.Length - 1
                Dim wi As Double = w(i)

                a(ifc(i)) = wi * g(ow(i)) + (1.0 - wi) * g(nb(i))
            Next

            For p As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(p)

                For k As Integer = 0 To geo.FaceCount - 1
                    a(geo.FaceIndices(k)) = g(geo.OwnerCells(k))
                Next
            Next

            Return a
        End Function

#End Region

#Region "梯度"

        ''' <summary>
        ''' Gauss-Green 单元梯度：∇φ_P = (1/V_P)·Σ_f φ_f·S_f。
        ''' 内部面用线性插值，边界面用边界值（2 阶，近正交网格上足够）。
        ''' </summary>
        Public Sub CellGradient(phi As EulerianField, component As Integer,
                                gx As Double(), gy As Double(), gz As Double())

            Dim m As EulerianFvMesh = phi.Mesh
            Dim nc As Integer = m.CellCount

            ValidateGradBuffers(nc, gx, gy, gz)
            Array.Clear(gx, 0, nc)
            Array.Clear(gy, 0, nc)
            Array.Clear(gz, 0, nc)

            Dim v As Double() = phi.Values
            Dim off As Integer = component * nc
            Dim sfArr As Moira.CFDEngine.Eulerian.EulerianVertex() = m.Sf
            Dim ifc As Integer() = m.InternalFaces
            Dim ow As Integer() = m.InternalOwner
            Dim nb As Integer() = m.InternalNeighbour
            Dim w As Double() = m.InterpWeight

            ' ---- 内部面 ----
            For i As Integer = 0 To ifc.Length - 1
                Dim f As Integer = ifc(i)
                Dim wi As Double = w(i)
                Dim phiF As Double = wi * v(off + ow(i)) + (1.0 - wi) * v(off + nb(i))
                Dim s As Moira.CFDEngine.Eulerian.EulerianVertex = sfArr(f)
                Dim P As Integer = ow(i)
                Dim Q As Integer = nb(i)

                ' Owner 侧 S_f 朝外为 +；Neighbour 侧为 −
                gx(P) += phiF * s.X
                gy(P) += phiF * s.Y
                gz(P) += phiF * s.Z

                gx(Q) -= phiF * s.X
                gy(Q) -= phiF * s.Y
                gz(Q) -= phiF * s.Z
            Next

            ' ---- 边界面 ----
            For p As Integer = 0 To m.PatchCount - 1
                Dim geo As EulerianPatchGeometry = m.Patches(p)
                Dim pf As EulerianPatchField = phi.Boundaries(p)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim phiB As Double = pf.Value(k, component)
                    Dim s As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k)

                    gx(own) += phiB * s.X
                    gy(own) += phiB * s.Y
                    gz(own) += phiB * s.Z
                Next
            Next

            ' ---- 除以体积 ----
            Dim vol As Double() = m.CellVolume

            For c As Integer = 0 To nc - 1
                Dim iv As Double = If(vol(c) > 0, 1.0 / vol(c), 0)

                gx(c) *= iv
                gy(c) *= iv
                gz(c) *= iv
            Next
        End Sub

        ''' <summary>
        ''' 加权最小二乘（Gauss LSQ）单元梯度：min Σ ( φ_ref − φ_P − ∇φ·d )²。
        ''' 邻域包含内部面邻居与边界面（边界面用边界值），因此壁面单元也能得到
        ''' 良条件的三维梯度；矩阵病态时回退到 Gauss-Green。
        ''' </summary>
        Public Sub CellGradientLeastSquares(phi As EulerianField, component As Integer,
                                           gx As Double(), gy As Double(), gz As Double())

            Dim m As EulerianFvMesh = phi.Mesh
            Dim nc As Integer = m.CellCount

            ValidateGradBuffers(nc, gx, gy, gz)

            Dim v As Double() = phi.Values
            Dim off As Integer = component * nc
            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex() = m.CellCentre
            Dim cellFaceList As List(Of Integer()) = m.Mesh.CellFaces
            Dim fallback As Boolean = False

            For c As Integer = 0 To nc - 1
                gx(c) = 0 : gy(c) = 0 : gz(c) = 0
            Next

            For c As Integer = 0 To nc - 1
                Dim a11 As Double = 0, a12 As Double = 0, a13 As Double = 0
                Dim a22 As Double = 0, a23 As Double = 0, a33 As Double = 0
                Dim bx As Double = 0, by As Double = 0, bz As Double = 0
                Dim phiP As Double = v(off + c)
                Dim cp As Moira.CFDEngine.Eulerian.EulerianVertex = cc(c)

                For Each f As Integer In cellFaceList(c)
                    Dim q As Integer = -1
                    Dim phiRef As Double = 0

                    If m.FaceIsBoundary(f) Then
                        ' 边界面：用边界值（BC 已在调用前 Evaluate）
                        Dim p As Integer = m.FacePatch(f)
                        q = -1
                        phiRef = phi.Boundaries(p).Value(m.FaceInPatch(f), component)
                    Else
                        Dim fo As Integer = m.Mesh.Faces(f).Owner

                        q = If(fo = c, m.Mesh.Faces(f).Neighbour, fo)
                        phiRef = v(off + q)
                    End If

                    Dim d As Moira.CFDEngine.Eulerian.EulerianVertex

                    If q >= 0 Then
                        d = cc(q) - cp
                    Else
                        d = m.Cf(f) - cp
                    End If

                    Dim dphi As Double = phiRef - phiP

                    a11 += d.X * d.X
                    a12 += d.X * d.Y
                    a13 += d.X * d.Z
                    a22 += d.Y * d.Y
                    a23 += d.Y * d.Z
                    a33 += d.Z * d.Z

                    bx += d.X * dphi
                    by += d.Y * dphi
                    bz += d.Z * dphi
                Next

                ' M = [a11 a12 a13; a12 a22 a23; a13 a23 a33]，加正则化容忍近退化邻域
                Dim tr As Double = a11 + a22 + a33

                If tr <= 0 Then
                    fallback = True
                    Exit For
                End If

                Dim eps As Double = tr * 1.0E-12
                Dim r11 As Double = a11 + eps
                Dim r22 As Double = a22 + eps
                Dim r33 As Double = a33 + eps

                Dim det As Double = r11 * (r22 * r33 - a23 * a23) -
                                    a12 * (a12 * r33 - a23 * a13) +
                                    a13 * (a12 * a23 - r22 * a13)

                ' 相对判据：det 太小说明三个方向近乎共面（不可解），回退
                If std.Abs(det) < 1.0E-14 * tr * tr * tr Then
                    fallback = True
                    Exit For
                End If

                Dim i11 As Double = (r22 * r33 - a23 * a23) / det
                Dim i22 As Double = (r11 * r33 - a13 * a13) / det
                Dim i33 As Double = (r11 * r22 - a12 * a12) / det
                Dim i12 As Double = (a13 * a23 - a12 * r33) / det
                Dim i13 As Double = (a12 * a23 - a13 * r22) / det
                Dim i23 As Double = (a13 * a12 - r11 * a23) / det

                gx(c) = i11 * bx + i12 * by + i13 * bz
                gy(c) = i12 * bx + i22 * by + i23 * bz
                gz(c) = i13 * bx + i23 * by + i33 * bz
            Next

            If fallback Then
                ' 存在退化邻域：整体回退到 Gauss-Green（保证任何网格都有可用梯度）
                Call CellGradient(phi, component, gx, gy, gz)
            End If
        End Sub

        Private Sub ValidateGradBuffers(nc As Integer, gx As Double(), gy As Double(), gz As Double())
            If gx Is Nothing OrElse gy Is Nothing OrElse gz Is Nothing Then
                Throw New ArgumentNullException("gx/gy/gz", "梯度缓冲不能为 Nothing")
            End If

            If gx.Length < nc OrElse gy.Length < nc OrElse gz.Length < nc Then
                Throw New ArgumentException($"梯度缓冲长度必须 ≥ {nc}")
            End If
        End Sub

        ''' <summary>
        ''' 面上梯度：内部面为两侧单元梯度的线性插值；边界面取 Owner 单元梯度。
        ''' </summary>
        Public Sub FaceGradient(mesh As EulerianFvMesh, gx As Double(), gy As Double(), gz As Double(),
                                fx As Double(), fy As Double(), fz As Double())

            Dim w As Double() = mesh.InterpWeight
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour

            For i As Integer = 0 To ifc.Length - 1
                Dim f As Integer = ifc(i)
                Dim wi As Double = w(i)

                fx(f) = wi * gx(ow(i)) + (1.0 - wi) * gx(nb(i))
                fy(f) = wi * gy(ow(i)) + (1.0 - wi) * gy(nb(i))
                fz(f) = wi * gz(ow(i)) + (1.0 - wi) * gz(nb(i))
            Next

            For p As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(p)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim f As Integer = geo.FaceIndices(k)
                    Dim own As Integer = geo.OwnerCells(k)

                    fx(f) = gx(own)
                    fy(f) = gy(own)
                    fz(f) = gz(own)
                Next
            Next
        End Sub

#End Region

#Region "显式散度"

        ''' <summary>
        ''' 显式散度：cellValues(P) = Σ_f faceValues_f（f 遍历 P 的所有面，含边界面）。
        ''' 用于 SIMPLE 的压力方程右端 div(phiHbyA)，以及连续性误差诊断。
        ''' </summary>
        Public Sub FvcDiv(mesh As EulerianFvMesh, faceValues As Double(), cellValues As Double())
            Dim nc As Integer = mesh.CellCount

            If cellValues Is Nothing OrElse cellValues.Length < nc Then
                Throw New ArgumentException($"cellValues 长度必须 ≥ {nc}", NameOf(cellValues))
            End If

            Array.Clear(cellValues, 0, nc)

            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour

            For i As Integer = 0 To ifc.Length - 1
                Dim F As Double = faceValues(ifc(i))

                cellValues(ow(i)) += F
                cellValues(nb(i)) -= F
            Next

            For p As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(p)

                For k As Integer = 0 To geo.FaceCount - 1
                    cellValues(geo.OwnerCells(k)) += faceValues(geo.FaceIndices(k))
                Next
            Next
        End Sub

#End Region

#Region "面通量"

        ''' <summary>内部面体积通量：u_f·S_f（线性插值；边界面用边界速度值）。</summary>
        Public Sub FaceVolumetricFlux(u As EulerianField, faceFlux As Double())
            Call FluxFromVectorField(u, u, faceFlux)
        End Sub

        ''' <summary>
        ''' 由 HbyA 与 U 构造面通量 phiHbyA（对应 OpenFOAM <c>constrainHbyA</c>）：
        '''   · 内部面：interpolate(HbyA)_f · S_f；
        '''   · 边界面：若 U 在该 patch 的边界条件"直接规定速度"（入口/壁面/对称/运动壁面），
        '''             则用 u_b·S_f 保证质量守恒；否则用 HbyA 的 Owner 单元值零阶外插。
        ''' </summary>
        Public Sub ConstrainedFaceFlux(hbyA As EulerianField, u As EulerianField, faceFlux As Double())
            Call FluxFromVectorField(hbyA, u, faceFlux)
        End Sub

        Private Sub FluxFromVectorField(vec As EulerianField, u As EulerianField, faceFlux As Double())
            Dim m As EulerianFvMesh = vec.Mesh
            Dim nF As Integer = m.FaceCount

            If vec.Components < 3 Then
                Throw New InvalidOperationException($"场 '{vec.Name}' 不是矢量场，无法构造面通量")
            End If

            If faceFlux Is Nothing OrElse faceFlux.Length < nF Then
                Throw New ArgumentException($"faceFlux 长度必须 ≥ {nF}", NameOf(faceFlux))
            End If

            Dim nc As Integer = m.CellCount
            Dim hx As Double() = vec.Values
            Dim hyOff As Integer = nc
            Dim hzOff As Integer = 2 * nc

            Dim sfArr As Moira.CFDEngine.Eulerian.EulerianVertex() = m.Sf
            Dim w As Double() = m.InterpWeight
            Dim ifc As Integer() = m.InternalFaces
            Dim ow As Integer() = m.InternalOwner
            Dim nb As Integer() = m.InternalNeighbour

            For i As Integer = 0 To ifc.Length - 1
                Dim f As Integer = ifc(i)
                Dim wi As Double = w(i)
                Dim P As Integer = ow(i)
                Dim Q As Integer = nb(i)

                Dim ux As Double = wi * hx(P) + (1.0 - wi) * hx(Q)
                Dim uy As Double = wi * hx(hyOff + P) + (1.0 - wi) * hx(hyOff + Q)
                Dim uz As Double = wi * hx(hzOff + P) + (1.0 - wi) * hx(hzOff + Q)
                Dim s As Moira.CFDEngine.Eulerian.EulerianVertex = sfArr(f)

                faceFlux(f) = ux * s.X + uy * s.Y + uz * s.Z
            Next

            For p As Integer = 0 To m.PatchCount - 1
                Dim geo As EulerianPatchGeometry = m.Patches(p)
                Dim pfU As EulerianPatchField = u.Boundaries(p)
                Dim fixedVelocity As Boolean = pfU.Condition IsNot Nothing AndAlso pfU.Condition.PrescribesVelocity

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim f As Integer = geo.FaceIndices(k)
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim s As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k)

                    If fixedVelocity Then
                        Dim bx As Double = pfU.Value(k, 0)
                        Dim by As Double = pfU.Value(k, 1)
                        Dim bz As Double = pfU.Value(k, 2)

                        faceFlux(f) = bx * s.X + by * s.Y + bz * s.Z
                    Else
                        faceFlux(f) = hx(own) * s.X + hx(hyOff + own) * s.Y + hx(hzOff + own) * s.Z
                    End If
                Next
            Next
        End Sub

        ''' <summary>质量通量：ṁ_f = ρ_f·(u_f·S_f)。</summary>
        Public Function MassFlux(faceFlux As Double(), rhoFace As Double()) As Double()
            Dim n As Integer = faceFlux.Length
            Dim r(n - 1) As Double

            For i As Integer = 0 To n - 1
                r(i) = rhoFace(i) * faceFlux(i)
            Next

            Return r
        End Function

#End Region

#Region "拉普拉斯（隐式）"

        ''' <summary>
        ''' 隐式拉普拉斯（OpenFOAM <c>fvm::laplacian</c> 语义）：
        '''     装配 <b>+div(Γ∇φ) ≈ Σ_f Γ_f [ Δ_f (φ_Q − φ_P) + (∇φ)_f·k_f ]</b>
        ''' 正交部分进矩阵（对角 + 非对角），非正交部分（k_f 项）显式进右端，
        ''' 边界贡献走边界条件的梯度系数（已含 Δ_f / |S_f| 的量纲）。
        '''
        ''' 符号约定（重要）：
        '''   · 输运方程的扩散项是 <c>−div(Γ∇φ)</c>，调用时传 <c>sign := -1.0</c>
        '''     （即  FvmDiv(...) 与 FvmLaplacian(sign:=-1) 相加 表示 div − div(Γ∇) ）。
        '''   · 压力方程是 <c>+div(rAU∇p) = div(phiHbyA)</c>，用默认 <c>sign = +1</c>。
        ''' 这与 OpenFOAM 的 <c>fvm::div(phi,U) - fvm::laplacian(nu,U)</c> /
        ''' <c>fvm::laplacian(rAtU(),p) == fvc::div(phiHbyA)</c> 一一对应。
        ''' </summary>
        ''' <param name="m">目标方程（会被累加）</param>
        ''' <param name="phi">被离散的场</param>
        ''' <param name="component">分量</param>
        ''' <param name="gammaFace">逐面扩散系数 Γ_f（长度 = FaceCount）</param>
        ''' <param name="sign">算子符号：+1 表示 +div(Γ∇φ)；−1 表示 −div(Γ∇φ)</param>
        ''' <param name="nonOrthogonalCorrection">是否启用非正交修正（强烈建议 True）</param>
        ''' <param name="gx">可选的已算好的单元梯度（避免重复计算）；Nothing 时内部计算</param>
        Public Sub FvmLaplacian(m As FvMatrix, phi As EulerianField, component As Integer,
                                gammaFace As Double(), Optional sign As Double = 1.0,
                                Optional nonOrthogonalCorrection As Boolean = True,
                                Optional gx As Double() = Nothing, Optional gy As Double() = Nothing,
                                Optional gz As Double() = Nothing)

            Dim mesh As EulerianFvMesh = m.Mesh
            Dim nc As Integer = mesh.CellCount
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour
            Dim dc As Double() = mesh.DeltaCoeff
            Dim korr As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.NonOrthCorrection
            Dim nInt As Integer = ifc.Length

            ' 把符号一次性并入逐面系数（内部面 / 非正交修正 / 边界贡献三处共享），
            ' 避免就地修改调用方传入的 gammaFace 数组。
            Dim nFace As Integer = gammaFace.Length
            Dim gs(nFace - 1) As Double

            For f As Integer = 0 To nFace - 1
                gs(f) = gammaFace(f) * sign
            Next

            ' ---- 正交隐式部分：+ΓΔ(φ_P − φ_Q) ----
            For i As Integer = 0 To nInt - 1
                Dim f As Integer = ifc(i)
                Dim coeff As Double = gs(f) * dc(i)
                Dim P As Integer = ow(i)
                Dim Q As Integer = nb(i)

                m.AddDiag(P, coeff)
                m.AddOwnerCoeff(f, -coeff)
                m.AddDiag(Q, coeff)
                m.AddNeighbourCoeff(f, -coeff)
            Next

            ' ---- 非正交显式修正：Γ_f·(∇φ)_f·k_f（属算子的已知部分 → 移到右端） ----
            If nonOrthogonalCorrection Then
                Dim useExternal As Boolean = gx IsNot Nothing AndAlso gy IsNot Nothing AndAlso gz IsNot Nothing
                Dim cx As Double() = gx
                Dim cy As Double() = gy
                Dim cz As Double() = gz

                If Not useExternal Then
                    cx = New Double(nc - 1) {}
                    cy = New Double(nc - 1) {}
                    cz = New Double(nc - 1) {}
                    Call CellGradient(phi, component, cx, cy, cz)
                End If

                Dim fx(nInt - 1) As Double
                Dim fy(nInt - 1) As Double
                Dim fz(nInt - 1) As Double
                Dim w As Double() = mesh.InterpWeight

                For i As Integer = 0 To nInt - 1
                    Dim wi As Double = w(i)

                    fx(i) = wi * cx(ow(i)) + (1.0 - wi) * cx(nb(i))
                    fy(i) = wi * cy(ow(i)) + (1.0 - wi) * cy(nb(i))
                    fz(i) = wi * cz(ow(i)) + (1.0 - wi) * cz(nb(i))
                Next

                For i As Integer = 0 To nInt - 1
                    Dim f As Integer = ifc(i)
                    Dim k As Moira.CFDEngine.Eulerian.EulerianVertex = korr(i)
                    Dim corr As Double = gs(f) * (fx(i) * k.X + fy(i) * k.Y + fz(i) * k.Z)

                    If corr <> 0 Then
                        m.AddSource(ow(i), -corr)
                        m.AddSource(nb(i), corr)
                    End If
                Next
            End If

            ' ---- 边界贡献 ----
            Call AddBoundaryLaplacian(m, phi, component, gs)
        End Sub

        ''' <summary>
        ''' 拉普拉斯边界贡献（<paramref name="gammaFace"/> 已含算子符号）：
        ''' 对角 += Γ_f·GradInternalCoeff(i)；已知部分 Γ_f·GradBoundaryCoeff(comp·n+i)
        ''' 属算子 → 移到方程右端取负。
        ''' </summary>
        Private Sub AddBoundaryLaplacian(m As FvMatrix, phi As EulerianField, component As Integer, gammaFace As Double())
            Dim mesh As EulerianFvMesh = m.Mesh

            For p As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(p)
                Dim pf As EulerianPatchField = phi.Boundaries(p)
                Dim bc As EulerianBoundaryCondition = pf.Condition

                ' 未绑定边界条件 → 等价于零梯度（无贡献）
                If bc Is Nothing Then
                    Continue For
                End If

                Dim gi As Double() = bc.GradientInternalCoeffs()
                Dim gb As Double() = bc.GradientBoundaryCoeffs()
                Dim n As Integer = geo.FaceCount
                Dim off As Integer = component * n

                For k As Integer = 0 To n - 1
                    Dim gf As Double = gammaFace(geo.FaceIndices(k))
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim a As Double = gf * gi(k)
                    Dim b As Double = gf * gb(off + k)

                    If a <> 0 Then
                        m.AddDiag(own, a)
                    End If

                    If b <> 0 Then
                        m.AddSource(own, -b)
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' 拉普拉斯算子对应的面通量（OpenFOAM <c>pEqn.flux()</c> 的等价物）：
        '''   · 内部面：Γ_f·Δ_f·(φ_Q − φ_P)
        '''   · 边界面：Γ_f·[ GradInternalCoeff·φ_P + GradBoundaryCoeff ]
        ''' 用于 SIMPLE/PIMPLE 的速度与通量修正，保证离散连续性。
        ''' </summary>
        Public Sub LaplacianFaceFlux(mesh As EulerianFvMesh, gammaFace As Double(),
                                     phi As EulerianField, component As Integer, faceFlux As Double())

            Dim nF As Integer = mesh.FaceCount
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour
            Dim dc As Double() = mesh.DeltaCoeff
            Dim v As Double() = phi.Values
            Dim off As Integer = component * mesh.CellCount

            For f As Integer = 0 To nF - 1
                faceFlux(f) = 0
            Next

            For i As Integer = 0 To ifc.Length - 1
                Dim f As Integer = ifc(i)

                faceFlux(f) = gammaFace(f) * dc(i) * (v(off + nb(i)) - v(off + ow(i)))
            Next

            For p As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(p)
                Dim pf As EulerianPatchField = phi.Boundaries(p)
                Dim bc As EulerianBoundaryCondition = pf.Condition

                If bc Is Nothing Then
                    Continue For
                End If

                Dim gi As Double() = bc.GradientInternalCoeffs()
                Dim gb As Double() = bc.GradientBoundaryCoeffs()
                Dim n As Integer = geo.FaceCount
                Dim cOff As Integer = component * n

                For k As Integer = 0 To n - 1
                    Dim own As Integer = geo.OwnerCells(k)

                    faceFlux(geo.FaceIndices(k)) = gammaFace(geo.FaceIndices(k)) *
                        (gi(k) * v(off + own) + gb(cOff + k))
                Next
            Next
        End Sub

#End Region

#Region "对流（隐式）"

        ''' <summary>
        ''' 隐式对流：Σ_f F_f·φ_f（F_f 为面通量，出 Owner 为正）。
        ''' 迎风与中心差分完全隐式；LinearUpwind / QUICK 采用"迎风隐式 + 高阶延迟修正"。
        ''' </summary>
        ''' <param name="m">目标方程（会被累加）</param>
        ''' <param name="faceFlux">面通量 F_f（长度 = FaceCount）</param>
        ''' <param name="phi">被输运的场</param>
        ''' <param name="component">分量</param>
        ''' <param name="scheme">对流格式</param>
        Public Sub FvmDiv(m As FvMatrix, faceFlux As Double(), phi As EulerianField, component As Integer,
                          Optional scheme As EulerianConvectionScheme = EulerianConvectionScheme.Upwind)

            Dim mesh As EulerianFvMesh = m.Mesh
            Dim nc As Integer = mesh.CellCount
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour
            Dim w As Double() = mesh.InterpWeight
            Dim v As Double() = phi.Values
            Dim off As Integer = component * nc
            Dim ifcLength As Integer = ifc.Length

            ' ---- 高阶格式：先算一次单元梯度，再在面循环内以上风单元做有界重构 ----
            Dim useHighOrder As Boolean =
                scheme = EulerianConvectionScheme.LinearUpwind OrElse scheme = EulerianConvectionScheme.Quick
            Dim lambda As Double = If(scheme = EulerianConvectionScheme.Quick, 0.5, 1.0)
            Dim gx As Double() = Nothing
            Dim gy As Double() = Nothing
            Dim gz As Double() = Nothing

            If useHighOrder Then
                gx = New Double(nc - 1) {}
                gy = New Double(nc - 1) {}
                gz = New Double(nc - 1) {}
                Call CellGradientLeastSquares(phi, component, gx, gy, gz)
            End If

            Dim cfArr As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.Cf
            Dim ccArr As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.CellCentre

            For i As Integer = 0 To ifcLength - 1
                Dim f As Integer = ifc(i)
                Dim flux As Double = faceFlux(f)
                Dim own As Integer = ow(i)
                Dim nbr As Integer = nb(i)

                If scheme = EulerianConvectionScheme.Central Then
                    Dim wi As Double = w(i)

                    ' Owner 行：+flux·(w φ_P + (1−w) φ_Q)
                    m.AddDiag(own, flux * wi)
                    m.AddOwnerCoeff(f, flux * (1.0 - wi))

                    ' Neighbour 行：−flux·(w φ_P + (1−w) φ_Q)
                    m.AddNeighbourCoeff(f, -flux * wi)
                    m.AddDiag(nbr, -flux * (1.0 - wi))
                Else
                    ' 迎风隐式部分（1 阶）
                    If flux >= 0 Then
                        m.AddDiag(own, flux)
                        m.AddNeighbourCoeff(f, -flux)
                    Else
                        m.AddOwnerCoeff(f, flux)
                        m.AddDiag(nbr, -flux)
                    End If
                End If

                ' 延迟修正：flux·(φ_f^HO − φ_f^UP) 移到右端
                If useHighOrder AndAlso flux <> 0 Then
                    Dim up As Integer = If(flux >= 0, own, nbr)
                    Dim dd As Moira.CFDEngine.Eulerian.EulerianVertex = cfArr(f) - ccArr(up)
                    Dim phiUp As Double = v(off + up)
                    Dim phiHo As Double = phiUp + lambda * (gx(up) * dd.X + gy(up) * dd.Y + gz(up) * dd.Z)

                    ' 单调性保护（宿主两单元的上下界钳制）
                    Dim lo As Double = std.Min(v(off + own), v(off + nbr))
                    Dim hi As Double = std.Max(v(off + own), v(off + nbr))

                    If phiHo < lo Then phiHo = lo
                    If phiHo > hi Then phiHo = hi

                    Dim corr As Double = flux * (phiHo - phiUp)

                    If corr <> 0 Then
                        m.AddSource(own, -corr)
                        m.AddSource(nbr, corr)
                    End If
                End If
            Next

            ' ---- 边界贡献（走边界条件的对流系数） ----
            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim pf As EulerianPatchField = phi.Boundaries(pi)
                Dim bc As EulerianBoundaryCondition = pf.Condition

                If bc Is Nothing Then
                    Continue For
                End If

                Dim vi As Double() = bc.ValueInternalCoeffs()
                Dim vb As Double() = bc.ValueBoundaryCoeffs()
                Dim n As Integer = geo.FaceCount
                Dim cOff As Integer = component * n

                For k As Integer = 0 To n - 1
                    Dim flux As Double = faceFlux(geo.FaceIndices(k))
                    Dim own As Integer = geo.OwnerCells(k)

                    If flux = 0 Then
                        Continue For
                    End If

                    m.AddDiag(own, flux * vi(k))
                    m.AddSource(own, -flux * vb(cOff + k))
                Next
            Next
        End Sub

        ''' <summary>
        ''' 内部面上的上风高阶重构值（对外暴露，供诊断与二阶精度检查使用）：
        '''   φ_f = φ_up + λ·(∇φ)_up·(x_f − x_up)，并钳制在 [min(φ_P,φ_Q), max(φ_P,φ_Q)]。
        ''' </summary>
        Public Function HighOrderFaceValues(phi As EulerianField, component As Integer,
                                           faceFlux As Double(),
                                           Optional lambda As Double = 1.0) As Double()

            Dim mesh As EulerianFvMesh = phi.Mesh
            Dim nc As Integer = mesh.CellCount
            Dim nInt As Integer = mesh.InternalFaceCount
            Dim gx(nc - 1) As Double
            Dim gy(nc - 1) As Double
            Dim gz(nc - 1) As Double

            Call CellGradientLeastSquares(phi, component, gx, gy, gz)

            Dim v As Double() = phi.Values
            Dim off As Integer = component * nc
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour
            Dim cfArr As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.Cf
            Dim ccArr As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.CellCentre

            Dim r(nInt - 1) As Double

            For i As Integer = 0 To nInt - 1
                Dim f As Integer = ifc(i)
                Dim own As Integer = ow(i)
                Dim nbr As Integer = nb(i)
                Dim flux As Double = faceFlux(f)
                Dim up As Integer = If(flux >= 0, own, nbr)
                Dim dd As Moira.CFDEngine.Eulerian.EulerianVertex = cfArr(f) - ccArr(up)
                Dim phiUp As Double = v(off + up)
                Dim phiHo As Double = phiUp + lambda * (gx(up) * dd.X + gy(up) * dd.Y + gz(up) * dd.Z)

                Dim lo As Double = std.Min(v(off + own), v(off + nbr))
                Dim hi As Double = std.Max(v(off + own), v(off + nbr))

                If phiHo < lo Then phiHo = lo
                If phiHo > hi Then phiHo = hi

                r(i) = phiHo
            Next

            Return r
        End Function

#End Region

#Region "时间项与源项"

        ''' <summary>
        ''' Euler 隐式时间项：V_P·ρ_P·(φ_P − φ_P^old)/Δt
        ''' → 对角 += V ρ/Δt，右端 += V ρ φ^old/Δt。
        ''' </summary>
        Public Sub FvmDdt(m As FvMatrix, phi As EulerianField, phiPrev As Double(), component As Integer,
                          deltaT As Double, rho As Double)

            If deltaT <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(deltaT), "时间步长必须为正")
            End If

            Dim mesh As EulerianFvMesh = m.Mesh
            Dim nc As Integer = mesh.CellCount
            Dim vol As Double() = mesh.CellVolume
            Dim v As Double() = phi.Values
            Dim off As Integer = component * nc

            For c As Integer = 0 To nc - 1
                Dim a As Double = vol(c) * rho / deltaT

                m.AddDiag(c, a)

                Dim prev As Double = If(phiPrev IsNot Nothing AndAlso phiPrev.Length > c, phiPrev(c), v(off + c))

                m.AddSource(c, a * prev)
            Next
        End Sub

        ''' <summary>Euler 隐式时间项（逐单元密度）。</summary>
        Public Sub FvmDdt(m As FvMatrix, phi As EulerianField, phiPrev As Double(), component As Integer,
                          deltaT As Double, rho As Double())

            If deltaT <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(deltaT), "时间步长必须为正")
            End If

            Dim mesh As EulerianFvMesh = m.Mesh
            Dim nc As Integer = mesh.CellCount
            Dim vol As Double() = mesh.CellVolume
            Dim v As Double() = phi.Values
            Dim off As Integer = component * nc

            For c As Integer = 0 To nc - 1
                Dim a As Double = vol(c) * rho(c) / deltaT

                m.AddDiag(c, a)

                Dim prev As Double = If(phiPrev IsNot Nothing AndAlso phiPrev.Length > c, phiPrev(c), v(off + c))

                m.AddSource(c, a * prev)
            Next
        End Sub

        ''' <summary>
        ''' 隐式线性源 fvm::Sp(c, φ)：把 c·φ_P 放到对角，保证对角占优。
        ''' 用于"负系数源项"（如湍流耗散、多孔阻力、消光项）。
        ''' </summary>
        Public Sub FvmSp(m As FvMatrix, coefficientPerCell As Double(), phi As EulerianField, component As Integer)
            Dim nc As Integer = m.Mesh.CellCount

            For c As Integer = 0 To nc - 1
                Dim k As Double = coefficientPerCell(c)

                If k <> 0 Then
                    m.AddDiag(c, k)
                End If
            Next
        End Sub

        ''' <summary>显式源项（逐单元）。</summary>
        Public Sub FvcSource(m As FvMatrix, sourcePerCell As Double())
            Call m.AddSourceField(sourcePerCell)
        End Sub

#End Region

#Region "诊断"

        ''' <summary>
        ''' 域内最大连续方程残差 |∇·u|·V_P（连续性误差，SIMPLE 收敛的核心指标）。
        ''' </summary>
        Public Function MaxContinuityError(mesh As EulerianFvMesh, faceFlux As Double()) As Double
            Dim nc As Integer = mesh.CellCount
            Dim d(nc - 1) As Double

            Call FvcDiv(mesh, faceFlux, d)

            Dim vol As Double() = mesh.CellVolume
            Dim m As Double = 0

            For c As Integer = 0 To nc - 1
                Dim e As Double = std.Abs(d(c)) * vol(c)

                If e > m Then m = e
            Next

            Return m
        End Function

        ''' <summary>域内体积流量不平衡 Σ_f F_f（应接近 0；用于诊断边界通量设置错误）。</summary>
        Public Function NetFlux(mesh As EulerianFvMesh, faceFlux As Double()) As Double
            Dim s As Double = 0

            For p As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(p)

                For k As Integer = 0 To geo.FaceCount - 1
                    s += faceFlux(geo.FaceIndices(k))
                Next
            Next

            Return s
        End Function

        ''' <summary>逐 patch 的体积流量（出口为正）。</summary>
        Public Function PatchFlux(mesh As EulerianFvMesh, faceFlux As Double(), patchIndex As Integer) As Double
            Dim geo As EulerianPatchGeometry = mesh.Patches(patchIndex)
            Dim s As Double = 0

            For k As Integer = 0 To geo.FaceCount - 1
                s += faceFlux(geo.FaceIndices(k))
            Next

            Return s
        End Function

#End Region

    End Module

End Namespace
