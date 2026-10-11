' /********************************************************************************/
'
'   Program.vb
'
'   EulerianFvm 命令行入口。
'
'   用法：
'       EulerianFvm windtunnel --mesh <eulerian.zip> [选项]
'       EulerianFvm tank [--mesh <eulerian.zip>] [选项]
'       EulerianFvm --help
'
'   退出码：0 = 成功；1 = 运行期失败；2 = 参数错误。
'
' /********************************************************************************/

Imports System
Imports WindTunnel

Module Program

    ''' <summary>入口。</summary>
    Function Main(args As String()) As Integer
        Dim options As CliOptions = CliOptions.Parse(args)

        Return WindTunnelRunner.Run(options)
    End Function

End Module
