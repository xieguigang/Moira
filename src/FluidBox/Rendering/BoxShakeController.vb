Imports System.Diagnostics
Imports Microsoft.VisualBasic.Imaging.Physics
Imports std = System.Math

Namespace Rendering

    ''' <summary>
    ''' Turns a mouse drag into the motion of the container: the inertial
    ''' acceleration that shakes the liquid and the tilt of the box.
    ''' </summary>
    ''' <remarks>
    ''' The container of the solver can not be translated, it is a fixed
    ''' <see cref="BoxBoundary3D"/>. A shake is therefore expressed in the
    ''' reference frame of the box:
    '''
    ''' <list type="bullet">
    ''' <item>the acceleration of the container enters the solver as the
    ''' inertial force <c>-a</c> of the moving frame, which is exactly what
    ''' <see cref="FluidEngine3D.DisturbAccel"/> is meant for. The engine
    ''' decays it on its own so the liquid settles down after the drag.</item>
    ''' <item>the tilt rotates the box on the screen; in the box local frame the
    ''' gravity direction becomes <c>R^T * (0,0,-1)</c>, so the free surface of
    ''' the liquid stays parallel to the horizon while the box is leaning.</item>
    ''' </list>
    '''
    ''' Both effects push the liquid into the same direction: dragging the box
    ''' to the right makes the liquid lag behind on the left hand side.
    ''' </remarks>
    Public Class BoxShakeController

        ''' <summary>how many radians of tilt one pixel of drag produces</summary>
        Public Property TiltPerPixel As Single = 0.0022F
        ''' <summary>the largest tilt angle in radians, about 26 degrees</summary>
        Public Property MaxTilt As Single = 0.45F
        ''' <summary>how many pixels per second of drag equal one g of acceleration</summary>
        Public Property PixelsPerG As Single = 420.0F
        ''' <summary>a global multiplier of the injected acceleration</summary>
        Public Property ShakeStrength As Single = 1.0F
        ''' <summary>the spring constant that pulls the tilt back to zero</summary>
        Public Property SpringStiffness As Single = 55.0F
        ''' <summary>the damping of the tilt spring</summary>
        Public Property SpringDamping As Single = 9.0F
        ''' <summary>the gravity of the scene, it scales the injected acceleration</summary>
        Public Property Gravity As Single = 9.81F

        ''' <summary>is the user dragging the box right now?</summary>
        Public ReadOnly Property IsDragging As Boolean
            Get
                Return m_dragging
            End Get
        End Property

        ''' <summary>the tilt about the x axis in radians</summary>
        Public ReadOnly Property TiltX As Single
            Get
                Return m_tiltX
            End Get
        End Property

        ''' <summary>the tilt about the y axis in radians</summary>
        Public ReadOnly Property TiltY As Single
            Get
                Return m_tiltY
            End Get
        End Property

        ''' <summary>the row major 3x3 rotation of the box, nine elements</summary>
        Public ReadOnly Property Rotation As Single()
            Get
                Return m_rotation
            End Get
        End Property

        ''' <summary>
        ''' the unit vector of the gravity in the box local frame,
        ''' <c>R^T * (0,0,-1)</c>
        ''' </summary>
        Public ReadOnly Property GravityDirection As Vector3
            Get
                ' R = Ry(tiltY) * Rx(tiltX) =
                '   [ cb, sb*sa, sb*ca ]
                '   [ 0,  ca,   -sa    ]
                '   [ -sb, cb*sa, cb*ca ]
                ' the third row negated is R^T * (0,0,-1)
                Dim sa As Single = CSng(std.Sin(m_tiltX))
                Dim ca As Single = CSng(std.Cos(m_tiltX))
                Dim sb As Single = CSng(std.Sin(m_tiltY))
                Dim cb As Single = CSng(std.Cos(m_tiltY))

                Return New Vector3(sb, -cb * sa, -cb * ca)
            End Get
        End Property

        ''' <summary>
        ''' the inertial acceleration of the frame that has to be injected into
        ''' the solver, zero while the box is at rest
        ''' </summary>
        Public ReadOnly Property Acceleration As Vector3
            Get
                Return New Vector3(m_accelX, m_accelY, m_accelZ)
            End Get
        End Property

        ''' <summary>the screen speed of the drag in pixels per second</summary>
        Public ReadOnly Property DragSpeed As Single
            Get
                Return CSng(std.Sqrt(m_speedX * m_speedX + m_speedY * m_speedY))
            End Get
        End Property

        Private m_dragging As Boolean = False
        Private m_lastX As Integer = 0
        Private m_lastY As Integer = 0
        Private m_lastTicks As Long = 0
        Private m_dragX As Single = 0
        Private m_dragY As Single = 0
        Private m_tiltX As Single = 0
        Private m_tiltY As Single = 0
        Private m_velX As Single = 0
        Private m_velY As Single = 0
        Private m_speedX As Single = 0
        Private m_speedY As Single = 0
        Private m_accelX As Single = 0
        Private m_accelY As Single = 0
        Private m_accelZ As Single = 0
        Private ReadOnly m_rotation As Single() = New Single(8) {1, 0, 0, 0, 1, 0, 0, 0, 1}
        Private ReadOnly m_stamp As Stopwatch = Stopwatch.StartNew()

        ''' <summary>start dragging the box</summary>
        Public Sub BeginDrag(x As Integer, y As Integer)
            m_dragging = True
            m_lastX = x
            m_lastY = y
            m_lastTicks = m_stamp.ElapsedMilliseconds
            m_dragX = 0
            m_dragY = 0
            m_speedX = 0
            m_speedY = 0
            m_accelX = 0
            m_accelY = 0
            m_accelZ = 0
        End Sub

        ''' <summary>
        ''' continue the drag, the offset drives the tilt and the speed drives
        ''' the inertial acceleration
        ''' </summary>
        Public Sub Drag(x As Integer, y As Integer)
            If Not m_dragging Then
                Call BeginDrag(x, y)
                Return
            End If

            Dim dx As Integer = x - m_lastX
            Dim dy As Integer = y - m_lastY
            Dim now As Long = m_stamp.ElapsedMilliseconds
            Dim dt As Double = (now - m_lastTicks) / 1000.0

            m_lastX = x
            m_lastY = y
            m_lastTicks = now

            If dx = 0 AndAlso dy = 0 Then Return
            If dt <= 0 Then dt = 1.0 / 60.0
            If dt > 0.25 Then dt = 0.25

            m_dragX += dx
            m_dragY += dy

            ' the raw speed of one mouse move event is noisy, a light low pass
            ' keeps the shake from jittering
            Dim vx As Single = CSng(dx / dt)
            Dim vy As Single = CSng(dy / dt)

            m_speedX = m_speedX * 0.55F + vx * 0.45F
            m_speedY = m_speedY * 0.55F + vy * 0.45F

            Dim scale As Single = Gravity * ShakeStrength / std.Max(1.0F, PixelsPerG)

            ' the frame of the box accelerates towards the drag, the liquid
            ' inside feels the opposite
            m_accelX = -m_speedX * scale
            m_accelY = 0
            m_accelZ = m_speedY * scale
        End Sub

        ''' <summary>release the box, the tilt springs back and the
        ''' disturbance decays inside of the solver</summary>
        Public Sub EndDrag()
            m_dragging = False
            m_speedX = 0
            m_speedY = 0
            m_accelX = 0
            m_accelY = 0
            m_accelZ = 0
        End Sub

        ''' <summary>drop the tilt and the acceleration at once</summary>
        Public Sub Reset()
            Call EndDrag()

            m_dragX = 0
            m_dragY = 0
            m_tiltX = 0
            m_tiltY = 0
            m_velX = 0
            m_velY = 0

            Call UpdateRotation()
        End Sub

        ''' <summary>
        ''' integrate the tilt spring
        ''' </summary>
        ''' <param name="dt">the elapsed time in seconds</param>
        Public Sub Update(dt As Single)
            If dt <= 0 Then Return
            If dt > 0.1F Then dt = 0.1F

            Dim targetX As Single = 0
            Dim targetY As Single = 0

            If m_dragging Then
                ' dragging to the right leans the box towards the left, so that
                ' the tilt and the inertia push the liquid the same way
                targetY = Clamp(-m_dragX * TiltPerPixel)
                targetX = Clamp(m_dragY * TiltPerPixel)
            End If

            m_velX += (SpringStiffness * (targetX - m_tiltX) - SpringDamping * m_velX) * dt
            m_velY += (SpringStiffness * (targetY - m_tiltY) - SpringDamping * m_velY) * dt
            m_tiltX += m_velX * dt
            m_tiltY += m_velY * dt

            m_tiltX = Clamp(m_tiltX)
            m_tiltY = Clamp(m_tiltY)

            Call UpdateRotation()

            If Not m_dragging Then
                ' the inertial kick is gone as soon as the box is released, the
                ' solver decays what is left of it
                m_accelX = 0
                m_accelY = 0
                m_accelZ = 0
            End If
        End Sub

        Private Function Clamp(v As Single) As Single
            If v > MaxTilt Then Return MaxTilt
            If v < -MaxTilt Then Return -MaxTilt
            Return v
        End Function

        Private Sub UpdateRotation()
            Dim sa As Single = CSng(std.Sin(m_tiltX))
            Dim ca As Single = CSng(std.Cos(m_tiltX))
            Dim sb As Single = CSng(std.Sin(m_tiltY))
            Dim cb As Single = CSng(std.Cos(m_tiltY))

            ' R = Ry(tiltY) * Rx(tiltX), row major
            m_rotation(0) = cb
            m_rotation(1) = sb * sa
            m_rotation(2) = sb * ca
            m_rotation(3) = 0
            m_rotation(4) = ca
            m_rotation(5) = -sa
            m_rotation(6) = -sb
            m_rotation(7) = cb * sa
            m_rotation(8) = cb * ca
        End Sub
    End Class
End Namespace
