' /********************************************************************************/
'
'   CFDEngine.vb
'
'   CFD 引擎 —— 顶层门面 (Facade)
'
'   作用：
'       提供一个简化的、面向用户的 API，封装底层组件。
'       用户只需要调用 CFDEngine 的方法即可完成：
'         - 创建发酵罐 + 搅拌器
'         - 运行模拟
'         - 查询任意体素的速度、压力、密度
'         - 导出结果到文件
'
'   使用示例：
'       Dim engine = CFDEngine.CreateDefault(24, 24, 24)
'       engine.Run(100, 0.1)
'       Dim voxel = engine.GetVoxel(12, 12, 12)
'       Console.WriteLine(voxel)
'
' /********************************************************************************/

Imports System.IO
Imports std = System.Math

Namespace CFDEngine

    ''' <summary>
    ''' 快照文件格式。用于在 VTK 与 JSON 两套快照系统之间切换。
    ''' </summary>
    Public Enum SnapshotFormat
        ''' <summary>Legacy VTK：逐帧 .vtk + animation.pvd（ParaView 可用）</summary>
        Vtk
        ''' <summary>JSON：metadata.json + frame_xxx.json（消除帧间网格定义冗余）</summary>
        Json
    End Enum

    ''' <summary>
    ''' CFD 引擎顶层门面，提供简化的模拟 API。
    ''' </summary>
    Public Class FluidSim

#Region "属性"

        ''' <summary>底层发酵罐模拟</summary>
        Public ReadOnly Property Tank As FermentationTank

#End Region

#Region "构造函数"

        ''' <summary>
        ''' 创建 CFD 引擎实例。
        ''' </summary>
        Public Sub New(tank As FermentationTank)
            Me.Tank = tank
        End Sub

#End Region

#Region "工厂方法"

        ''' <summary>
        ''' 创建一个默认配置的发酵罐 CFD 引擎：
        '''   - 网格尺寸 nx×ny×nz
        '''   - 搅拌器位于罐中央，叶轮在下半部
        '''   - 角速度 3.0 rad/时间单位
        ''' </summary>
        Public Shared Function CreateDefault(nx As Integer, ny As Integer, nz As Integer,
                                            Optional angularVelocity As Double = 3.0) As FluidSim

            ' 搅拌器参数：
            '   旋转轴在网格中心
            '   叶轮半径约为网格宽度的 1/3
            '   叶轮位于 1/3 高度处（发酵罐典型位置）
            '   叶轮厚度 2 个网格
            Dim stirrer As New Stirrer(
                centerX:=(nx - 1) * 0.5,
                centerY:=(ny - 1) * 0.5,
                zCenter:=nz * (1.0 / 3.0),
                radius:=std.Min(nx, ny) * (1.0 / 3.0),
                height:=2.0,
                angularVelocity:=angularVelocity)

            Dim tank As New FermentationTank(nx, ny, nz, stirrer)
            Return New FluidSim(tank)

        End Function

        ''' <summary>
        ''' 创建一个不带搅拌器的空罐（用于自由流测试）。
        ''' </summary>
        Public Shared Function CreateEmpty(nx As Integer, ny As Integer, nz As Integer) As FluidSim
            Dim tank As New FermentationTank(nx, ny, nz, Nothing)
            Return New FluidSim(tank)
        End Function

#End Region

#Region "模拟控制"

        ''' <summary>
        ''' 执行单个时间步。
        ''' </summary>
        Public Sub StepForward(dt As Double)
            Tank.StepForward(dt)
        End Sub

        ''' <summary>
        ''' 执行多个时间步。
        ''' </summary>
        ''' <param name="steps">步数</param>
        ''' <param name="dt">每步时间步长</param>
        ''' <param name="progressCallback">可选：每步回调 (stepIndex, time)</param>
        ''' <param name="recorder">
        ''' 可选：数据快照记录器（VTK 或 JSON 均可，均实现 ISnapshotRecorder）。
        ''' 若非 Nothing，则每步后自动捕获当前流体场为一帧，循环结束后自动收尾。
        ''' 向后兼容：传入 SnapshotRecorder 即使用原有 VTK 快照。
        ''' </param>
        Public Sub Run(steps As Integer, dt As Double,
                       Optional progressCallback As Action(Of Integer, Double) = Nothing,
                       Optional recorder As ISnapshotRecorder = Nothing)

            For s = 1 To steps
                Tank.StepForward(dt)
                If progressCallback IsNot Nothing Then
                    progressCallback(s, Tank.Time)
                End If
                If recorder IsNot Nothing Then
                    recorder.Capture(Tank.Field, Tank.StepCount, Tank.Time)
                End If
            Next

            ' 循环结束后收尾（写出 .pvd 或 metadata.json 的 frames 列表）
            If recorder IsNot Nothing Then
                recorder.Finish()
            End If

        End Sub

        ''' <summary>
        ''' 执行多个时间步，并按指定格式自动创建快照记录器。
        ''' 用 format 参数在 VTK 与 JSON 两套快照系统之间切换；同时保留
        ''' 传入自定义 ISnapshotRecorder 的能力（见另一 Run 重载）。
        ''' </summary>
        ''' <param name="steps">步数</param>
        ''' <param name="dt">每步时间步长</param>
        ''' <param name="progressCallback">每步回调 (stepIndex, time)</param>
        ''' <param name="format">快照格式（Vtk / Json）</param>
        ''' <param name="outputDir">输出目录</param>
        ''' <param name="baseName">帧文件基础名（如 "frame"）</param>
        ''' <param name="interval">采样间隔（每隔多少步捕获一帧）</param>
        ''' <param name="metadata">JSON 模式的元数据；省略时由 Tank 自动派生</param>
        Public Sub Run(steps As Integer, dt As Double,
                       progressCallback As Action(Of Integer, Double),
                       format As SnapshotFormat,
                       outputDir As String,
                       Optional baseName As String = "frame",
                       Optional interval As Integer = 1,
                       Optional metadata As SnapshotMetadata = Nothing)

            Dim rec = CreateRecorder(format, outputDir, baseName, interval, steps, dt, metadata)

            For s = 1 To steps
                Tank.StepForward(dt)
                If progressCallback IsNot Nothing Then
                    progressCallback(s, Tank.Time)
                End If
                rec.Capture(Tank.Field, Tank.StepCount, Tank.Time)
            Next

            rec.Finish()

        End Sub

        ''' <summary>
        ''' 按格式构造对应的快照记录器（工厂 helper）。
        ''' </summary>
        Private Function CreateRecorder(format As SnapshotFormat, outputDir As String,
                                        baseName As String, interval As Integer,
                                        steps As Integer, dt As Double,
                                        metadata As SnapshotMetadata) As ISnapshotRecorder

            Dim estimatedFrames = CInt(std.Ceiling(steps / interval))

            If format = SnapshotFormat.Json Then
                Dim md = If(metadata Is Nothing, SnapshotMetadata.FromTank(Tank, dt), metadata)
                Return New JsonSnapshotRecorder(outputDir, md, baseName, interval, estimatedFrames)
            Else
                Return New SnapshotRecorder(outputDir, baseName, interval,
                                            pvdName:="animation.pvd", estimatedFrames:=estimatedFrames)
            End If

        End Function

#End Region

#Region "查询接口"

        ''' <summary>
        ''' 获取指定体素的全部基础物理量（速度、压力、密度）。
        ''' </summary>
        Public Function GetVoxel(i As Integer, j As Integer, k As Integer) As VoxelData
            Return Tank.GetVoxel(i, j, k)
        End Function

        ''' <summary>
        ''' 获取网格尺寸。
        ''' </summary>
        Public ReadOnly Property GridSize As (nx As Integer, ny As Integer, nz As Integer)
            Get
                Return (Tank.Field.Nx, Tank.Field.Ny, Tank.Field.Nz)
            End Get
        End Property

        ''' <summary>
        ''' 获取当前模拟时间。
        ''' </summary>
        Public ReadOnly Property Time As Double
            Get
                Return Tank.Time
            End Get
        End Property

        ''' <summary>
        ''' 获取已执行步数。
        ''' </summary>
        Public ReadOnly Property StepCount As Integer
            Get
                Return Tank.StepCount
            End Get
        End Property

#End Region

#Region "初始化辅助"

        ''' <summary>
        ''' 在搅拌器位置注入示踪剂（密度），用于可视化混合过程。
        ''' </summary>
        Public Sub InjectDyeAtStirrer(amount As Double)
            If Tank.Stirrer IsNot Nothing Then
                Tank.Stirrer.InjectDye(Tank.Field, amount)
            End If
        End Sub

        ''' <summary>
        ''' 在指定位置注入一团示踪剂（球形）。
        ''' </summary>
        Public Sub InjectDyeBlob(centerI As Integer, centerJ As Integer, centerK As Integer,
                                 radius As Double, amount As Double)

            Dim f = Tank.Field
            Dim r2 = radius * radius
            For i = CInt(std.Max(0, centerI - radius - 1)) To CInt(std.Min(f.Nx - 1, centerI + radius + 1))
                For j = CInt(std.Max(0, centerJ - radius - 1)) To CInt(std.Min(f.Ny - 1, centerJ + radius + 1))
                    For k = CInt(std.Max(0, centerK - radius - 1)) To CInt(std.Min(f.Nz - 1, centerK + radius + 1))
                        Dim di = i - centerI
                        Dim dj = j - centerJ
                        Dim dk = k - centerK
                        If di * di + dj * dj + dk * dk <= r2 Then
                            f.Density(i, j, k) = amount
                        End If
                    Next
                Next
            Next

        End Sub

#End Region

    End Class

End Namespace
