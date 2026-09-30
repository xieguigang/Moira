' /********************************************************************************/
'
'   MeshGNN.vb
'
'   面向 CFD 体素网格的节点级回归 GNN 模型（Mesh Graph Neural Network）
'
'   架构：
'       输入特征 [N, featDim]
'         → SparseGcnLayer(featDim → hidden, ReLU)
'         → SparseGcnLayer(hidden → hidden, ReLU) × (numLayers - 2)
'         → SparseGcnLayer(hidden → outputDim, None)     ' 回归输出，无激活
'       输出 [N, outputDim]（u/U∞、v/U∞、w/U∞ 三通道归一化速度）
'
'   实现说明：
'       sciBASIC# GNN 库的 GCNConvLayer.Forward(input, graph) 内部构造
'       O(N²) 稠密归一化邻接矩阵做 MatMul，数万节点的体素网格内存与
'       时间开销不可接受；且库内 LinearLayer 的权重初始化形状与 Forward/
'       Backward 假设不一致、Backward 每次替换梯度张量对象导致 AdamOptimizer
'       持有的梯度引用失效。因此本层自实现：
'         1. 基于边表（CSR 稀疏邻接）的 O(N+E) 消息传递；
'         2. 权重形状 [in, out]，Forward: A_hat(XW)，Backward 手动反传；
'         3. 梯度张量在构造时一次性分配、Backward 内原地覆写，
'            保证 AdamOptimizer 持有的引用始终有效。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.DeepLearning.GNN
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports std = System.Math

''' <summary>
''' 稀疏 GCN 卷积层：Y = σ( A_hat · (X · W) + b )，
''' A_hat 为 CSR 稀疏对称归一化邻接（含自环），消息传递复杂度 O(N + E)。
''' </summary>
Public Class SparseGcnLayer

    Public ReadOnly Property InFeatures As Integer
    Public ReadOnly Property OutFeatures As Integer

    Private ReadOnly _activation As ActivationType

    ''' <summary>权重 [in, out]。</summary>
    Private ReadOnly _weights As Tensor

    ''' <summary>偏置 [1, out]。</summary>
    Private ReadOnly _bias As Tensor

    ''' <summary>权重梯度 [in, out]（构造时分配，Backward 原地覆写，引用恒定）。</summary>
    Private ReadOnly _weightGrad As Tensor

    ''' <summary>偏置梯度 [1, out]（同上）。</summary>
    Private ReadOnly _biasGrad As Tensor

    ' ---- Forward 缓存（供 Backward 使用）----
    Private _lastInput As Tensor      ' [N, in]
    Private _preAct As Tensor         ' [N, out] 激活前的聚合结果 A_hat(XW) + b

    Public Sub New(inFeatures As Integer, outFeatures As Integer,
                   Optional activation As ActivationType = ActivationType.ReLU,
                   Optional seed As Integer? = Nothing)

        Me.InFeatures = inFeatures
        Me.OutFeatures = outFeatures
        Me._activation = activation

        _weights = Tensor.XavierInit(inFeatures, outFeatures, seed)
        _bias = New Tensor(1, outFeatures)                ' 全 0
        _weightGrad = New Tensor(inFeatures, outFeatures) ' 全 0
        _biasGrad = New Tensor(1, outFeatures)            ' 全 0
    End Sub

    ''' <summary>
    ''' 前向传播：返回 [N, outFeatures]。
    ''' </summary>
    Public Function Forward(input As Tensor, adj As SparseAdjacency) As Tensor
        Dim n As Integer = input.Shape(0)
        Dim dIn As Integer = InFeatures
        Dim dOut As Integer = OutFeatures

        If input.Shape(1) <> dIn Then
            Throw New ArgumentException($"输入特征维度 {input.Shape(1)} 与层定义 {dIn} 不匹配")
        End If

        ' 缓存输入供 Backward 计算 dW 使用
        _lastInput = input

        ' ---- 1. 线性变换 Z = X · W ----
        Dim z = input.MatMul(_weights)          ' [N, out]
        Dim zArr = z.Data

        ' ---- 2. 稀疏消息传递：out = A_hat · Z ----
        Dim outData(n * dOut - 1) As Double
        Dim rs = adj.RowStart, ci = adj.ColIdx, vv = adj.Val

        For i As Integer = 0 To n - 1
            Dim ob As Integer = i * dOut
            For p As Integer = rs(i) To rs(i + 1) - 1
                Dim jb As Integer = ci(p) * dOut
                Dim w As Double = vv(p)
                For d As Integer = 0 To dOut - 1
                    outData(ob + d) += w * zArr(jb + d)
                Next
            Next
        Next

        ' ---- 3. 加偏置，保存激活前值 ----
        Dim bArr = _bias.Data
        For idx As Integer = 0 To outData.Length - 1
            outData(idx) += bArr(idx Mod dOut)
        Next

        _preAct = New Tensor(outData, n, dOut)

        ' ---- 4. 激活 ----
        If _activation = ActivationType.None Then
            Return New Tensor(outData, n, dOut)
        Else
            Dim actData = CType(outData.Clone(), Double())
            ApplyActivation(actData)
            Return New Tensor(actData, n, dOut)
        End If
    End Function

    ''' <summary>
    ''' 反向传播：gradient 为损失对本层输出的梯度 [N, outFeatures]，
    ''' 返回对输入的梯度 [N, inFeatures]。权重/偏置梯度原地覆写到内部张量。
    ''' </summary>
    Public Function Backward(gradient As Tensor, adj As SparseAdjacency) As Tensor
        Dim n As Integer = gradient.Shape(0)
        Dim dIn As Integer = InFeatures
        Dim dOut As Integer = OutFeatures
        Dim gArr = gradient.Data
        Dim preArr = _preAct.Data

        ' ---- 1. 激活导数：dAct = gradient ⊙ σ'(preAct) ----
        Dim dAct(n * dOut - 1) As Double
        Select Case _activation
            Case ActivationType.None
                Array.Copy(gArr, dAct, gArr.Length)
            Case ActivationType.ReLU
                For idx As Integer = 0 To gArr.Length - 1
                    If preArr(idx) > 0 Then dAct(idx) = gArr(idx)
                Next
            Case Else
                Throw New NotSupportedException($"SparseGcnLayer 暂不支持激活 {_activation} 的反向传播")
        End Select

        ' ---- 2. 反向聚合：dZ = A_hat^T · dAct = A_hat · dAct（A_hat 对称）----
        Dim dz(n * dOut - 1) As Double
        Dim rs = adj.RowStart, ci = adj.ColIdx, vv = adj.Val

        For i As Integer = 0 To n - 1
            Dim ob As Integer = i * dOut
            For p As Integer = rs(i) To rs(i + 1) - 1
                Dim jb As Integer = ci(p) * dOut
                Dim w As Double = vv(p)
                For d As Integer = 0 To dOut - 1
                    dz(ob + d) += w * dAct(jb + d)
                Next
            Next
        Next

        ' ---- 3. 权重梯度：dW = X^T · dZ（原地覆写）----
        Dim wGrad = _weightGrad.Data
        Array.Clear(wGrad, 0, wGrad.Length)
        Dim xArr = _lastInput.Data

        For i As Integer = 0 To n - 1
            Dim xb As Integer = i * dIn
            Dim zb As Integer = i * dOut
            For a As Integer = 0 To dIn - 1
                Dim xv As Double = xArr(xb + a)
                If xv <> 0.0 Then
                    Dim wb As Integer = a * dOut
                    For b As Integer = 0 To dOut - 1
                        wGrad(wb + b) += xv * dz(zb + b)
                    Next
                End If
            Next
        Next

        ' ---- 4. 偏置梯度：db = Σ_i dZ(i)（原地覆写）----
        Dim bGrad = _biasGrad.Data
        Array.Clear(bGrad, 0, bGrad.Length)
        For idx As Integer = 0 To n * dOut - 1
            bGrad(idx Mod dOut) += dz(idx)
        Next

        ' ---- 5. 输入梯度：dx = dZ · W^T ----
        Dim wArr = _weights.Data
        Dim dx(n * dIn - 1) As Double

        For i As Integer = 0 To n - 1
            Dim zb As Integer = i * dOut
            Dim xb As Integer = i * dIn
            For a As Integer = 0 To dIn - 1
                Dim wb As Integer = a * dOut
                Dim s As Double = 0.0
                For b As Integer = 0 To dOut - 1
                    s += dz(zb + b) * wArr(wb + b)
                Next
                dx(xb + a) = s
            Next
        Next

        Return New Tensor(dx, n, dIn)
    End Function

    Private Sub ApplyActivation(data As Double())
        Select Case _activation
            Case ActivationType.ReLU
                For idx As Integer = 0 To data.Length - 1
                    If data(idx) < 0 Then data(idx) = 0.0
                Next
            Case ActivationType.None
                ' 无操作
            Case Else
                Throw New NotSupportedException($"SparseGcnLayer 暂不支持激活 {_activation}")
        End Select
    End Sub

    ''' <summary>参数列表：[W, b]。</summary>
    Public Function GetParameters() As List(Of Tensor)
        Return New List(Of Tensor) From {_weights, _bias}
    End Function

    ''' <summary>梯度列表：[dW, db]（引用恒定，原地更新）。</summary>
    Public Function GetGradients() As List(Of Tensor)
        Return New List(Of Tensor) From {_weightGrad, _biasGrad}
    End Function

    ''' <summary>读取权重（外部检查用）。</summary>
    Public Function GetWeights() As Tensor
        Return _weights
    End Function

    ''' <summary>读取偏置（外部检查用）。</summary>
    Public Function GetBias() As Tensor
        Return _bias
    End Function

    ''' <summary>写入权重（模型反序列化用）。</summary>
    Public Sub SetWeights(weights As Double(), bias As Double())
        Array.Copy(weights, _weights.Data, _weights.Length)
        Array.Copy(bias, _bias.Data, _bias.Length)
    End Sub

End Class

''' <summary>
''' CFD 体素网格节点级回归 GNN 模型（继承 sciBASIC# GNN 库的 GNNModel 基类）。
''' 支持两种代理模式（由输入特征维度区分）：
'''     模式A：直接预测稳态流场（featDim = CfdGraphData.ModeAFeatDim）
'''     模式B：自回归单步时间演化（featDim = CfdGraphData.ModeBFeatDim）
''' 输出统一为 [N, 3] 的归一化速度（u/U∞, v/U∞, w/U∞）。
''' </summary>
Public Class MeshGNN : Inherits GNNModel

    Private ReadOnly _gcnLayers As New List(Of SparseGcnLayer)

    Public ReadOnly Property InputDim As Integer
    Public ReadOnly Property HiddenDim As Integer
    Public ReadOnly Property OutputDim As Integer

    Public ReadOnly Property LayerCount As Integer
        Get
            Return _gcnLayers.Count
        End Get
    End Property

    ''' <summary>
    ''' 构建 GCN 堆叠回归网络。
    ''' </summary>
    ''' <param name="inputDim">输入节点特征维度</param>
    ''' <param name="hiddenDim">隐藏层维度</param>
    ''' <param name="outputDim">输出维度（CFD 速度代理为 3）</param>
    ''' <param name="numLayers">GCN 层数（>= 2；首层起 ReLU、末层无激活）</param>
    Public Sub New(inputDim As Integer, hiddenDim As Integer, outputDim As Integer,
                   Optional numLayers As Integer = 3)

        Me.InputDim = inputDim
        Me.HiddenDim = hiddenDim
        Me.OutputDim = outputDim

        If numLayers < 2 Then numLayers = 2

        For l As Integer = 0 To numLayers - 1
            Dim dIn As Integer = If(l = 0, inputDim, hiddenDim)
            Dim dOut As Integer = If(l = numLayers - 1, outputDim, hiddenDim)
            Dim act As ActivationType = If(l = numLayers - 1, ActivationType.None, ActivationType.ReLU)
            _gcnLayers.Add(New SparseGcnLayer(dIn, dOut, act))
        Next
    End Sub

    ''' <summary>
    ''' 前向传播（稀疏邻接快速路径）。
    ''' </summary>
    Public Overloads Function Forward(features As Tensor, adj As SparseAdjacency) As Tensor
        Dim h As Tensor = features
        For Each layer In _gcnLayers
            h = layer.Forward(h, adj)
        Next
        Return h
    End Function

    ''' <summary>
    ''' 反向传播（稀疏邻接快速路径）：gradient 为 [N, outputDim] 损失梯度。
    ''' </summary>
    Public Overloads Function Backward(gradient As Tensor, adj As SparseAdjacency) As Tensor
        Dim g As Tensor = gradient
        For l As Integer = _gcnLayers.Count - 1 To 0 Step -1
            g = _gcnLayers(l).Backward(g, adj)
        Next
        Return g
    End Function

    ' ---- GNNModel 基类兼容接口 ----

    Public Overrides Function Forward(nodeFeatures As Tensor, graph As Graph) As Tensor
        Return Forward(nodeFeatures, GetGraphAdjacency(graph))
    End Function

    Public Overrides Function Backward(gradient As Tensor, graph As Graph) As Tensor
        Return Backward(gradient, GetGraphAdjacency(graph))
    End Function

    Public Overrides Function GetParameters() As List(Of Tensor)
        Dim ps As New List(Of Tensor)
        For Each layer In _gcnLayers
            ps.AddRange(layer.GetParameters())
        Next
        Return ps
    End Function

    Public Overrides Function GetGradients() As List(Of Tensor)
        Dim gs As New List(Of Tensor)
        For Each layer In _gcnLayers
            gs.AddRange(layer.GetGradients())
        Next
        Return gs
    End Function

    Public Overrides Sub PrintModelInfo()
        MyBase.PrintModelInfo()
        Console.WriteLine($"  MeshGNN: input={InputDim}, hidden={HiddenDim}, output={OutputDim}, layers={LayerCount}")
        Console.WriteLine($"  参数总量: {GetParameters().Sum(Function(t) t.Length)}")
    End Sub

    ' ---- Graph → 稀疏邻接缓存 ----

    Private Shared ReadOnly _graphAdjCache As New Dictionary(Of String, SparseAdjacency)

    Private Shared Function GetGraphAdjacency(g As Graph) As SparseAdjacency
        Dim key = $"{g.NumNodes}:{g.NumEdges}"
        SyncLock _graphAdjCache
            Dim adj As SparseAdjacency = Nothing
            If Not _graphAdjCache.TryGetValue(key, adj) Then
                adj = SparseAdjacency.BuildFromGraph(g)
                _graphAdjCache(key) = adj
            End If
            Return adj
        End SyncLock
    End Function

    ' ---- 参数持久化（JSON 检查点）----

    ''' <summary>保存模型参数到 JSON 文件。</summary>
    Public Sub Save(filePath As String)
        Dim dto As New Checkpoint With {
            .InputDim = InputDim,
            .HiddenDim = HiddenDim,
            .OutputDim = OutputDim,
            .NumLayers = LayerCount,
            .Layers = _gcnLayers.Select(
                Function(l) New LayerWeights With {
                    .Weights = l.GetWeights().Data,
                    .Bias = l.GetBias().Data}).ToArray()
        }

        Dim options As New System.Text.Json.JsonSerializerOptions With {.WriteIndented = True}
        System.IO.File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(dto, options))
    End Sub

    ''' <summary>
    ''' 从 JSON 检查点恢复模型。
    ''' </summary>
    Public Shared Function Load(filePath As String) As MeshGNN
        Dim dto = System.Text.Json.JsonSerializer.Deserialize(Of Checkpoint)(
            System.IO.File.ReadAllText(filePath))

        If dto Is Nothing Then
            Throw New InvalidOperationException($"检查点文件无效：{filePath}")
        End If

        Dim model As New MeshGNN(dto.InputDim, dto.HiddenDim, dto.OutputDim, dto.NumLayers)

        If dto.Layers Is Nothing OrElse dto.Layers.Length <> model.LayerCount Then
            Throw New InvalidOperationException(
                $"检查点层数 ({If(dto.Layers Is Nothing, 0, dto.Layers.Length)}) 与模型定义 ({model.LayerCount}) 不一致")
        End If

        For l As Integer = 0 To model.LayerCount - 1
            model._gcnLayers(l).SetWeights(dto.Layers(l).Weights, dto.Layers(l).Bias)
        Next

        Return model
    End Function

    ''' <summary>JSON 检查点结构。</summary>
    Public Class Checkpoint
        Public Property InputDim As Integer
        Public Property HiddenDim As Integer
        Public Property OutputDim As Integer
        Public Property NumLayers As Integer
        Public Property Layers As LayerWeights()
    End Class

    ''' <summary>单层权重结构。</summary>
    Public Class LayerWeights
        Public Property Weights As Double()
        Public Property Bias As Double()
    End Class

End Class
