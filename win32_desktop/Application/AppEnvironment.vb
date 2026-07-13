Module AppEnvironment

    Public ReadOnly Property globalHttpPort As Integer
        Get
            If http Is Nothing Then
                Return 0
            Else
                Return http.port
            End If
        End Get
    End Property

    Dim http As HttpServices

    Public Function CheckDevelopmentMode() As Boolean
        Dim home As String = App.HOME.GetDirectoryFullPath
        Dim check = home.StartsWith("G:/Moira")

        Return check
    End Function

    Public Function GetWwwRoot() As String
        If AppEnvironment.CheckDevelopmentMode Then
            Return "G:\Moira\src\app"
        Else
            Return App.HOME & "/app"
        End If
    End Function

    Public Function StartGlobalHttp() As Integer
        http = New HttpServices(GetWwwRoot)
        http = http.StartHttp
        Return http.port
    End Function

End Module