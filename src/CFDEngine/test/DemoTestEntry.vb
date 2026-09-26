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
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.Run(New DemoTestForm)
    End Sub

End Module
