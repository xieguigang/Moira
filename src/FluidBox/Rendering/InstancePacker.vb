Imports System.Diagnostics
Imports System.Threading
Imports Microsoft.VisualBasic.Imaging.Physics
Imports FluidBox.Simulation

Namespace Rendering

    ''' <summary>
    ''' Packs the instance data of the point cloud on its own thread so that the
    ''' ui thread only has to hand the finished buffer to the gpu.
    ''' </summary>
    ''' <remarks>
    ''' A frame of ten million particles costs tens of milliseconds of cpu time
    ''' to pack, which is far more than the 16 ms budget of one frame. The pack
    ''' therefore runs on a background thread while the ui thread keeps painting
    ''' the last cloud that the packer has finished, so dragging the box never
    ''' blocks the message loop.
    '''
    ''' Three buffers are rotated: one is on the gpu pipeline (busy), one is the
    ''' newest finished cloud (the front) and the third is the one the packer is
    ''' writing. A buffer that has been handed to the gpu is never written to
    ''' until the frame has been painted.
    '''
    ''' The packer reads the SoA buffers of the solver while the solver is
    ''' stepping, so a frame can mix two sub steps. That is the same behaviour
    ''' the canvas had before and is invisible among millions of points.
    ''' </remarks>
    Public Class InstancePacker : Implements IDisposable

        ''' <summary>how many instance buffers are rotated</summary>
        Private Const Slots As Integer = 3

        Private ReadOnly m_mapper As SpeedHeatMapper
        Private ReadOnly m_lock As New Object()
        Private ReadOnly m_signal As New AutoResetEvent(False)
        Private ReadOnly m_data As Single()() = New Single(Slots - 1)() {}
        Private ReadOnly m_produced As Integer() = New Integer(Slots - 1) {}
        Private ReadOnly m_busy As Boolean() = New Boolean(Slots - 1) {}
        Private ReadOnly m_wanted As Single() = New Single(8) {1, 0, 0, 0, 1, 0, 0, 0, 1}
        Private ReadOnly m_work As Single() = New Single(8) {1, 0, 0, 0, 1, 0, 0, 0, 1}

        Private m_state As SphState3D
        Private m_count As Integer = 0
        Private m_budget As Integer = 0
        Private m_ox As Single = 0
        Private m_oy As Single = 0
        Private m_oz As Single = 0
        Private m_pending As Boolean = False
        Private m_front As Integer = -1
        Private m_generation As Long = 0
        Private m_packMs As Double = 0

        Private m_worker As Thread
        Private m_running As Boolean = False

        Sub New(mapper As SpeedHeatMapper)
            m_mapper = mapper
        End Sub

        ''' <summary>the cpu time of the last pack in milliseconds</summary>
        Public ReadOnly Property PackMs As Double
            Get
                Return m_packMs
            End Get
        End Property

        ''' <summary>how many packs have been finished so far</summary>
        Public ReadOnly Property Generation As Long
            Get
                Return m_generation
            End Get
        End Property

        ''' <summary>how many clouds the canvas has asked for</summary>
        Public ReadOnly Property Requests As Long
            Get
                Return m_requests
            End Get
        End Property

        Private m_requests As Long = 0

        ''' <summary>
        ''' give every buffer room for <paramref name="budget"/> points
        ''' </summary>
        Public Sub Resize(budget As Integer)
            If budget < 1 Then budget = 1

            Dim size As Integer = budget * SpeedHeatMapper.FloatsPerPoint

            SyncLock m_lock
                If m_data(0) IsNot Nothing AndAlso m_data(0).Length = size Then
                    Return
                End If

                For i As Integer = 0 To Slots - 1
                    m_data(i) = New Single(size - 1) {}
                    m_produced(i) = 0
                    m_busy(i) = False
                Next

                m_front = -1
                m_budget = budget
            End SyncLock
        End Sub

        ''' <summary>
        ''' ask the packer for a cloud of the current state
        ''' </summary>
        ''' <param name="rotation">the row major 3x3 world transform of the box</param>
        Public Sub Request(state As SphState3D, count As Integer, rotation As Single(),
                           ox As Single, oy As Single, oz As Single)

            If state Is Nothing OrElse count <= 0 Then Return

            SyncLock m_lock
                If m_data(0) Is Nothing Then Return

                Call Array.Copy(rotation, m_wanted, 9)

                m_state = state
                m_count = count
                m_ox = ox
                m_oy = oy
                m_oz = oz
                m_pending = True
                m_requests += 1
            End SyncLock

            Call m_signal.Set()
        End Sub

        ''' <summary>
        ''' take the newest finished cloud, the buffer stays locked until
        ''' <see cref="ReleaseTaken"/> is called
        ''' </summary>
        Public Function TryTake(ByRef data As Single(), ByRef count As Integer,
                                ByRef generation As Long) As Boolean

            SyncLock m_lock
                If m_front < 0 Then Return False

                Dim i As Integer = m_front

                If m_produced(i) <= 0 OrElse m_busy(i) Then
                    Return False
                End If

                m_busy(i) = True
                data = m_data(i)
                count = m_produced(i)
                generation = m_generation

                Return True
            End SyncLock
        End Function

        ''' <summary>
        ''' the frame has been painted: every buffer that was handed over has
        ''' been copied into the instance buffer of the gpu by now, so all of
        ''' them may be written again
        ''' </summary>
        ''' <remarks>
        ''' The release deliberately drops the busy flag of every slot instead
        ''' of the one of the last take: the canvas can hand over a second
        ''' buffer before the paint of the first one when the packer is faster
        ''' than the display, and remembering a single slot would leak the flag
        ''' of the older one. Three leaks are enough to leave the packer
        ''' without any free buffer at all.
        ''' </remarks>
        Public Sub ReleaseTaken()
            SyncLock m_lock
                For i As Integer = 0 To Slots - 1
                    m_busy(i) = False
                Next
            End SyncLock
        End Sub

        Public Sub Start()
            If m_running Then Return

            m_running = True
            ' A pack costs a few milliseconds only, but it has to happen while
            ' the solver is stepping: at a lower priority the solver starves it
            ' and the box stops following the mouse.
            m_worker = New Thread(AddressOf WorkerLoop) With {
                .IsBackground = True,
                .Name = "fluid-box-packer",
                .Priority = ThreadPriority.Normal
            }
            Call m_worker.Start()
        End Sub

        Public Sub [Stop]()
            m_running = False
            Call m_signal.Set()
        End Sub

        Private Sub WorkerLoop()
            While m_running
                Call m_signal.WaitOne(50)

                If Not m_running Then Exit While

                Dim state As SphState3D
                Dim count As Integer
                Dim budget As Integer
                Dim ox As Single, oy As Single, oz As Single

                SyncLock m_lock
                    If Not m_pending Then Continue While

                    m_pending = False
                    Call Array.Copy(m_wanted, m_work, 9)

                    state = m_state
                    count = m_count
                    budget = m_budget
                    ox = m_ox
                    oy = m_oy
                    oz = m_oz
                End SyncLock

                Dim slot As Integer = -1

                SyncLock m_lock
                    For i As Integer = 0 To Slots - 1
                        If Not m_busy(i) AndAlso i <> m_front Then
                            slot = i
                            Exit For
                        End If
                    Next
                End SyncLock

                If slot < 0 Then Continue While

                Dim watch = Stopwatch.StartNew()

                Call m_mapper.SetTransform(m_work, ox, oy, oz)
                Call m_mapper.BuildInto(state, count, budget, m_data(slot))

                watch.Stop()

                Dim produced As Integer = m_mapper.Count

                SyncLock m_lock
                    m_produced(slot) = produced
                    m_front = slot
                    m_generation += 1
                    m_packMs = watch.Elapsed.TotalMilliseconds
                End SyncLock
            End While
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Call [Stop]()
        End Sub
    End Class
End Namespace
