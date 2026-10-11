
''' <summary>碰撞策略。</summary>
Public Enum CollisionModel
    ''' <summary>单松弛 BGK（基准，低 Re）。</summary>
    BGK = 0
    ''' <summary>两松弛 TRT（幻影参数 Λ=1/4，壁面流稳定）。</summary>
    TRT = 1
    ''' <summary>多松弛 MRT（19 矩，高阶模 s=1，高 Re 稳定）。</summary>
    MRT = 2
End Enum
