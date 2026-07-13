' /********************************************************************************/
'
'   Snapshot.vb
'
'   数据快照 —— CFD 单帧结果的纯数据单元
'
'   作用：
'       把某一时间步的完整流体场（速度、压力、密度）封装为一个
'       "不可变" 的独立数据单元，用于逐帧保存 / 序列化。
'
'   设计说明：
'       - 本类不依赖 FermentationTank / FluidSim 等引擎对象，仅持有 FluidField。
'         这样快照系统就与引擎解耦，可被任意来源的 FluidField 复用。
'       - 构造时对传入的 FluidField 做深拷贝 (Clone)，保证快照拥有独立的
'         数据生命周期，后续引擎继续演进不会污染已保存的快照。
'
' /********************************************************************************/

Namespace Snapshot

    ''' <summary>
    ''' CFD 单帧数据快照 —— 与引擎解耦的纯数据单元。
    ''' 持有某一时间步流体场的深拷贝、步号与时间。
    ''' </summary>
    Public Class Snapshot

        ''' <summary>该快照对应的时间步序号。</summary>
        Public ReadOnly Property StepIndex As Integer

        ''' <summary>该快照对应的模拟时间（累计）。</summary>
        Public ReadOnly Property Time As Double

        ''' <summary>
        ''' 流体场深拷贝（独立生命周期）。
        ''' 包含 U/V/W 速度、Pressure 压力、Density 密度。
        ''' </summary>
        Public ReadOnly Property Field As FluidField

        ''' <summary>
        ''' 创建数据快照。构造时对流体场做深拷贝，确保快照与引擎解耦。
        ''' </summary>
        ''' <param name="field">要快照的流体场（会被 Clone，不持有原引用）</param>
        ''' <param name="stepIndex">时间步序号</param>
        ''' <param name="time">模拟时间</param>
        Public Sub New(field As FluidField, stepIndex As Integer, time As Double)
            Me.Field = field.Clone()
            Me.StepIndex = stepIndex
            Me.Time = time
        End Sub

    End Class

End Namespace
