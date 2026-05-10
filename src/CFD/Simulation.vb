Public MustInherit Class Simulation : Implements IDisposable

    Private disposedValue As Boolean

    ' *************************************************************************
    '                           - DIMENTIONS -         -----                  *
    ' *************************************************************************
    ' number of data points / pixels per dimention
    Public ReadOnly Property xdim As Integer = 1920
    Public ReadOnly Property ydim As Integer = 1080

    Sub New(width As Integer, height As Integer)
        xdim = width
        ydim = height
    End Sub

    ''' <summary>
    ''' *************************************************************************
    ''' METHODS                                                                  *
    ''' **************************************************************************
    ''' </summary>
    Public MustOverride Sub reset()
    Public MustOverride Sub advance()
    Protected MustOverride Sub close()

    Const max As Double = 10

    Friend Shared Sub SuppressDoubleRange(ByRef m As Double()(), Optional min As Double = -max, Optional max As Double = max)
        For x As Integer = 0 To m.Length - 1
            Dim xi = m(x)

            For y As Integer = 0 To xi.Length - 1
                If xi(y) > max Then
                    xi(y) = max
                ElseIf xi(y) < min Then
                    xi(y) = min
                End If
            Next
        Next
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                ' TODO: 释放托管状态(托管对象)
                Call close()
            End If

            ' TODO: 释放未托管的资源(未托管的对象)并重写终结器
            ' TODO: 将大型字段设置为 null
            disposedValue = True
        End If
    End Sub

    ' ' TODO: 仅当“Dispose(disposing As Boolean)”拥有用于释放未托管资源的代码时才替代终结器
    ' Protected Overrides Sub Finalize()
    '     ' 不要更改此代码。请将清理代码放入“Dispose(disposing As Boolean)”方法中
    '     Dispose(disposing:=False)
    '     MyBase.Finalize()
    ' End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ' 不要更改此代码。请将清理代码放入“Dispose(disposing As Boolean)”方法中
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
End Class
