' /********************************************************************************/
'
'   MeshGNNTrainer.vb
'
'   MeshGNN 节点级回归训练器 —— 手写 MSE 训练循环
'
'   说明：
'       sciBASIC# GNN 库内置的 Trainer 只支持图分类（交叉熵 + Integer 类别
'       标签），不支持节点级回归。本训练器手动实现回归训练循环：
'           前向 → MSE 损失 → 损失梯度 [N, 3] → 逐层反传 → AdamOptimizer.Step()
'       并提供 train/val 划分、epoch 日志、最优验证权重快照回滚与
'       CUDA GPU 后端注册（失败静默回退 SIMD CPU）。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.Computing.ILCuda
Imports Microsoft.VisualBasic.DeepLearning.GNN
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports std = System.Math

''' <summary>
''' 单个 GNN 训练样本：节点特征 + 节点标签（单精度数组存储，
''' 训练时物化为 Double 张量，用完即释放，控制内存占用）。
''' </summary>
Public Class GnnSample

    ''' <summary>节点特征（长度 N * featDim）。</summary>
    Public ReadOnly Property Features As Single()

    ''' <summary>节点标签（长度 N * 3：u/U∞, v/U∞, w/U∞）。</summary>
    Public ReadOnly Property Labels As Single()

    ''' <summary>样本来源描述（调试 / 日志用）。</summary>
    Public Property Tag As String

    Public Sub New(features As Single(), labels As Single(), Optional tag As String = Nothing)
        Me.Features = features
        Me.Labels = labels
        Me.Tag = tag
    End Sub

End Class

''' <summary>
''' MeshGNN 回归训练器：MSE 损失 + Adam 优化器。
''' </summary>
Public Class MeshGNNTrainer

    Private ReadOnly _model As MeshGNN
    Private ReadOnly _optimizer As AdamOptimizer

    ''' <summary>训练损失历史（每 epoch 平均 MSE）。</summary>
    Public ReadOnly Property TrainLossHistory As New List(Of Single)

    ''' <summary>验证损失历史（每 epoch 平均 MSE）。</summary>
    Public ReadOnly Property ValLossHistory As New List(Of Single)

    Public Property LearningRate As Single
        Get
            Return _optimizer.LearningRate
        End Get
        Set(value As Single)
            _optimizer.LearningRate = value
        End Set
    End Property

    Public Sub New(model As MeshGNN, Optional learningRate As Single = 0.005F)
        _model = model
        _optimizer = New AdamOptimizer(model.GetParameters(), model.GetGradients(), learningRate)
    End Sub

    ''' <summary>
    ''' 单样本单步训练，返回该样本的 MSE。
    ''' </summary>
    Public Function TrainStep(sample As GnnSample, adj As SparseAdjacency, featDim As Integer) As Single
        _optimizer.ZeroGrad()

        Dim features = CfdGraphData.WrapFeatures(sample.Features, featDim)
        Dim labels = CfdGraphData.WrapLabels(sample.Labels)
        Dim pred As Tensor = Nothing
        Dim grad As Tensor = Nothing

        Try
            pred = _model.Forward(features, adj)
            Dim loss = ComputeMse(pred, labels)
            grad = LossGradient(pred, labels)
            _model.Backward(grad, adj)
            _optimizer.Step()
            Return loss
        Finally
            If grad IsNot Nothing Then grad.Dispose()
            If pred IsNot Nothing Then pred.Dispose()
            labels.Dispose()
            features.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' 在样本集上评估平均 MSE（不更新参数）。
    ''' </summary>
    Public Function Evaluate(samples As IList(Of GnnSample), adj As SparseAdjacency, featDim As Integer) As Single
        If samples.Count = 0 Then Return 0.0F
        Dim total As Double = 0.0

        For Each s In samples
            Dim features = CfdGraphData.WrapFeatures(s.Features, featDim)
            Dim labels = CfdGraphData.WrapLabels(s.Labels)
            Dim pred = _model.Forward(features, adj)
            total += ComputeMse(pred, labels)

            pred.Dispose()
            labels.Dispose()
            features.Dispose()
        Next

        Return CSng(total / samples.Count)
    End Function

    ''' <summary>
    ''' 完整训练流程：train/val 划分 + 多 epoch 训练 + 最优验证权重回滚。
    ''' </summary>
    ''' <param name="samples">训练样本集</param>
    ''' <param name="adj">稀疏邻接（样本网格拓扑相同）</param>
    ''' <param name="featDim">节点特征维度</param>
    ''' <param name="epochs">训练轮数</param>
    ''' <param name="valFrac">验证集比例</param>
    ''' <param name="printEvery">每多少个 epoch 打印一次日志</param>
    Public Sub Train(samples As IList(Of GnnSample), adj As SparseAdjacency, featDim As Integer,
                     Optional epochs As Integer = 40,
                     Optional valFrac As Double = 0.15,
                     Optional printEvery As Integer = 5)

        If samples.Count < 4 Then
            Throw New ArgumentException($"训练样本过少（{samples.Count}），至少需要 4 个")
        End If

        ' ---- 划分 train / val ----
        Dim indices As New List(Of Integer)(Enumerable.Range(0, samples.Count))
        Dim rng As New Random(12345)
        Shuffle(indices, rng)

        Dim valCount As Integer = std.Max(1, CInt(samples.Count * valFrac))
        Dim valIdx = indices.Take(valCount).ToArray()
        Dim trainIdx = indices.Skip(valCount).ToArray()

        Console.WriteLine($"[训练] 样本 train={trainIdx.Length}, val={valIdx.Length}, epochs={epochs}, lr={LearningRate}")

        Dim bestVal As Single = Single.PositiveInfinity
        Dim bestParams = CloneParameters(_model.GetParameters())

        TrainLossHistory.Clear()
        ValLossHistory.Clear()

        For epoch As Integer = 1 To epochs
            Shuffle(trainIdx, rng)

            Dim epochLoss As Double = 0.0
            For Each i As Integer In trainIdx
                epochLoss += TrainStep(samples(i), adj, featDim)
            Next
            Dim trainMse = CSng(epochLoss / trainIdx.Length)
            Dim valMse = Evaluate(valIdx.Select(Function(i) samples(i)).ToArray(), adj, featDim)

            TrainLossHistory.Add(trainMse)
            ValLossHistory.Add(valMse)

            ' ---- 最优验证权重快照 ----
            If valMse < bestVal Then
                bestVal = valMse
                SnapshotParameters(bestParams)
            End If

            If printEvery > 0 AndAlso (epoch = 1 OrElse epoch Mod printEvery = 0 OrElse epoch = epochs) Then
                Console.WriteLine($"    epoch {epoch,4}/{epochs}  train MSE = {trainMse:E3}  val MSE = {valMse:E3}")
            End If
        Next

        ' ---- 回滚到最优验证权重 ----
        RestoreParameters(bestParams)
        Console.WriteLine($"[训练] 完成，最优 val MSE = {bestVal:E3}（已回滚至最优权重）")
    End Sub

    ''' <summary>计算 MSE = Σ(pred-target)² / Length。</summary>
    Public Shared Function ComputeMse(pred As Tensor, target As Tensor) As Single
        Dim p = pred.Data, t = target.Data
        Dim sum As Double = 0.0
        For idx As Integer = 0 To p.Length - 1
            Dim d = p(idx) - t(idx)
            sum += d * d
        Next
        Return CSng(sum / p.Length)
    End Function

    ''' <summary>MSE 损失梯度：2/(N·D) · (pred - target)。</summary>
    Public Shared Function LossGradient(pred As Tensor, target As Tensor) As Tensor
        Dim p = pred.Data, t = target.Data
        Dim g(p.Length - 1) As Double
        Dim scale As Double = 2.0 / p.Length
        For idx As Integer = 0 To p.Length - 1
            g(idx) = (p(idx) - t(idx)) * scale
        Next
        Return New Tensor(g, pred.Shape)
    End Function

    ''' <summary>
    ''' 尝试注册 CUDA GPU 计算后端；失败时静默回退 SIMD CPU。
    ''' 注意：GPU 只加速 Double 栈的 Tensor 运算链，TensorF（CFD 场量）无 CUDA 后端。
    ''' </summary>
    Public Shared Function TryEnableGpu() As Boolean
        Try
            If GPUTensor.CudaTensor.Register() Then
                Console.WriteLine("[GPU] CUDA 计算后端注册成功，Tensor 运算将走 GPU 加速")
                Return True
            Else
                Console.WriteLine("[GPU] CUDA 后端不可用，回退 SIMD CPU")
                Return False
            End If
        Catch ex As Exception
            Console.WriteLine($"[GPU] CUDA 后端注册异常（{ex.Message}），回退 SIMD CPU")
            Return False
        End Try
    End Function

    ' ---- 参数快照（最优验证权重回滚）----

    ''' <summary>克隆一份参数张量（深拷贝）。</summary>
    Private Shared Function CloneParameters(parameters As List(Of Tensor)) As List(Of Tensor)
        Dim copies As New List(Of Tensor)
        For Each p In parameters
            Dim c As New Tensor(p.Shape)
            Array.Copy(p.Data, c.Data, p.Length)
            copies.Add(c)
        Next
        Return copies
    End Function

    ''' <summary>把当前模型参数保存到快照。</summary>
    Private Sub SnapshotParameters(snapshot As List(Of Tensor))
        Dim parameters = _model.GetParameters()
        For i As Integer = 0 To parameters.Count - 1
            Array.Copy(parameters(i).Data, snapshot(i).Data, parameters(i).Length)
        Next
    End Sub

    ''' <summary>把快照恢复到模型参数。</summary>
    Private Sub RestoreParameters(snapshot As List(Of Tensor))
        Dim parameters = _model.GetParameters()
        For i As Integer = 0 To parameters.Count - 1
            Array.Copy(snapshot(i).Data, parameters(i).Data, parameters(i).Length)
        Next
    End Sub

    Private Shared Sub Shuffle(list As List(Of Integer), rng As Random)
        For i As Integer = list.Count - 1 To 1 Step -1
            Dim j = rng.Next(i + 1)
            Dim tmp = list(i)
            list(i) = list(j)
            list(j) = tmp
        Next
    End Sub

End Class
