Imports Microsoft.VisualBasic.Drawing
Imports SMRUCC.Rsharp.Runtime.Interop

<Assembly: RPackageModule>

Public Class zzz

    Public Shared Sub onLoad()
        SkiaDriver.Register()
    End Sub
End Class
