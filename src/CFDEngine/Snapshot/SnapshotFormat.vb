Namespace Snapshot

    ''' <summary>
    ''' 快照文件格式。用于在 VTK、VTI 与 JSON 三套快照系统之间切换。
    ''' </summary>
    Public Enum SnapshotFormat
        ''' <summary>Legacy VTK：逐帧 .vtk（ASCII）+ animation.pvd（ParaView 可用）</summary>
        Vtk
        ''' <summary>JSON：metadata.json + frame_xxx.json（消除帧间网格定义冗余）</summary>
        Json
        ''' <summary>
        ''' VTK XML ImageData：逐帧 .vti（二进制 Float32）+ animation.pvd + frames.json。
        ''' 推荐格式 —— 体积约为 ASCII VTK 的 1/5，且浏览器端 VTK.js 可直接读取。
        ''' </summary>
        Vti
    End Enum
End Namespace
