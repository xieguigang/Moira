
Imports Microsoft.VisualBasic.Serialization.JSON

Namespace Storage

    Public Class Metadata

        Public Property total As Integer
        Public Property dims As Integer()
        Public Property dimensions As String()

        Public Overrides Function ToString() As String
            Return Me.GetJson
        End Function

    End Class
End Namespace