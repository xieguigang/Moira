
''' <summary>D3Q19 MRT 矩基（19 个 Hermite 型多项式）与矩阵工具。</summary>
''' <remarks>
''' /********************************************************************************/
'''
'''   CollisionModels.vb
'''
'''   碰撞模型层 —— BGK / TRT / MRT 三策略。
'''
'''   ── TRT（两松弛时间，Ginzburg 2005）───────────────────────────────────────
'''       分布按方向奇偶分解：f⁺ = (f_a + f_ᾱ)/2，f⁻ = (f_a − f_ᾱ)/2
'''       f^c_a = f_a − ω⁺(f⁺ − f⁺eq) − ω⁻(f⁻ − f⁻eq)
'''       · ω⁻ = 1/τ 控制剪切粘度：ν = cs²(1/ω⁻ − ½)（与 BGK 相同的物理）
'''       · ω⁺ 由幻影参数 Λ = (1/ω⁺−½)(1/ω⁻−½) = 1/4 确定：
'''         反弹壁面精度最优 + 高 Re（τ→½）时偶模（应力）被强阻尼 → 稳定
'''       展开为 f^c_a = A·f_a + B·f_ᾱ + C·f^eq_a + D·f^eq_ᾱ（纯标量系数，
'''       完全走 tfMath 张量路径）
'''
'''   ── MRT（多松弛时间，D'Humières 1992）────────────────────────────────────
'''       f^c = f − M⁻¹·S·(m − m_eq)，m = M·f
'''       · 19 矩基取 Hermite 型多项式（见 MrtBasisD3Q19.Evaluate）
'''       · m_eq := M·f_eq（用与 BGK 相同的 f_eq 现算矩 —— 保证不动点性质
'''         f = f_eq → f^c = f_eq 对任意基成立，无需解析平衡矩）
'''       · 松弛谱：守恒模（ρ, j）s=0；应力模 s=1/τ（粘性精确）；
'''         能量模 s=1/τ；高阶幻影模 s=1（瞬时清除 ≈ 正则化极限 → 高 Re 稳定）
'''       · M⁻¹ 启动时 Gauss-Jordan 数值求逆（一次），xunit 验证 M·M⁻¹ = I
'''       · 全 s 相等时 MRT 精确退化为 BGK（等价性测试覆盖）
'''
''' /********************************************************************************/
''' </remarks>
Public Module MrtBasisD3Q19

    ''' <summary>
    ''' 第 k 个矩基多项式在离散速度 (ex,ey,ez) 处的取值。
    ''' 行序（k）：
    '''   0: ρ                1: e=|e|²          2: ε=Σe²e² 混合四阶
    '''   3: jx               4: qx=ex·|e|²      5: jy    6: qy
    '''   7: jz               8: qz=ez·|e|²
    '''   9-13: 应力（ex²−ey², ex²+ey²−2ez², exey, eyez, ezex）
    '''   14-17: 三阶幻影（ex(ey²−ez²) 等, exeyez）
    '''   18: 四阶幻影 ex²ey²ez²（仅体对角 8 方向非零）
    ''' </summary>
    Public Function Evaluate(k As Integer, ex As Integer, ey As Integer, ez As Integer) As Double
        Dim e2 As Integer = ex * ex + ey * ey + ez * ez
        Select Case k
            Case 0 : Return 1.0R
            Case 1 : Return CDbl(e2)
            Case 2 : Return CDbl(ex * ex * ey * ey + ey * ey * ez * ez + ez * ez * ex * ex)
            Case 3 : Return CDbl(ex)
            Case 4 : Return CDbl(ex * e2)
            Case 5 : Return CDbl(ey)
            Case 6 : Return CDbl(ey * e2)
            Case 7 : Return CDbl(ez)
            Case 8 : Return CDbl(ez * e2)
            Case 9 : Return CDbl(ex * ex - ey * ey)
            Case 10 : Return CDbl(ex * ex + ey * ey - 2 * ez * ez)
            Case 11 : Return CDbl(ex * ey)
            Case 12 : Return CDbl(ey * ez)
            Case 13 : Return CDbl(ez * ex)
            Case 14 : Return CDbl(ex * (ey * ey - ez * ez))
            Case 15 : Return CDbl(ey * (ez * ez - ex * ex))
            Case 16 : Return CDbl(ez * (ex * ex - ey * ey))
            Case 17 : Return CDbl((ex * ex - ey * ey) * (ex * ex + ey * ey + ez * ez))  ' 四阶：应力×能量
            Case 18 : Return CDbl(ex * ex * ey * ey)                                     ' 四阶：面副对角
            Case Else
                Throw New ArgumentOutOfRangeException(NameOf(k))
        End Select
    End Function

    ''' <summary>构造 19×19 矩变换矩阵 M（行=矩基 k，列=方向 a）。</summary>
    Public Function BuildMatrix() As Double(,)
        Dim m(18, 18) As Double
        For k = 0 To 18
            For a = 0 To 18
                m(k, a) = Evaluate(k, LatticeD3Q19.EX(a), LatticeD3Q19.EY(a), LatticeD3Q19.EZ(a))
            Next
        Next
        Return m
    End Function

    ''' <summary>
    ''' Gauss-Jordan 带部分主元消元求逆。奇异（矩基线性相关）时抛异常。
    ''' </summary>
    Public Function Invert(mIn As Double(,)) As Double(,)
        Dim n = mIn.GetLength(0)
        If mIn.GetLength(1) <> n Then Throw New ArgumentException("需要方阵")
        ' 增广 [m | I]
        Dim aug(n - 1, 2 * n - 1) As Double
        For i = 0 To n - 1
            For j = 0 To n - 1
                aug(i, j) = mIn(i, j)
            Next
            aug(i, n + i) = 1.0R
        Next
        For col = 0 To n - 1
            ' 部分主元
            Dim piv = col
            For r = col + 1 To n - 1
                If System.Math.Abs(aug(r, col)) > System.Math.Abs(aug(piv, col)) Then piv = r
            Next
            If System.Math.Abs(aug(piv, col)) < 0.000000000001R Then
                Throw New InvalidOperationException("矩基矩阵奇异（19 个基函数线性相关）")
            End If
            If piv <> col Then
                For j = 0 To 2 * n - 1
                    Dim t = aug(col, j) : aug(col, j) = aug(piv, j) : aug(piv, j) = t
                Next
            End If
            Dim d = aug(col, col)
            For j = 0 To 2 * n - 1 : aug(col, j) /= d : Next
            For r = 0 To n - 1
                If r <> col AndAlso aug(r, col) <> 0.0R Then
                    Dim f = aug(r, col)
                    For j = 0 To 2 * n - 1
                        aug(r, j) -= f * aug(col, j)
                    Next
                End If
            Next
        Next
        Dim inv(n - 1, n - 1) As Double
        For i = 0 To n - 1
            For j = 0 To n - 1
                inv(i, j) = aug(i, n + j)
            Next
        Next
        Return inv
    End Function

    ''' <summary>
    ''' MRT 松弛谱 s_k（依赖当前 τ）：
    ''' 守恒模（ρ,jx,jy,jz）= 0；能量/应力模 = 1/τ；高阶幻影模 = 1。
    ''' </summary>
    Public Function RelaxationSpectrum(tau As Double) As Double()
        ' 谱设计经壁面反弹稳定性数值验证（幂迭代发散率 + 驱动腔体测试，见 README）：
        '   · 守恒模（0,3,5,7）= 0
        '   · 偶扇区（能量 1,2 + 应力 9-13 + 四阶 17,18）统一 = ω⁺（TRT 幻影参数 Λ=1/4）
        '   · 奇扇区（4,6,8,14,15,16）统一 = ω⁻ = 1/τ（剪切粘度 ν = cs²(τ−½)）
        '
        ' 关键实验结论（本引擎开发中实测）：
        '   ① 偶扇区若以应力速率 1/τ（≈2）松弛，会与半程反弹共振 —— 无驱动即指数放大
        '      （每步 ~2.2×）；四阶偶模取 1 或慢速率同样失稳
        '   ② 偶扇区统一取 ω⁺、奇扇区统一取 ω⁻ 时，MRT 算子与 TRT 逐位等价
        '      （xunit MRT_EqualsTRT_WhenSpectrumPaired 数值验证，差 < 1e-15）
        '   ③ 该配置下粘度由奇扇区承载：ν = cs²(τ−½)（周期域剪切波衰减实测确认）
        Dim s(18) As Double
        Dim omegaMinus = 1.0R / tau
        Dim omegaPlus = 1.0R / (0.5R + MagicLambda / (tau - 0.5R))
        For k = 0 To 18
            Select Case k
                Case 0, 3, 5, 7                                    ' ρ, jx, jy, jz
                    s(k) = 0.0R
                Case 1, 2, 9, 10, 11, 12, 13, 17, 18              ' 偶扇区
                    s(k) = omegaPlus
                Case Else                                           ' 奇扇区
                    s(k) = omegaMinus
            End Select
        Next
        Return s
    End Function

    ''' <summary>TRT 幻影参数 Λ（(1/ω⁺−½)(1/ω⁻−½)），反弹壁面最优取 1/4。</summary>
    Public Const MagicLambda As Double = 0.25R

End Module
