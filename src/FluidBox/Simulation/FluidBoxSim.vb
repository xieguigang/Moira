Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.VisualBasic.Imaging.Physics
Imports Moira.SPHEngine
Imports std = System.Math

Namespace Simulation

    ''' <summary>
    ''' The fluid box: a rectangular container that is filled with a regular
    ''' lattice of SPH particles up to a fraction of its height and is solved
    ''' by <see cref="FluidEngine3D"/> on the cuda backend of the SPH engine.
    ''' </summary>
    ''' <remarks>
    ''' <list type="bullet">
    ''' <item>The particles live in the box local space [0, BoxSize]^3 and the
    ''' container itself never moves: a shake is injected as the inertial
    ''' acceleration of the moving reference frame
    ''' (<see cref="FluidEngine3D.DisturbAccel"/>) plus a rotation of the
    ''' gravity direction.</item>
    ''' <item>The solver runs on its own thread while the ui thread reads the
    ''' very same SoA buffers of <see cref="SphState3D"/>. A frame may mix the
    ''' positions of two sub steps, which is invisible among ten million points
    ''' but saves a copy of the whole state (about 720 MB at that size).</item>
    ''' </list>
    ''' </remarks>
    Public Class FluidBoxSim
        Implements IDisposable

        ''' <summary>the edge length of the cubic container (world units)</summary>
        Public ReadOnly Property BoxSize As Single
        ''' <summary>the height of the liquid block at rest</summary>
        Public ReadOnly Property LiquidHeight As Single
        ''' <summary>how many particles have actually been created</summary>
        Public ReadOnly Property ParticleCount As Integer
        ''' <summary>the spacing of the initial particle lattice</summary>
        Public ReadOnly Property Spacing As Single
        ''' <summary>the smoothing radius h = 2 * spacing</summary>
        Public ReadOnly Property SmoothingRadius As Single

        ''' <summary>the solver of this box</summary>
        Public ReadOnly Property Engine As FluidEngine3D

        ''' <summary>"CUDA-F32" or the name of the cpu fallback backend</summary>
        Public ReadOnly Property BackendName As String
            Get
                Return m_backendName
            End Get
        End Property

        ''' <summary>why the gpu backend could not be used, empty when it runs</summary>
        Public ReadOnly Property GpuError As String
            Get
                Return m_gpuError
            End Get
        End Property

        ''' <summary>is the solver driven by the cuda kernels?</summary>
        Public ReadOnly Property IsGpuEnabled As Boolean
            Get
                Return m_gpuEnabled
            End Get
        End Property

        ''' <summary>the wall clock time of the last step in milliseconds</summary>
        Public ReadOnly Property LastStepMs As Double
            Get
                Return m_stepMs
            End Get
        End Property

        ''' <summary>how many cfl sub steps the last step has taken</summary>
        Public ReadOnly Property LastSubSteps As Integer
            Get
                Return m_subSteps
            End Get
        End Property

        Public ReadOnly Property StepCount As Long
            Get
                Return m_steps
            End Get
        End Property

        Public ReadOnly Property IsRunning As Boolean
            Get
                Return m_running
            End Get
        End Property

        ''' <summary>the message of the last failure of the solver thread</summary>
        Public Property LastError As String

        ''' <summary>
        ''' the simulated time of one step in seconds.
        ''' </summary>
        ''' <remarks>
        ''' A step of ten million particles costs seconds of wall clock time, so
        ''' the animation is far away from real time no matter what. A small
        ''' step keeps the number of cfl sub steps (and therefore the wall clock
        ''' cost of a step) low and gives the eye more frames per minute.
        ''' </remarks>
        Public Property TimeStep As Single = 1.0F / 120.0F
        ''' <summary>the multiplier of the acceleration that a mouse drag injects</summary>
        Public Property ShakeStrength As Single = 1.0F

        Private m_backendName As String = ""
        Private m_gpuError As String = ""
        Private m_gpuEnabled As Boolean = False
        Private m_stepMs As Double = 0
        Private m_subSteps As Integer = 0
        Private m_steps As Long = 0

        Private m_worker As Thread
        Private m_running As Boolean = False
        Private ReadOnly m_lock As New Object()

        ''' <summary>
        ''' create the box, fill it with particles and (when possible) move the
        ''' solver onto the cuda device
        ''' </summary>
        ''' <param name="boxSize">the edge length of the cubic container</param>
        ''' <param name="fillFraction">the height of the liquid as a fraction of the box height</param>
        ''' <param name="particleCount">the wanted number of particles, the lattice is fitted to it</param>
        ''' <param name="enableGpu">try the cuda backend first</param>
        ''' <param name="progress">reports (message, fraction) while the box is being built</param>
        Sub New(boxSize As Single,
                fillFraction As Single,
                particleCount As Integer,
                Optional enableGpu As Boolean = True,
                Optional progress As Action(Of String, Double) = Nothing)

            If boxSize <= 0 Then boxSize = 100.0F
            If fillFraction <= 0 OrElse fillFraction > 1 Then fillFraction = 2.0F / 3.0F
            If particleCount < 1000 Then particleCount = 1000

            _BoxSize = boxSize
            _LiquidHeight = boxSize * fillFraction

            ' ---- fit a regular lattice of n particles into the liquid block ----
            Dim liquidVolume As Double = CDbl(boxSize) * boxSize * CDbl(_LiquidHeight)
            Dim want As Double = (liquidVolume / particleCount) ^ (1.0 / 3.0)
            Dim nx As Integer = std.Max(1, CInt(std.Floor(boxSize / want)))
            Dim ny As Integer = nx
            Dim nz As Integer = std.Max(1, CInt(std.Round(particleCount / CDbl(nx * ny))))

            Dim n As Integer = nx * ny * nz
            Dim sx As Single = CSng(boxSize / nx)
            Dim sy As Single = CSng(boxSize / ny)
            Dim sz As Single = CSng(_LiquidHeight / nz)

            _ParticleCount = n
            _Spacing = CSng((CDbl(sx) + sy + sz) / 3.0)
            _SmoothingRadius = 2.0F * _Spacing

            If progress IsNot Nothing Then
                Call progress($"生成 {n.ToString("N0")} 个粒子点阵", 0.05)
            End If

            ' ---- the container and the solver ----
            Dim boundary As New BoxBoundary3D(boxSize, boxSize, boxSize)
            Dim engine As New FluidEngine3D(boundary, _SmoothingRadius)

            _Engine = engine

            Call FillLattice(engine.State, n, nx, ny, nz, sx, sy, sz)

            If progress IsNot Nothing Then
                Call progress("整定求解器参数", 0.6)
            End If

            Call Tune(engine)

            ' ---- the compute backend: cuda first, the parallel cpu backend
            ' is the fallback of the engine itself ----
            engine.Backend = New SphCpuCompute3D()

            If enableGpu Then
                If progress IsNot Nothing Then
                    Call progress("编译 CUDA 内核并分配显存", 0.7)
                End If

                If CudaSphBackend.TryEnableGpu(engine) Then
                    m_gpuEnabled = True
                    m_backendName = engine.Backend.Name

                    ' the density and the pressure are only read back when a
                    ' snapshot needs them, this halves the pcie traffic of
                    ' every single sub step
                    DirectCast(engine.Backend, CudaSphBackend).FullSync = False
                Else
                    m_gpuEnabled = False
                    m_backendName = engine.Backend.Name
                    m_gpuError = CudaSphBackend.LastError
                End If
            Else
                m_gpuEnabled = False
                m_backendName = engine.Backend.Name
                m_gpuError = "the gpu backend was disabled by the host"
            End If

            If progress IsNot Nothing Then
                Call progress("准备第一帧", 0.9)
            End If
        End Sub

        ''' <summary>
        ''' write the lattice straight into the SoA buffers of the state, no
        ''' intermediate object per particle
        ''' </summary>
        Private Shared Sub FillLattice(state As SphState3D, n As Integer,
                                      nx As Integer, ny As Integer, nz As Integer,
                                      sx As Single, sy As Single, sz As Single)

            ' an exact capacity: EnsureCapacity doubles the buffers when it has
            ' to grow, which would double the memory of ten million particles
            Call state.EnsureCapacity(n)

            Dim px = state.px
            Dim py = state.py
            Dim pz = state.pz
            Const jitter As Single = 0.06F

            ' a shift only hash of the lattice index gives every particle a
            ' deterministic jitter: it breaks the symmetry of the lattice
            ' without an allocation and without any integer multiplication
            Call Parallel.For(0, nz,
                Sub(iz As Integer)
                    Dim zb As UInteger = CUInt(iz) << 16
                    Dim slice As Integer = iz * nx * ny
                    Dim z As Single = (iz + 0.5F) * sz

                    For iy As Integer = 0 To ny - 1
                        Dim y As Single = (iy + 0.5F) * sy
                        Dim yb As UInteger = zb Xor (CUInt(iy) << 8)
                        Dim i As Integer = slice + iy * nx

                        For ix As Integer = 0 To nx - 1
                            Dim h As UInteger = yb Xor CUInt(ix)

                            h = h Xor (h >> 7)
                            h = h Xor (h << 11)
                            Dim jx As Single = (CSng(h And 1023UI) / 1023.0F - 0.5F) * jitter * sx

                            h = h Xor (h >> 5)
                            h = h Xor (h << 9)
                            Dim jy As Single = (CSng(h And 1023UI) / 1023.0F - 0.5F) * jitter * sy

                            h = h Xor (h >> 3)
                            h = h Xor (h << 13)
                            Dim jz As Single = (CSng(h And 1023UI) / 1023.0F - 0.5F) * jitter * sz

                            px(i) = (ix + 0.5F) * sx + jx
                            py(i) = (iy + 0.5F) * sy + jy
                            pz(i) = z + jz
                            i += 1
                        Next
                    Next
                End Sub)

            ' the count has to be published before the dynamic buffers are
            ' cleared, ClearDynamic only touches the first Count elements
            state.Count = n
            Call state.ClearDynamic()
        End Sub

        ''' <summary>
        ''' the physical parameters of a weakly compressible sloshing tank
        ''' </summary>
        Private Sub Tune(engine As FluidEngine3D)
            Dim g As Single = 9.81F
            Dim reference As Single = CSng(std.Sqrt(2.0 * g * _LiquidHeight))

            engine.Gravity = g
            engine.GravityDirection = New Vector3(0, 0, -1)
            engine.ParticleSpacing = _Spacing
            engine.WallPadding = 0.5F * _Spacing
            engine.ParticleSize = _Spacing
            engine.ViscosityStrength = 3.0F
            engine.CollisionDamping = 0.4F
            engine.DisturbDamping = 0.9F

            ' the artificial sound speed drives both the stiffness and the cfl
            ' limit: a large box needs a small factor to keep the sub step
            ' count of one animation frame in the single digits
            engine.SoundSpeedFactor = 3.0F
            engine.ReferenceSpeed = reference
            engine.MaxVelocity = 3.0F * reference
            engine.MaxAccel = 30.0F * g
            engine.MaxSubSteps = 8
            engine.CflFactor = 0.35F
            engine.AutoPressure = True
            engine.AutoCalibrateDensity = True
            engine.DeltaTime = TimeStep
        End Sub

        ''' <summary>
        ''' the live state of the solver, the ui reads it while the solver is
        ''' stepping
        ''' </summary>
        Public Function ReadSnapshot() As SphState3D
            Return Engine.State
        End Function

        ''' <summary>
        ''' inject the inertial acceleration of the container and the gravity
        ''' direction of the tilted box
        ''' </summary>
        ''' <param name="accel">
        ''' the acceleration of the container in world units per second squared.
        ''' A zero vector leaves the current disturbance alone so that the
        ''' engine can decay it and the liquid settles down again.
        ''' </param>
        ''' <param name="gravityDir">the unit vector of the gravity in the box local frame</param>
        Public Sub ApplyShake(accel As Vector3, gravityDir As Vector3)
            If Engine Is Nothing Then Return

            If accel.Magnitude > 0.000001 Then
                Engine.DisturbAccel = New Vector3(
                    accel.x * ShakeStrength,
                    accel.y * ShakeStrength,
                    accel.z * ShakeStrength)
            End If

            If gravityDir.Magnitude > 0.000001 Then
                Engine.GravityDirection = gravityDir
            End If
        End Sub

        ''' <summary>start the background solver</summary>
        Public Sub Start()
            If m_running Then Return

            m_running = True
            m_worker = New Thread(AddressOf WorkerLoop) With {
                .IsBackground = True,
                .Name = "fluid-box-solver"
            }
            Call m_worker.Start()
        End Sub

        ''' <summary>stop the background solver</summary>
        Public Sub [Stop]()
            m_running = False
        End Sub

        ''' <summary>advance the solver by one step on the calling thread</summary>
        Public Sub StepOnce()
            If m_running Then Return

            Dim watch As Stopwatch = Stopwatch.StartNew()

            Try
                Call Engine.RunStep(TimeStep)
            Catch ex As Exception
                LastError = ex.Message
            End Try

            watch.Stop()

            SyncLock m_lock
                m_stepMs = watch.Elapsed.TotalMilliseconds
                m_subSteps = Engine.LastSubSteps
                m_steps += 1
            End SyncLock
        End Sub

        Private Sub WorkerLoop()
            While m_running
                Dim watch As Stopwatch = Stopwatch.StartNew()

                Try
                    Call Engine.RunStep(TimeStep)
                Catch ex As Exception
                    LastError = ex.Message
                    m_running = False
                    Exit While
                End Try

                watch.Stop()

                SyncLock m_lock
                    m_stepMs = watch.Elapsed.TotalMilliseconds
                    m_subSteps = Engine.LastSubSteps
                    m_steps += 1
                End SyncLock
            End While
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            m_running = False
        End Sub
    End Class
End Namespace
