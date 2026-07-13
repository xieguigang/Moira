Imports Microsoft.VisualBasic.Serialization.JSON

Namespace My

    Public Class Settings

        Shared ReadOnly default_file As String = $"{App.ProductProgramData}/workbench_settings.json"

        Private Shared Function CreateNew()
            Dim config As New Settings()
            Call config.GetJson.SaveTo(default_file)
            Return config
        End Function

        Public Shared Function LoadSettings() As Settings
            If Not default_file.FileExists Then
                CreateNew()
            End If

            Dim config As Settings = default_file.LoadJsonFile(Of Settings)

            If config Is Nothing Then
                config = CreateNew()
            End If

            Return config
        End Function

        Public Sub Save()
            Call Me.GetJson.SaveTo(default_file)
        End Sub

    End Class
End Namespace