' /********************************************************************************/
'
'   DemoTestEntry.vb
'
'   DemoTestForm 的启动入口（临时，仅开发期测试用）
'
'   编译运行方式：
'       dotnet build src/CDFDxCanvas/CDFDxCanvas.vbproj -p:EnableDemoTest=true
'       src/CDFDxCanvas/bin/Debug/net10.0-windows/CDFDxCanvas.exe
'
'   说明：默认配置（不加 -p:EnableDemoTest）下本项目仍编译为控件库，
'   本模块不会成为入口，不影响控件库的消费方。
'
' /********************************************************************************/

Imports System.Windows.Forms

Module DemoTestEntry

    <STAThread>
    Sub Main()
        ' ---- 命令行测试分支转发：WinForms 演示表单仍为默认入口 ----
        ' 传入 --zmesh-windtunnel / --windtunnel / --gnn-windtunnel 时，
        ' 转交 Program.Main 的控制台测试分支派发执行。
        Dim args = System.Environment.GetCommandLineArgs()
        For Each a In args
            Dim lower = a.ToLower()
            If lower = "--zmesh-windtunnel" OrElse lower = "zmesh-windtunnel" OrElse
               lower = "--windtunnel" OrElse lower = "windtunnel" OrElse
               lower = "--gnn-windtunnel" OrElse lower = "gnn-windtunnel" Then
                Program.Main()
                Return
            End If
        Next

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.Run(New DemoTestForm)
    End Sub

End Module
