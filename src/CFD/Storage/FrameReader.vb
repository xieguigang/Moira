Imports System.Drawing
Imports System.IO
Imports System.Runtime.CompilerServices
Imports Microsoft.VisualBasic.ComponentModel.Collection
Imports Microsoft.VisualBasic.ComponentModel.Ranges.Model
Imports Microsoft.VisualBasic.Data.IO
Imports Microsoft.VisualBasic.DataStorage.HDSPack
Imports Microsoft.VisualBasic.DataStorage.HDSPack.FileSystem
Imports Microsoft.VisualBasic.Imaging
Imports Microsoft.VisualBasic.Imaging.BitmapImage
Imports Microsoft.VisualBasic.Serialization.JSON

Namespace Storage

    Public Class FrameReader : Implements IDisposable

        ReadOnly buf As StreamPack

        Dim ranges As Dictionary(Of String, Double())

        Public ReadOnly Property dims As Size
        Public ReadOnly Property total As Integer
        Public ReadOnly Property dimensions As String()

        Private disposedValue As Boolean

        Sub New(file As Stream)
            buf = New StreamPack(file, [readonly]:=True)
            Call loadMetadata()
        End Sub

        Public Function hasModel() As Boolean
            Dim path As String = $"/model.img"
            Dim size As String = $"/model.json"

            Return buf.FileExists(path) AndAlso buf.FileExists(size)
        End Function

        ''' <summary>
        ''' 
        ''' </summary>
        ''' <returns>An in-memory bitmap image object with transparent channels</returns>
        Public Function getModel() As Bitmap
            Dim size As Integer() = buf.ReadText($"/model.json").Trim.LoadJSON(Of Integer())
            Dim bmpBuf As Byte() = buf.ReadBinary($"/model.img").ToArray
            Dim bmp As New BitmapBuffer(bmpBuf, New Size(size(0), size(1)), size(2))

            ' replace the black to transparent?
            Return New Bitmap(bmp)
        End Function

        <MethodImpl(MethodImplOptions.AggressiveInlining)>
        Public Function GetValueRange(dimension As String) As DoubleRange
            Return New DoubleRange(ranges(dimension))
        End Function

        Private Sub loadMetadata()
            Dim json As String = buf.ReadText("/metadata.json")
            Dim metadata As Metadata = json.LoadJSON(Of Metadata)

            _dims = New Size(metadata.dims(0), metadata.dims(1))
            _dimensions = metadata.dimensions
            _total = metadata.total / dimensions.Length - 1

            json = buf.ReadText("/ranges.json")
            ranges = json.LoadJSON(Of Dictionary(Of String, Double()))
        End Sub

        Public Function ReadFrame(i As Integer, dimension As String) As Double()()
            Dim path As String = $"/framedata/{dimension}/{i}.dat"
            Dim file As Stream = buf.OpenFile(path, FileMode.Open, FileAccess.Read)
            Dim rd As New BinaryDataReader(file) With {
                .ByteOrder = ByteOrder.BigEndian
            }
            Dim framedata As Double()() = RectangularArray.Matrix(Of Double)(dims.Width, dims.Height)

            For offset As Integer = 0 To framedata.Length - 1
                framedata(offset) = rd.ReadDoubles(count:=dims.Height)
            Next

            Return framedata
        End Function

        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    ' TODO: 释放托管状态(托管对象)
                    Call buf.Dispose()
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
End Namespace