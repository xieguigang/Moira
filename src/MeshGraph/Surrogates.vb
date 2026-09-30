' /********************************************************************************/
'
'   Surrogates.vb
'
'   CFD 图神经网络代理模型 —— 两种代理模式的统一封装
'
'   模式A  SteadyStateSurrogate：
'       输入几何（VoxelShape）+ 来流条件，一次前向传播直接输出稳态流场。
'       推理最快，适合自动优化循环中的快速评估。
'
'   模式B  AutoregressiveSurrogate：
'       以初始均匀来流为起点，反复用"单步时间演化" GNN 模型 rollout
'       得到流场演化过程，最终场作为稳态估计。更贴近 CFD 动力学，
'       但误差随步数累积（已通过固体掩膜硬约束与有限 rollout 步数缓解）。
'
'   两种代理都实现 ICfdSurrogate 统一接口：
'       Predict(shape, freestream) → FluidField
'   供自动优化框架程序化调用，替代慢速 Stable Fluids 求解器。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine
Imports std = System.Math

''' <summary>
''' CFD 代理模型统一推理契约：输入体素几何 + 来流条件 → 输出流场。
''' </summary>
Public Interface ICfdSurrogate

    ''' <summary>
    ''' 预测给定几何在指定来流速度下的稳态流场。
    ''' </summary>
    ''' <param name="shape">计算域体素模型（True = 流体，False = 固体）</param>
    ''' <param name="freestream">来流速度 U∞（沿 +X）</param>
    Function Predict(shape As VoxelShape, freestream As Double) As FluidField

    ''' <summary>保存模型参数。</summary>
    Sub Save(filePath As String)

End Interface

''' <summary>代理模型工厂：按模式与检查点文件重建代理实例。</summary>
Public Module CfdSurrogate

    Public Enum SurrogateMode
        ''' <summary>模式A：直接预测稳态流场。</summary>
        SteadyState
        ''' <summary>模式B：自回归单步预测（rollout）。</summary>
        Autoregressive
    End Enum

    ''' <summary>从 JSON 模型检查点加载代理模型。</summary>
    Public Function Load(mode As SurrogateMode, filePath As String) As ICfdSurrogate
        Dim model = MeshGNN.Load(filePath)
        Select Case mode
            Case SurrogateMode.SteadyState
                Return New SteadyStateSurrogate(model)
            Case SurrogateMode.Autoregressive
                Return New AutoregressiveSurrogate(model)
            Case Else
                Throw New ArgumentException($"未知的代理模式 {mode}")
        End Select
    End Function

End Module

''' <summary>
''' 模式A代理：几何 + 来流 → 一次前向 → 稳态流场。
''' </summary>
Public Class SteadyStateSurrogate : Implements ICfdSurrogate

    Public ReadOnly Property Model As MeshGNN

    Public Sub New(model As MeshGNN)
        If model.InputDim <> CfdGraphData.ModeAFeatDim Then
            Throw New ArgumentException(
                $"模式A要求模型输入维度为 {CfdGraphData.ModeAFeatDim}，当前 {model.InputDim}")
        End If
        Me.Model = model
    End Sub

    Public Function Predict(shape As VoxelShape, freestream As Double) As FluidField Implements ICfdSurrogate.Predict
        Dim adj = CfdGraphData.GetGridAdjacency(shape)
        Dim feats = CfdGraphData.BuildModeAFeatures(shape, freestream)
        Dim features = CfdGraphData.WrapFeatures(feats, CfdGraphData.ModeAFeatDim)

        Dim output = Model.Forward(features, adj)
        Dim field = CfdGraphData.ToFluidField(output, shape, freestream)

        output.Dispose()
        features.Dispose()
        Return field
    End Function

    Public Sub Save(filePath As String) Implements ICfdSurrogate.Save
        Model.Save(filePath)
    End Sub

End Class

''' <summary>
''' 模式B代理：以均匀来流为初始场，用单步演化模型迭代 rollout 得到稳态估计。
''' </summary>
Public Class AutoregressiveSurrogate : Implements ICfdSurrogate

    Public ReadOnly Property Model As MeshGNN

    ''' <summary>rollout 步数（每步对应一个 CFD 时间步）。</summary>
    Public Property RolloutSteps As Integer = 40

    ''' <summary>rollout 时间步长（须与训练数据采集时的 dt 一致）。</summary>
    Public Property TimeStep As Double = 0.1

    Public Sub New(model As MeshGNN)
        If model.InputDim <> CfdGraphData.ModeBFeatDim Then
            Throw New ArgumentException(
                $"模式B要求模型输入维度为 {CfdGraphData.ModeBFeatDim}，当前 {model.InputDim}")
        End If
        Me.Model = model
    End Sub

    ''' <summary>
    ''' 从给定初始流场开始 rollout（供分步观察演化过程 / 外部驱动）。
    ''' </summary>
    Public Function Rollout(shape As VoxelShape, freestream As Double,
                            Optional progressCallback As Action(Of Integer, FluidField) = Nothing) As FluidField
        Dim adj = CfdGraphData.GetGridAdjacency(shape)
        Dim field As FluidField = CreateUniformFlow(shape, freestream)

        For s As Integer = 1 To RolloutSteps
            Dim feats = CfdGraphData.BuildModeBFeatures(field, freestream)
            Dim features = CfdGraphData.WrapFeatures(feats, CfdGraphData.ModeBFeatDim)
            Dim output = Model.Forward(features, adj)
            Dim nextField = CfdGraphData.ToFluidField(output, shape, freestream)

            output.Dispose()
            features.Dispose()
            field = nextField

            If progressCallback IsNot Nothing Then
                progressCallback(s, field)
            End If
        Next

        Return field
    End Function

    Public Function Predict(shape As VoxelShape, freestream As Double) As FluidField Implements ICfdSurrogate.Predict
        Return Rollout(shape, freestream)
    End Function

    Public Sub Save(filePath As String) Implements ICfdSurrogate.Save
        Model.Save(filePath)
    End Sub

    ''' <summary>
    ''' 创建均匀来流初始场：流体体素 U = U∞、V = W = 0，固体体素全 0。
    ''' </summary>
    Public Shared Function CreateUniformFlow(shape As VoxelShape, freestream As Double) As FluidField
        Dim f As New FluidField(shape)
        Dim n As Integer = f.TotalVoxels
        For t As Integer = 0 To n - 1
            If shape.IsActive(t) Then
                f.U.Data(t) = CSng(freestream)
            End If
        Next
        Return f
    End Function

End Class
