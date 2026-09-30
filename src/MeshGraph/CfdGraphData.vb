' /********************************************************************************/
'
'   CfdGraphData.vb
'
'   CFD 体素场 ↔ 图数据 转换层 —— GNN 代理模型的数据接口
'
'   图构建约定：
'       - 每个体素（含固体）作为图节点，节点编号与 VoxelShape.Index 一致：
'             node(i,j,k) = (i*Ny + j)*Nz + k
'       - 6 邻域（±x / ±y / ±z）建无向边（每条无向边 = 两条有向边）；
'       - 消息传递使用 CSR 稀疏归一化邻接（含自环）：
'             A_hat = D^-1/2 (A + I) D^-1/2
'         同一尺寸的计算域拓扑完全相同，故按域尺寸缓存，训练/推理零重建开销。
'         （sciBASIC# 的 GCNConvLayer 内部走 O(N²) 稠密矩阵，大网格不可行，
'           因此本项目自实现稀疏消息传递，见 MeshGNN.vb 的 SparseGcnLayer。）
'
'   节点特征归一化：
'       模式A（稳态直接预测，5 维）：固体掩膜、x/(Nx-1)、y/(Ny-1)、z/(Nz-1)、U∞/U_REF
'       模式B（自回归单步预测，7 维）：掩膜、三向归一化坐标、u/U∞、v/U∞、w/U∞
'       标签：u/U∞、v/U∞、w/U∞（[N,3]），固体体素恒为 0（与 CFD 引擎的
'       EnforceSolidMask 行为一致，模型可借掩膜特征学习壁面边界）。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.DeepLearning.GNN
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine
Imports std = System.Math

''' <summary>
''' CSR 格式的稀疏对称归一化邻接矩阵 A_hat = D^-1/2 (A + I) D^-1/2（含自环）。
''' 对无向图 A_hat 对称，Forward 与 Backward 聚合可复用同一份结构。
''' </summary>
Public Class SparseAdjacency

    ''' <summary>节点数 N。</summary>
    Public ReadOnly Property NumNodes As Integer

    ''' <summary>行指针（长度 N+1）。</summary>
    Public ReadOnly Property RowStart As Integer()

    ''' <summary>列索引（长度 nnz）。</summary>
    Public ReadOnly Property ColIdx As Integer()

    ''' <summary>归一化权重（长度 nnz）。</summary>
    Public ReadOnly Property Val As Single()

    Public Sub New(numNodes As Integer, rowStart As Integer(), colIdx As Integer(), val As Single())
        Me.NumNodes = numNodes
        Me.RowStart = rowStart
        Me.ColIdx = colIdx
        Me.Val = val
    End Sub

    ''' <summary>
    ''' 从三维网格构建 6 邻域稀疏邻接（体素编号 = (i*Ny + j)*Nz + k）。
    ''' </summary>
    Public Shared Function BuildFromGrid(nx As Integer, ny As Integer, nz As Integer) As SparseAdjacency
        Dim n As Integer = nx * ny * nz

        ' 第一遍：统计每个节点的邻居数（含自环）
        Dim rowStart(n) As Integer
        Dim degree(n - 1) As Integer

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim d As Integer = 1   ' 自环
                    If i > 0 Then d += 1
                    If i < nx - 1 Then d += 1
                    If j > 0 Then d += 1
                    If j < ny - 1 Then d += 1
                    If k > 0 Then d += 1
                    If k < nz - 1 Then d += 1
                    degree((i * ny + j) * nz + k) = d
                Next
            Next
        Next

        Dim total As Integer = 0
        For t As Integer = 0 To n - 1
            rowStart(t) = total
            total += degree(t)
        Next
        rowStart(n) = total

        ' 第二遍：填充列索引与归一化权重 w = 1 / sqrt(d_hat_u * d_hat_v)
        Dim colIdx(total - 1) As Integer
        Dim val(total - 1) As Single
        Dim invDeg(n - 1) As Single
        For t As Integer = 0 To n - 1
            invDeg(t) = CSng(1.0 / std.Sqrt(degree(t)))
        Next

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim u As Integer = (i * ny + j) * nz + k
                    Dim p As Integer = rowStart(u)

                    ' 自环
                    colIdx(p) = u
                    val(p) = invDeg(u) * invDeg(u)
                    p += 1

                    ' 6 邻域：±k, ±j, ±i
                    If k > 0 Then AddNeighbor(colIdx, val, p, u, u - 1, invDeg)
                    If k < nz - 1 Then AddNeighbor(colIdx, val, p, u, u + 1, invDeg)
                    If j > 0 Then AddNeighbor(colIdx, val, p, u, u - nz, invDeg)
                    If j < ny - 1 Then AddNeighbor(colIdx, val, p, u, u + nz, invDeg)
                    If i > 0 Then AddNeighbor(colIdx, val, p, u, u - ny * nz, invDeg)
                    If i < nx - 1 Then AddNeighbor(colIdx, val, p, u, u + ny * nz, invDeg)
                Next
            Next
        Next

        Return New SparseAdjacency(n, rowStart, colIdx, val)
    End Function

    Private Shared Sub AddNeighbor(colIdx As Integer(), val As Single(),
                                   ByRef p As Integer, u As Integer, v As Integer, invDeg As Single())
        colIdx(p) = v
        val(p) = invDeg(u) * invDeg(v)
        p += 1
    End Sub

    ''' <summary>
    ''' 从 sciBASIC# Graph 对象的边表构建稀疏邻接（含自环与对称归一化）。
    ''' 用于兼容库的 GNNModel 接口签名。
    ''' </summary>
    Public Shared Function BuildFromGraph(g As Graph) As SparseAdjacency
        Dim n As Integer = g.NumNodes

        ' 统计度（含自环 1 + 出边数；无向图每条边已在 Edges 中存为两条有向边）
        Dim degree(n - 1) As Integer
        For t As Integer = 0 To n - 1
            degree(t) = 1
        Next
        For Each e In g.Edges
            degree(e.Source) += 1
        Next

        ' CSR 行指针
        Dim rowStart(n) As Integer
        Dim total As Integer = 0
        For t As Integer = 0 To n - 1
            rowStart(t) = total
            total += degree(t)
        Next
        rowStart(n) = total

        Dim colIdx(total - 1) As Integer
        Dim val(total - 1) As Single
        Dim invDeg(n - 1) As Single
        For t As Integer = 0 To n - 1
            invDeg(t) = CSng(1.0 / std.Sqrt(degree(t)))
        Next

        Dim fill = CType(rowStart.Clone(), Integer())

        ' 自环
        For t As Integer = 0 To n - 1
            colIdx(fill(t)) = t
            val(fill(t)) = invDeg(t) * invDeg(t)
            fill(t) += 1
        Next

        ' 出边按 Source 分桶填入对应行
        Dim bySource As New List(Of Edge)(g.Edges.Count)
        bySource.AddRange(g.Edges)
        bySource.Sort(Function(a, b) a.Source.CompareTo(b.Source))

        For Each e In bySource
            Dim u As Integer = e.Source
            colIdx(fill(u)) = e.Target
            val(fill(u)) = invDeg(u) * invDeg(e.Target)
            fill(u) += 1
        Next

        Return New SparseAdjacency(n, rowStart, colIdx, val)
    End Function

End Class

''' <summary>
''' CFD 体素场 ↔ GNN 图数据的双向转换工具。
''' </summary>
Public Class CfdGraphData

    ''' <summary>模式A（稳态直接预测）节点特征维度。</summary>
    Public Const ModeAFeatDim As Integer = 5

    ''' <summary>模式B（自回归单步预测）节点特征维度。</summary>
    Public Const ModeBFeatDim As Integer = 7

    ''' <summary>来流速度归一化参考值 U_REF（速度特征与标签都除以此值）。</summary>
    Public Const VelocityRef As Double = 2.5

    Private Shared ReadOnly _gridAdjCache As New Dictionary(Of String, SparseAdjacency)

    ''' <summary>
    ''' 获取（带缓存的）指定网格尺寸的 6 邻域稀疏邻接。
    ''' 同尺寸计算域拓扑相同，缓存后训练 / 推理无需重复构建。
    ''' </summary>
    Public Shared Function GetGridAdjacency(nx As Integer, ny As Integer, nz As Integer) As SparseAdjacency
        Dim key = $"{nx}x{ny}x{nz}"
        SyncLock _gridAdjCache
            Dim adj As SparseAdjacency = Nothing
            If Not _gridAdjCache.TryGetValue(key, adj) Then
                adj = SparseAdjacency.BuildFromGrid(nx, ny, nz)
                _gridAdjCache(key) = adj
            End If
            Return adj
        End SyncLock
    End Function

    ''' <summary>
    ''' 获取与给定 VoxelShape 匹配的稀疏邻接。
    ''' </summary>
    Public Shared Function GetGridAdjacency(shape As VoxelShape) As SparseAdjacency
        Return GetGridAdjacency(shape.Width, shape.Height, shape.Depth)
    End Function

    ''' <summary>
    ''' 构建模式A节点特征数组（长度 N*5）：掩膜、三向归一化坐标、U∞/U_REF。
    ''' </summary>
    Public Shared Function BuildModeAFeatures(shape As VoxelShape, freestream As Double) As Single()
        Dim nx = shape.Width, ny = shape.Height, nz = shape.Depth
        Dim n As Integer = nx * ny * nz
        Dim feats(n * ModeAFeatDim - 1) As Single

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim node As Integer = (i * ny + j) * nz + k
                    Dim p As Integer = node * ModeAFeatDim
                    feats(p + 0) = If(shape.IsActive(node), 0.0F, 1.0F)   ' 固体掩膜
                    feats(p + 1) = CSng(i / std.Max(nx - 1, 1))
                    feats(p + 2) = CSng(j / std.Max(ny - 1, 1))
                    feats(p + 3) = CSng(k / std.Max(nz - 1, 1))
                    feats(p + 4) = CSng(freestream / VelocityRef)
                Next
            Next
        Next
        Return feats
    End Function

    ''' <summary>
    ''' 构建模式B节点特征数组（长度 N*7）：掩膜、坐标、当前速度分量（归一化）。
    ''' </summary>
    Public Shared Function BuildModeBFeatures(field As FluidField, freestream As Double) As Single()
        Dim nx = field.Nx, ny = field.Ny, nz = field.Nz
        Dim n As Integer = nx * ny * nz
        Dim feats(n * ModeBFeatDim - 1) As Single
        Dim invU As Single = CSng(1.0 / freestream)
        Dim u0 = field.U.Data, v0 = field.V.Data, w0 = field.W.Data

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim node As Integer = (i * ny + j) * nz + k
                    Dim p As Integer = node * ModeBFeatDim
                    Dim fluid = field.IsActive(i, j, k)
                    feats(p + 0) = If(fluid, 0.0F, 1.0F)
                    feats(p + 1) = CSng(i / std.Max(nx - 1, 1))
                    feats(p + 2) = CSng(j / std.Max(ny - 1, 1))
                    feats(p + 3) = CSng(k / std.Max(nz - 1, 1))
                    feats(p + 4) = If(fluid, u0(node) * invU, 0.0F)
                    feats(p + 5) = If(fluid, v0(node) * invU, 0.0F)
                    feats(p + 6) = If(fluid, w0(node) * invU, 0.0F)
                Next
            Next
        Next
        Return feats
    End Function

    ''' <summary>
    ''' 提取节点级回归标签（长度 N*3）：u/U∞、v/U∞、w/U∞（固体体素为 0）。
    ''' </summary>
    Public Shared Function ExtractLabels(field As FluidField, freestream As Double) As Single()
        Dim nx = field.Nx, ny = field.Ny, nz = field.Nz
        Dim n As Integer = field.TotalVoxels
        Dim labels(n * 3 - 1) As Single
        Dim invU As Single = CSng(1.0 / freestream)
        Dim u0 = field.U.Data, v0 = field.V.Data, w0 = field.W.Data

        For t As Integer = 0 To n - 1
            Dim p As Integer = t * 3
            ' 展平索引 → 三维坐标
            Dim i = t \ (ny * nz)
            Dim rem_ = t Mod (ny * nz)
            Dim j = rem_ \ nz
            Dim k = rem_ Mod nz
            If field.IsActive(i, j, k) Then
                labels(p + 0) = u0(t) * invU
                labels(p + 1) = v0(t) * invU
                labels(p + 2) = w0(t) * invU
            End If
        Next
        Return labels
    End Function

    ''' <summary>
    ''' 将 Single() 特征包装为 [N, featDim] 的 Double 张量（复制）。
    ''' </summary>
    Public Shared Function WrapFeatures(data As Single(), featDim As Integer) As Tensor
        Dim n As Integer = data.Length \ featDim
        Return New Tensor(data, n, featDim)
    End Function

    ''' <summary>
    ''' 将 Single() 标签包装为 [N, 3] 的 Double 张量（复制）。
    ''' </summary>
    Public Shared Function WrapLabels(data As Single()) As Tensor
        Dim n As Integer = data.Length \ 3
        Return New Tensor(data, n, 3)
    End Function

    ''' <summary>
    ''' 将 GNN 输出 [N, 3]（归一化速度）还原为 FluidField：
    ''' U/V/W 反归一化（乘以来流速度），固体体素强制清零，压力/密度置 0。
    ''' </summary>
    Public Shared Function ToFluidField(output As Tensor, shape As VoxelShape, freestream As Double) As FluidField
        Dim nx = shape.Width, ny = shape.Height, nz = shape.Depth
        Dim f As New FluidField(shape)
        Dim data = output.Data

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim node As Integer = (i * ny + j) * nz + k
                    If shape.IsActive(node) Then
                        Dim p As Integer = node * 3
                        f.U.Data(node) = CSng(data(p + 0) * freestream)
                        f.V.Data(node) = CSng(data(p + 1) * freestream)
                        f.W.Data(node) = CSng(data(p + 2) * freestream)
                    End If
                Next
            Next
        Next
        Return f
    End Function

End Class
