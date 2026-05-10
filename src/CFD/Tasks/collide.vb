Imports Microsoft.VisualBasic.Parallel

Namespace Tasks

    ' 将for循环切割为不同的片段，每一条并行线程执行一个for循环片段

    Public Class collide : Inherits VectorTask

        ReadOnly cfd As FluidDynamics
        ReadOnly rho_max As Double = 1
        ReadOnly rho_min As Double = -1

        Public ReadOnly Property cpu_threads As Integer
            Get
                Return cpu_count
            End Get
        End Property

        Public Sub New(cfd As FluidDynamics)
            MyBase.New(nsize:=cfd.xdim)
            Me.cfd = cfd
        End Sub

        ''' <summary>
        ''' run thread parallel
        ''' </summary>
        ''' <param name="start">parallel task partition start index</param>
        ''' <param name="ends">parallel task partition end index</param>
        ''' <param name="cpu_id">thread id</param>
        Protected Overrides Sub Solve(start As Integer, ends As Integer, cpu_id As Integer)
            Dim this_rho, one9thn, one36thn, vx, vy, vx2, vy2, vx3, vy3, vxvy2, v2, v215 As Double
            Dim omega = 1 / (3 * cfd.viscocity + 0.5) ' reciprocal of tau, the relaxation time

            Dim n0 As Double()
            Dim nN As Double()
            Dim nS As Double()
            Dim nE As Double()
            Dim nW As Double()
            Dim nNW As Double()
            Dim nNE As Double()
            Dim nSW As Double()
            Dim nSE As Double()

            ' Calculated variables
            Dim rho As Double()      ' macroscopic density
            Dim xvel As Double()     ' macroscopic velocity [ux]
            Dim yvel As Double()     ' macroscopic velocity [uy]
            Dim speed2 As Double()
            Dim barrier As Boolean() ' Boolean array, true at sites that contain barriers

            Const zero As Double = 0

            For x As Integer = start To ends

                n0 = cfd.n0(x)
                nN = cfd.nN(x)
                nS = cfd.nS(x)
                nE = cfd.nE(x)
                nW = cfd.nW(x)
                nNW = cfd.nNW(x)
                nNE = cfd.nNE(x)
                nSW = cfd.nSW(x)
                nSE = cfd.nSE(x)

                rho = cfd.rho(x)
                xvel = cfd.xvel(x)
                yvel = cfd.yvel(x)
                speed2 = cfd.speed2(x)
                barrier = cfd.barrier(x)

                For y As Integer = 0 To cfd.ydim - 1
                    If Not barrier(y) Then
                        this_rho = n0(y) + nN(y) + nS(y) + nE(y) + nW(y) + nNW(y) + nNE(y) + nSW(y) + nSE(y)

                        If this_rho > rho_max Then
                            this_rho = rho_max
                        ElseIf this_rho < rho_min Then
                            this_rho = rho_min
                        End If

                        ' macroscopic density may be needed for plotting
                        cfd.rho(x)(y) = this_rho

                        one9thn = one9th * this_rho
                        one36thn = one36th * this_rho

                        If this_rho > zero Then
                            vx = (nE(y) + nNE(y) + nSE(y) - nW(y) - nNW(y) - nSW(y)) / this_rho
                        Else
                            vx = 0
                        End If

                        cfd.xvel(x)(y) = vx ' may be needed for plotting

                        If this_rho > zero Then
                            vy = (nN(y) + nNE(y) + nNW(y) - nS(y) - nSE(y) - nSW(y)) / this_rho
                        Else
                            vy = 0
                        End If

                        cfd.yvel(x)(y) = vy ' may be needed for plotting

                        vx3 = 3 * vx
                        vy3 = 3 * vy
                        vx2 = vx * vx
                        vy2 = vy * vy
                        vxvy2 = 2 * vx * vy
                        v2 = vx2 + vy2

                        cfd.speed2(x)(y) = v2 ' may be needed for plotting

                        v215 = 1.5 * v2
                        cfd.n0(x)(y) += omega * (four9ths * this_rho * (1 - v215) - n0(y))
                        cfd.nE(x)(y) += omega * (one9thn * (1 + vx3 + 4.5 * vx2 - v215) - nE(y))
                        cfd.nW(x)(y) += omega * (one9thn * (1 - vx3 + 4.5 * vx2 - v215) - nW(y))
                        cfd.nN(x)(y) += omega * (one9thn * (1 + vy3 + 4.5 * vy2 - v215) - nN(y))
                        cfd.nS(x)(y) += omega * (one9thn * (1 - vy3 + 4.5 * vy2 - v215) - nS(y))
                        cfd.nNE(x)(y) += omega * (one36thn * (1 + vx3 + vy3 + 4.5 * (v2 + vxvy2) - v215) - nNE(y))
                        cfd.nNW(x)(y) += omega * (one36thn * (1 - vx3 + vy3 + 4.5 * (v2 - vxvy2) - v215) - nNW(y))
                        cfd.nSE(x)(y) += omega * (one36thn * (1 + vx3 - vy3 + 4.5 * (v2 - vxvy2) - v215) - nSE(y))
                        cfd.nSW(x)(y) += omega * (one36thn * (1 - vx3 - vy3 + 4.5 * (v2 + vxvy2) - v215) - nSW(y))
                    End If
                Next
            Next
        End Sub
    End Class
End Namespace