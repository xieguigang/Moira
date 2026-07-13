Namespace Snapshot

    ''' <summary>
    ''' 快照文件格式。用于在 VTK 与 JSON 两套快照系统之间切换。
    ''' </summary>
    Public Enum SnapshotFormat
        ''' <summary>Legacy VTK：逐帧 .vtk + animation.pvd（ParaView 可用）</summary>
        Vtk
        ''' <summary>JSON：metadata.json + frame_xxx.json（消除帧间网格定义冗余）</summary>
        Json
    End Enum
End Namespace