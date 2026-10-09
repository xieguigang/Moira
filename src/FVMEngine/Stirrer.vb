Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   Stirrer.vb
'
'   搅拌器几何与运动参数（Snapshot 元数据契约类）。
'
' /********************************************************************************/

''' <summary>
''' 搅拌桨（Rushton 圆盘涡轮）几何与运动描述。
''' 坐标为网格体素坐标（与 VoxelShape 同一坐标系）。
''' </summary>
Public Class Stirrer

    ''' <summary>桨盘中心 X（体素坐标）。</summary>
    Public Property CenterX As Double

    ''' <summary>桨盘中心 Y（体素坐标）。</summary>
    Public Property CenterY As Double

    ''' <summary>桨盘中心高度 Z（体素坐标）。</summary>
    Public Property ZCenter As Double

    ''' <summary>桨叶半径（体素单位）。</summary>
    Public Property Radius As Double

    ''' <summary>桨叶区高度（体素单位）。</summary>
    Public Property Height As Double

    ''' <summary>角速度（rad/s，网格物理单位）。</summary>
    Public Property AngularVelocity As Double

    ''' <summary>桨叶轴向泵送速度（Rushton 主要为径向流，此值接近 0）。</summary>
    Public Property AxialVelocity As Double

End Class
