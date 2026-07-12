' /********************************************************************************/
'
'   ISnapshotRecorder.vb
'
'   快照记录器统一接口
'
'   作用：
'       为 VTK (SnapshotRecorder) 与 JSON (JsonSnapshotRecorder) 两套快照系统
'       提供统一抽象，使 FluidSim.Run 能以接口类型接收任一实现，从而在两套
'       格式之间自由切换，而不破坏原有 VTK 逻辑。
'
'   设计说明：
'       - Capture 与 Finish 的语义与原有 SnapshotRecorder 的 Capture / WriteIndex 对齐。
'       - Finish 在模拟结束后调用一次，负责写出帧集合 / 索引（.pvd 或 metadata.json 的 frames 列表）。
'       - 仅依赖 FluidField，与引擎对象解耦。
'
' /********************************************************************************/

Namespace CFDEngine

    ''' <summary>
    ''' 快照记录器统一接口。VTK 与 JSON 两套记录器均实现本接口，
    ''' 供 FluidSim.Run 以多态方式调用，从而在格式之间切换。
    ''' </summary>
    Public Interface ISnapshotRecorder

        ''' <summary>输出目录。</summary>
        ReadOnly Property OutputDir As String

        ''' <summary>已写出的帧数。</summary>
        ReadOnly Property FrameCount As Integer

        ''' <summary>
        ''' 捕获当前流体场为一帧（各实现决定写入 .vtk 还是 frame_xxx.json）。
        ''' 若 step 不满足采样间隔则跳过（由实现自行判断）。
        ''' </summary>
        ''' <param name="field">当前流体场</param>
        ''' <param name="stepIndex">当前时间步序号</param>
        ''' <param name="time">当前模拟时间</param>
        Sub Capture(field As FluidField, stepIndex As Integer, time As Double)

        ''' <summary>
        ''' 模拟结束后收尾：写出动画集合 / 帧索引（如 .pvd 或 metadata.json 的 frames 列表）。
        ''' 对应原 SnapshotRecorder.WriteIndex。
        ''' </summary>
        Sub Finish()

    End Interface

End Namespace
