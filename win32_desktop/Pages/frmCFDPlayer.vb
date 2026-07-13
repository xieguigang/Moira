Imports Galaxy.Workbench
Imports Microsoft.Web.WebView2.Core

Public Class frmCFDPlayer

    Dim background As HttpServices

    Private Async Sub frmCFDPlayer_Load(sender As Object, e As EventArgs) Handles Me.Load
        background = New HttpServices(AppEnvironment.GetWwwRoot)
        background.StartHttp()

        Await WebViewLoader.Init(WebView21, enableDevTool:=False)
    End Sub

    Private Sub frmCFDPlayer_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Try
            Call background.Dispose()
        Catch ex As Exception

        End Try
    End Sub

    Private Sub WebView21_CoreWebView2InitializationCompleted(sender As Object, e As CoreWebView2InitializationCompletedEventArgs) Handles WebView21.CoreWebView2InitializationCompleted
        WebView21.CoreWebView2.Navigate($"http://localhost:{background.port}/cfd-player.html")
    End Sub
End Class