Imports System.Drawing
Imports System.IO
Imports CFD
Imports CFD.Storage
Imports Microsoft.VisualBasic.ApplicationServices
Imports Microsoft.VisualBasic.ApplicationServices.Terminal.ProgressBar.Tqdm
Imports Microsoft.VisualBasic.CommandLine.Reflection
Imports Microsoft.VisualBasic.ComponentModel.Ranges.Model
Imports Microsoft.VisualBasic.Drawing
Imports Microsoft.VisualBasic.Emit.Delegates
Imports Microsoft.VisualBasic.Imaging
Imports Microsoft.VisualBasic.Imaging.Drawing2D.HeatMap
Imports Microsoft.VisualBasic.Imaging.Driver
Imports Microsoft.VisualBasic.Linq
Imports Microsoft.VisualBasic.Parallel
Imports Microsoft.VisualBasic.Scripting.MetaData
Imports Microsoft.VisualBasic.Scripting.Runtime
Imports SkiaSharp
Imports SMRUCC.Rsharp.Runtime
Imports SMRUCC.Rsharp.Runtime.Components
Imports SMRUCC.Rsharp.Runtime.Interop
Imports Folder = Microsoft.VisualBasic.FileIO.Directory
Imports RInternal = SMRUCC.Rsharp.Runtime.Internal

<Package("CFD")>
Module Rscript

    ''' <summary>
    ''' open the CFD frame data matrix storage
    ''' </summary>
    ''' <param name="file"></param>
    ''' <param name="mode"></param>
    ''' <param name="env"></param>
    ''' <returns></returns>
    <ExportAPI("open.pack")>
    <RApiReturn(GetType(FrameWriter), GetType(FrameReader))>
    Public Function open(<RRawVectorArgument>
                         file As Object,
                         Optional mode As FileAccess = FileAccess.Read,
                         Optional env As Environment = Nothing) As Object

        Dim buf = SMRUCC.Rsharp.GetFileStream(file, mode, env)

        If buf Like GetType(Message) Then
            Return buf.TryCast(Of Message)
        End If

        If mode = FileAccess.Read Then
            Return New FrameReader(buf.TryCast(Of Stream))
        Else
            Return New FrameWriter(buf.TryCast(Of Stream))
        End If
    End Function

    ''' <summary>
    ''' Create a new CFD session 
    ''' </summary>
    ''' <returns></returns>
    <ExportAPI("session")>
    <RApiReturn(GetType(Session))>
    Public Function create(storage As FrameWriter,
                           <RRawVectorArgument>
                           Optional dims As Object = "1920,1080",
                           Optional interval As Integer = 30,
                           Optional model_file As String = Nothing,
                           Optional env As Environment = Nothing) As Object

        Dim size = InteropArgumentHelper.getSize(dims, env, [default]:="1920,1080")
        Dim session As New Session(storage)

        If model_file.FileExists Then
            Call storage.setModelBitmap(New Bitmap(New SkiaImage(SKBitmap.Decode(model_file))))
        Else
            If Not model_file Is Nothing Then
                Call $"model file {model_file} is not existsed on the filesystem location!".Warning
            End If
        End If

        Return session _
            .dims(size.SizeParser) _
            .interval(interval) _
            .model(model_file)
    End Function

    ''' <summary>
    ''' start run the simulation
    ''' </summary>
    ''' <param name="ss"></param>
    ''' <param name="max_time"></param>
    ''' <returns></returns>
    <ExportAPI("start")>
    Public Function start(ss As Session, Optional max_time As Integer = 10 ^ 6, Optional n_threads As Integer = 8) As Session
        VectorTask.n_threads = n_threads

        Call ss.iterations(max_time).Run()
        Call ss.Dispose()

        Return ss
    End Function

    ''' <summary>
    ''' read a frame data as raster matrix
    ''' </summary>
    ''' <param name="pack"></param>
    ''' <param name="time"></param>
    ''' <returns></returns>
    <ExportAPI("read.frameRaster")>
    <RApiReturn(GetType(RawRaster))>
    Public Function readFrameRaster(pack As FrameReader, time As Integer, Optional dimension As String = "speed2") As Object
        Dim frame As Double()() = pack.ReadFrame(time, dimension)
        Dim pixels As PixelData() = frame _
            .Select(Function(row, i)
                        Return row.Select(Function(c, j) New PixelData(i + 1, j + 1, c))
                    End Function) _
            .IteratesALL _
            .ToArray

        Return New RawRaster() With {.raster = pixels}
    End Function

    ''' <summary>
    ''' export the simulation result as image frames
    ''' </summary>
    ''' <param name="pack"></param>
    ''' <param name="fs"></param>
    ''' <param name="dimension"></param>
    ''' <param name="colors"></param>
    ''' <param name="color_levels"></param>
    ''' <param name="env"></param>
    ''' <returns></returns>
    <ExportAPI("dump_stream")>
    Public Function dump_stream(pack As FrameReader, fs As Object,
                                Optional dimension As String = "speed2",
                                <RRawVectorArgument>
                                Optional colors As Object = "viridis",
                                Optional color_levels As Integer = 200,
                                Optional env As Environment = Nothing) As Object

        Dim dir As IFileSystemEnvironment

        If fs Is Nothing Then
            Return RInternal.debug.stop("the required filesystem location should not be nothing!", env)
        End If
        If TypeOf fs Is String Then
            dir = Folder.FromLocalFileSystem(CStr(fs))
        ElseIf fs.GetType.ImplementInterface(Of IFileSystemEnvironment) Then
            dir = DirectCast(fs, IFileSystemEnvironment)
        Else
            Return Message.InCompatibleType(GetType(IFileSystemEnvironment), fs.GetType, env)
        End If

        Dim colorSet As String = RColorPalette.getColorSet(colors, [default]:="jet")
        Dim dims As Size = pack.dims
        Dim range As DoubleRange = pack.GetValueRange(dimension)
        Dim scaleTarget As New DoubleRange(0, color_levels - 1)
        Dim model As SkiaImage = Nothing

        If pack.hasModel Then
            model = New SkiaImage(pack.getModel().CastSkiaBitmap)
            model = model.SetTransparent
        End If

        ' there is a bug about image overlaps in skiasharp
        model = Nothing

        For Each time As Integer In TqdmWrapper.Range(1, pack.total)
            Dim frame As Double()() = pack.ReadFrame(time, dimension)
            Dim pixels As PixelData() = frame _
                .AsParallel _
                .Select(Function(row, i)
                            Return row.Select(Function(c, j) New PixelData(i + 1, j + 1, range.ScaleMapping(c, scaleTarget)))
                        End Function) _
                .IteratesALL _
                .ToArray

            Dim bitmap As Bitmap = New PixelRender(
                colorSet:=colorSet,
                mapLevels:=color_levels,
                defaultColor:=Color.Transparent
            ).RenderRasterImage(
                pixels:=pixels,
                size:=dims,
                fillRect:=True
            )
            Dim file As Stream = dir.OpenFile($"/frame-{time.ToString.PadLeft(5, "0"c)}.png", access:=FileAccess.Write)

            If Not model Is Nothing Then
                Using g As IGraphics = Driver.CreateGraphicsDevice(bitmap.Size, driver:=Drivers.GDI)
                    Call g.DrawImage(bitmap, New Point)
                    Call g.DrawImage(model, New Point)
                    Call g.Flush()

                    Call DirectCast(g, GdiRasterGraphics).ImageResource.Save(file, ImageFormats.Png)
                End Using
            Else
                Call bitmap.Save(file, ImageFormats.Png)
            End If

            Call file.Flush()
            Call file.Close()
        Next

        Return True
    End Function
End Module
