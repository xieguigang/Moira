' /********************************************************************************/
'
'   StreamlineDiag —— WindTunnelBox「流线图」空白问题诊断程序
'
'   背景：
'       在 src\WindTunnelBox\FormWindTunnel.vb 中加载
'       src\ZMesh\CityBlender\samples\city_seed42.eulerian.zip，
'       跑完 FVM 模拟后选择【流线图】，画布一片空白。
'       怀疑：WindTunnelCanvas 默认 HideStraightStreamlines = True，
'             用 StreamlineSet.MinCurvature = 0.05（Straightness >= 0.05 才显示）
'             把"直线形态"的流线全部过滤掉了。
'
'   本程序复刻 WindTunnelBox 的**完整数据链路**（无 GUI）：
'
'       [A] 网格层      city_seed42.stl → EulerianPipeline → *.eulerian.zip
'       [B] 求解层      EulerianWindTunnelCase（与 FormWindTunnel.BuildOptions 同参数）
'                       + EulerianFvmCompute（CUDA float32 可选）
'       [C] 重采样层    EulerianFieldSampler（逐字复刻 Simulation\EulerianFieldSnapshot.vb）
'                       —— 关键：最近单元中心（零阶，分片常数）
'       [D] 流线层      StreamlineTracer.Trace（src\CDFDxCanvas\Rendering\Streamlines.vb 真身）
'                       + Straightness / MinCurvature 过滤 / BuildLines / StreamlineAnimator
'       [E] 交叉验证    WindTunnel.vbproj 的 CLI 路径（EulerianToCartesianResampler.AutoConfigure
'                       → *.vti）对比同一条链路产出的流线形态
'
'   用法：
'       StreamlineDiag mesh      <eulerian.zip>                     仅网格诊断
'       StreamlineDiag convert   <model.stl> <out.zip> [resolution] STL → Eulerian 网格
'       StreamlineDiag solve     <eulerian.zip> [steps]            求解 + 全套流线诊断
'       StreamlineDiag all       <model.stl> <out.zip> [steps]     转换 + 求解 + 诊断
'
'   日志：stdout 同时以 UTF-8 写入 <本程序目录>\logs\ 下的时间戳日志文件。
'
' /********************************************************************************/

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports Moira.CFDEngine.Eulerian
Imports Moira.CFDEngine.Snapshot
Imports Moira.FVMEngine.Eulerian
Imports Moira.ZMesh
Imports Moira.CDFDxCanvas.Rendering
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Math = System.Math

Module Program

    Private ReadOnly CI As CultureInfo = CultureInfo.InvariantCulture
    Private _log As StreamWriter
    Private _started As Stopwatch
    Private _gpuReady As Boolean
    Private _snapOverride As List(Of Integer)
    Private _prIterOverride As Integer
    Private _prTolOverride As Double
    Private _momSweepOverride As Integer
    Private _speedOverride As Double = -1.0
    Private _coOverride As Double = -1.0

    ' ============================================================================
    '  入口
    ' ============================================================================

    Function Main(args As String()) As Integer
        _started = Stopwatch.StartNew()

        Dim logDir As String = IO.Path.Combine(AppContext.BaseDirectory, "..\..\..\logs")
        logDir = IO.Path.GetFullPath(logDir)
        Call Directory.CreateDirectory(logDir)

        Dim logPath As String = IO.Path.Combine(logDir, $"streamline_diag_{Date.Now:yyyyMMdd_HHmmss}.log")
        _log = New StreamWriter(logPath, False, New UTF8Encoding(False)) With {.AutoFlush = True}

        Dim code As Integer = 2

        Try
            If args.Length = 0 Then
                Call Usage()
                Return 2
            End If

            Select Case args(0).ToLowerInvariant()
                Case "mesh"
                    If args.Length < 2 Then Throw New ArgumentException("mesh 需要 <eulerian.zip>")
                    code = RunMeshOnly(args(1))

                Case "convert"
                    If args.Length < 3 Then Throw New ArgumentException("convert 需要 <model.stl> <out.zip> [resolution]")
                    Dim res As Double = If(args.Length > 3, Double.Parse(args(3), CI), 0.0)
                    code = RunConvert(args(1), args(2), res, args.Contains("--cut-cells"))

                Case "solve"
                    If args.Length < 2 Then Throw New ArgumentException("solve 需要 <eulerian.zip> [steps]")
                    Dim steps As Integer = If(args.Length > 2, Integer.Parse(args(2), CI), 200)

                    Call ParseOverrides(args)
                    code = RunSolve(args(1), steps)

                Case "compare"
                    If args.Length < 2 Then Throw New ArgumentException("compare 需要 <eulerian.zip> [steps] [key=value ...]")
                    Dim steps As Integer = 100

                    If args.Length > 2 AndAlso IsNumeric(args(2)) Then
                        steps = Integer.Parse(args(2), CI)
                    End If

                    code = RunCompare(args(1), steps, args)

                Case "pressurediag"
                    If args.Length < 2 Then Throw New ArgumentException("pressurediag 需要 <eulerian.zip> [steps] [gpu]")
                    Dim psteps As Integer = If(args.Length > 2, Integer.Parse(args(2), CI), 20)
                    Dim pgpu As Boolean = If(args.Length > 3 AndAlso Not args(3).StartsWith("--"), Boolean.Parse(args(3)), True)

                    For Each a As String In args
                        If a.StartsWith("--speed=") Then _speedOverride = Double.Parse(a.Substring(8), CI)
                    Next
                    Call ParseOverrides(args)
                    code = RunPressureDiag(args(1), psteps, pgpu)

                Case "il"
                    If args.Length < 4 Then Throw New ArgumentException("il 需要 <dll> <类型全名> <方法名>")
                    code = RunIlDump(args(1), args(2), args(3))

                Case "paramscan"
                    If args.Length < 2 Then Throw New ArgumentException("paramscan 需要 <eulerian.zip> [steps]")
                    code = RunParamScan(args(1), If(args.Length > 2, Integer.Parse(args(2), CI), 20), args)

                Case "pcorr"
                    If args.Length < 2 Then Throw New ArgumentException("pcorr 需要 <eulerian.zip> [steps]")
                    code = RunPcorrProbe(args(1), If(args.Length > 2, Integer.Parse(args(2), CI), 1))

                Case "deadends"
                    If args.Length < 2 Then Throw New ArgumentException("deadends 需要 <eulerian.zip>")
                    code = RunDeadEndScan(args(1))

                Case "channel"
                    '  channel [nx] [ny] [nz] [speed] [nu] [steps] [gpu] [priter] [prtol] [relaxP] [relaxU] [sweeps]
                    Dim nxc As Integer = If(args.Length > 1, Integer.Parse(args(1), CI), 20)
                    Dim nyc As Integer = If(args.Length > 2, Integer.Parse(args(2), CI), 4)
                    Dim nzc As Integer = If(args.Length > 3, Integer.Parse(args(3), CI), 4)
                    Dim spd As Double = If(args.Length > 4, Double.Parse(args(4), CI), 30.0R)
                    Dim nuc As Double = If(args.Length > 5, Double.Parse(args(5), CI), 0.000015R)
                    Dim stc As Integer = If(args.Length > 6, Integer.Parse(args(6), CI), 50)
                    Dim gpuC As Boolean = If(args.Length > 7, Boolean.Parse(args(7)), True)
                    Dim pic As Integer = If(args.Length > 8, Integer.Parse(args(8), CI), 200)
                    Dim ptc As Double = If(args.Length > 9, Double.Parse(args(9), CI), 1.0E-9R)
                    Dim rpc As Double = If(args.Length > 10, Double.Parse(args(10), CI), 0.3R)
                    Dim ruc As Double = If(args.Length > 11, Double.Parse(args(11), CI), 0.7R)
                    Dim swc As Integer = If(args.Length > 12, Integer.Parse(args(12), CI), 2)

                    Dim obs As Boolean = args.Contains("obstacle")
                    code = RunChannelTest(nxc, nyc, nzc, spd, nuc, stc, gpuC, pic, ptc, rpc, ruc, swc, obs)

                Case "divergence"
                    If args.Length < 2 Then Throw New ArgumentException("divergence 需要 <eulerian.zip>")
                    code = RunDivergenceProbe(args(1))

                Case "momentumdiag"
                    If args.Length < 2 Then Throw New ArgumentException("momentumdiag 需要 <eulerian.zip> [steps]")
                    Dim msteps As Integer = If(args.Length > 2, Integer.Parse(args(2), CI), 5)
                    code = RunMomentumDiag(args(1), msteps)

                Case "resampler"
                    If args.Length < 2 Then Throw New ArgumentException("resampler 需要 <eulerian.zip>")
                    code = RunResamplerProbe(args(1))

                Case "all"
                    If args.Length < 3 Then Throw New ArgumentException("all 需要 <model.stl> <out.zip> [steps]")
                    Dim steps As Integer = If(args.Length > 3, Integer.Parse(args(3), CI), 200)
                    code = RunConvert(args(1), args(2), 0.0)

                    If code = 0 Then
                        code = RunSolve(args(2), steps)
                    End If

                Case Else
                    Call Usage()
                    code = 2
            End Select

        Catch ex As Exception
            Call Line($"")
            Call Line($"[FATAL] {ex.GetType().Name}: {ex.Message}")
            Call Line(ex.ToString())
            code = 1

        Finally
            Call Line($"")
            Call Line($"=== 用时 {_started.Elapsed.TotalSeconds:F1} s，日志：{logPath} ===")

            If _log IsNot Nothing Then
                _log.Flush()
                _log.Dispose()
            End If
        End Try

        Return code
    End Function

    Private Sub Usage()
        Call Line("StreamlineDiag —— WindTunnelBox 流线图空白问题诊断")
        Call Line("")
        Call Line("  StreamlineDiag mesh        <eulerian.zip>                       仅网格诊断")
        Call Line("  StreamlineDiag convert     <model.stl> <out.zip> [resolution]   STL → Eulerian 网格（GPU）")
        Call Line("  StreamlineDiag solve       <eulerian.zip> [steps] [--snap=a,b]  求解 + 流线诊断")
        Call Line("  StreamlineDiag compare     <eulerian.zip> [steps] [only-new]    求解稳定性对照矩阵（19 组参数）")
        Call Line("  StreamlineDiag pressurediag <eulerian.zip> [steps] [gpu]        逐步推进 + 压力方程线性求解探针")
        Call Line("  StreamlineDiag resampler   <eulerian.zip>                       AutoConfigure / 重采样方式探针")
        Call Line("  StreamlineDiag il          <dll> <类型全名> <方法名>             IL 反汇编（核对编译产物与源码）")
        Call Line("  StreamlineDiag all         <model.stl> <out.zip> [steps]        转换 + 求解 + 诊断")
    End Sub

    ''' <summary>同时写控制台与日志文件。</summary>
    Private Sub Line(text As String)
        Console.WriteLine(text)

        If _log IsNot Nothing Then
            _log.WriteLine(text)
        End If
    End Sub

    ''' <summary>解析通用覆盖参数：--snap= / --priter= / --prtol= / --sweep=</summary>
    Private Sub ParseOverrides(args As String())
        For Each a As String In args
            If a.StartsWith("--snap=") Then
                Dim list As New List(Of Integer)

                For Each s As String In a.Substring(7).Split(","c)
                    list.Add(Integer.Parse(s, CI))
                Next

                _snapOverride = list
            ElseIf a.StartsWith("--priter=") Then
                _prIterOverride = Integer.Parse(a.Substring(9), CI)
            ElseIf a.StartsWith("--prtol=") Then
                _prTolOverride = Double.Parse(a.Substring(8), CI)
            ElseIf a.StartsWith("--sweep=") Then
                _momSweepOverride = Integer.Parse(a.Substring(8), CI)
            ElseIf a.StartsWith("--co=") Then
                _coOverride = Double.Parse(a.Substring(5), CI)
            End If
        Next
    End Sub

    Private Sub Section(title As String)
        Call Line("")
        Call Line(New String("="c, 100))
        Call Line($"  {title}")
        Call Line(New String("="c, 100))
    End Sub

    Private Sub Sub_(title As String)
        Call Line("")
        Call Line($"--- {title} " & New String("-"c, Math.Max(0, 90 - title.Length)))
    End Sub

    ' ============================================================================
    '  [A] STL → Eulerian 网格
    ' ============================================================================

    Private Function RunConvert(modelPath As String, outZip As String, resolution As Double,
                                Optional cutCells As Boolean = False) As Integer
        Call Section($"[A] STL → Eulerian 网格：{IO.Path.GetFileName(modelPath)}")

        Call Line($"model      : {modelPath}")
        Call Line($"output zip : {outZip}")
        Call Line($"resolution : {If(resolution > 0, resolution.ToString("G4", CI), "(默认 0.05)")}   cutCells={cutCells}")

        If Not File.Exists(modelPath) Then
            Call Line($"[FAIL] 找不到模型文件：{modelPath}")
            Return 1
        End If

        Call EnableGpu()

        Dim modelExtentAsDiagonal As Double = ModelDiagonal(modelPath)
        Call Line($"模型包围盒对角线 = {modelExtentAsDiagonal.ToString("G4", CI)} m")

        Dim res As Double = If(resolution > 0, resolution, 0.05)
        Call Line($"EulerianPipeline 目标网格尺度 = 对角线 × resolution = " &
                  $"{(modelExtentAsDiagonal * res).ToString("G4", CI)} m")

        Dim sw As Stopwatch = Stopwatch.StartNew()

        Try
            Dim report As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
                modelPath, outZip, EulerianPipeline.MeshType.Octree,
                resolution:=res,
                domainScale:=2.0,
                cutCells:=cutCells,
                smoothLaplacian:=False)

            sw.Stop()

            Call Line($"[OK] {report.ToString()}")
            Call Line($"     网格算法 = {report.MeshKind}，用时 {sw.Elapsed.TotalSeconds:F1} s")

            Dim info As EulerianMeshInfo = EulerianMeshPackage.ReadMetadata(outZip)

            Call Line($"     包元数据：points={info.PointCount}, faces={info.FaceCount}, cells={info.CellCount}, " &
                      $"boundaryFaces={info.BoundaryFaceCount}")
            Call Line($"     bounds min = [{info.BoundsMin(0):G6}, {info.BoundsMin(1):G6}, {info.BoundsMin(2):G6}]")
            Call Line($"     bounds max = [{info.BoundsMax(0):G6}, {info.BoundsMax(1):G6}, {info.BoundsMax(2):G6}]")
            Call Line($"     totalVolume = {info.TotalVolume:G6}")
            Call Line($"     generator = {info.Generator}")

            Dim extentX As Double = info.BoundsMax(0) - info.BoundsMin(0)
            Dim extentY As Double = info.BoundsMax(1) - info.BoundsMin(1)
            Dim extentZ As Double = info.BoundsMax(2) - info.BoundsMin(2)
            Dim cellSize As Double = Math.Pow(Math.Max(info.TotalVolume, 1.0E-300) / Math.Max(1, info.CellCount), 1.0 / 3.0)

            Call Line($"     计算域 = {extentX:G6} × {extentY:G6} × {extentZ:G6} m")
            Call Line($"     等效立方单元边长 ≈ {cellSize:G4} m  →  " &
                      $"约 {extentX / cellSize:F1} × {extentY / cellSize:F1} × {extentZ / cellSize:F1} 单元")

            For Each p As EulerianPatchInfo In info.Patches
                Call Line($"       patch {p.Name,-14} {p.Type,-10} faces={p.FaceCount}")
            Next

            Return 0

        Catch ex As Exception
            sw.Stop()
            Call Line($"[FAIL] 网格转换异常：{ex.GetType().Name}: {ex.Message}")
            Call Line(ex.ToString())
            Return 1
        End Try
    End Function

    ''' <summary>用 STL 的边界框对角线长度估算 modelExtent（与 EulerianPipeline 的语义一致）。</summary>
    Private Function ModelDiagonal(path As String) As Double
        Try
            Dim text As String = Nothing

            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim buf(255) As Byte
                Dim n As Integer = fs.Read(buf, 0, 256)
                text = Encoding.ASCII.GetString(buf, 0, n)
            End Using

            Dim isAscii As Boolean = text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase) AndAlso
                                     text.IndexOf("facet", StringComparison.OrdinalIgnoreCase) >= 0

            Dim minX As Double = Double.MaxValue, minY As Double = Double.MaxValue, minZ As Double = Double.MaxValue
            Dim maxX As Double = Double.MinValue, maxY As Double = Double.MinValue, maxZ As Double = Double.MinValue
            Dim cnt As Integer = 0

            If isAscii Then
                For Each raw As String In File.ReadLines(path)
                    Dim ln As String = raw.Trim()

                    If Not ln.StartsWith("vertex", StringComparison.OrdinalIgnoreCase) Then Continue For

                    Dim parts As String() = ln.Split(New Char() {" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries)

                    If parts.Length < 4 Then Continue For

                    Dim x As Double, y As Double, z As Double

                    If Not Double.TryParse(parts(1), NumberStyles.Float, CI, x) Then Continue For
                    If Not Double.TryParse(parts(2), NumberStyles.Float, CI, y) Then Continue For
                    If Not Double.TryParse(parts(3), NumberStyles.Float, CI, z) Then Continue For

                    If x < minX Then minX = x
                    If y < minY Then minY = y
                    If z < minZ Then minZ = z
                    If x > maxX Then maxX = x
                    If y > maxY Then maxY = y
                    If z > maxZ Then maxZ = z
                    cnt += 1
                Next
            Else
                Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Using br As New BinaryReader(fs)
                        Call br.ReadBytes(80)
                        Dim tri As UInteger = br.ReadUInt32()

                        For t As ULong = 0 To tri - 1UL
                            ' 法向
                            Call br.ReadSingle() : Call br.ReadSingle() : Call br.ReadSingle()

                            For v As Integer = 0 To 2
                                Dim x As Double = br.ReadSingle()
                                Dim y As Double = br.ReadSingle()
                                Dim z As Double = br.ReadSingle()

                                If x < minX Then minX = x
                                If y < minY Then minY = y
                                If z < minZ Then minZ = z
                                If x > maxX Then maxX = x
                                If y > maxY Then maxY = y
                                If z > maxZ Then maxZ = z
                                cnt += 1
                            Next

                            Call br.ReadUInt16()
                        Next
                    End Using
                End Using
            End If

            If cnt = 0 OrElse maxX < minX Then
                Return 1.0
            End If

            Dim dx As Double = maxX - minX, dy As Double = maxY - minY, dz As Double = maxZ - minZ

            Call Line($"  [STL 包围盒] min=({minX:G6}, {minY:G6}, {minZ:G6})  max=({maxX:G6}, {maxY:G6}, {maxZ:G6})  vertices={cnt}")

            Return Math.Sqrt(dx * dx + dy * dy + dz * dz)

        Catch ex As Exception
            Call Line($"  [WARN] 模型包围盒解析失败（{ex.Message}），退回 1.0")
            Return 1.0
        End Try
    End Function

    ' ============================================================================
    '  GPU
    ' ============================================================================

    Private Sub EnableGpu()
        Call Section("[GPU] CUDA float32 后端注册（FVM 求解加速）")

        Try
            _gpuReady = EulerianFvmCompute.TryRegister()

            If _gpuReady Then
                Call Line($"[OK] 已启用：{EulerianFvmCompute.Current.DescribeDevice()}")
            Else
                Call Line($"[WARN] 不可用，回落 CPU double 路径：{EulerianFvmCompute.LastError}")
            End If
        Catch ex As Exception
            _gpuReady = False
            Call Line($"[WARN] GPU 注册异常，回落 CPU：{ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ' ============================================================================
    '  [B] 网格诊断（仅加载既有 zip）
    ' ============================================================================

    Private Function RunMeshOnly(zipPath As String) As Integer
        Call Section($"[B] Eulerian 网格诊断：{IO.Path.GetFileName(zipPath)}")

        If Not File.Exists(zipPath) Then
            Call Line($"[FAIL] 找不到网格包：{zipPath}")
            Return 1
        End If

        Dim info As EulerianMeshInfo = EulerianMeshPackage.ReadMetadata(zipPath)

        Call Line($"points={info.PointCount}, faces={info.FaceCount}, cells={info.CellCount}, " &
                  $"boundaryFaces={info.BoundaryFaceCount}, generator={info.Generator}")

        Dim extX As Double = info.BoundsMax(0) - info.BoundsMin(0)
        Dim extY As Double = info.BoundsMax(1) - info.BoundsMin(1)
        Dim extZ As Double = info.BoundsMax(2) - info.BoundsMin(2)
        Dim cellSize As Double = Math.Pow(Math.Max(info.TotalVolume, 1.0E-300) / Math.Max(1, info.CellCount), 1.0 / 3.0)

        Call Line($"bounds min = [{info.BoundsMin(0):G6}, {info.BoundsMin(1):G6}, {info.BoundsMin(2):G6}]")
        Call Line($"bounds max = [{info.BoundsMax(0):G6}, {info.BoundsMax(1):G6}, {info.BoundsMax(2):G6}]")
        Call Line($"domain     = {extX:G6} × {extY:G6} × {extZ:G6} m")
        Call Line($"avg cell   ≈ {cellSize:G4} m  →  约 {extX / cellSize:F1} × {extY / cellSize:F1} × {extZ / cellSize:F1} 单元")

        For Each p As EulerianPatchInfo In info.Patches
            Call Line($"  patch {p.Name,-14} {p.Type,-10} faces={p.FaceCount}")
        Next

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)

        Sub_("FVM 网格视图")
        Call Line(mesh.Diagnostics.TrimEnd())

        ' 单元尺度分布（八叉树网格的关键：单元尺寸是否均匀）
        Dim vol As Double() = mesh.CellVolumeArray()
        Dim n As Integer = vol.Length
        Dim sorted(n - 1) As Double
        Array.Copy(vol, sorted, n)
        Array.Sort(sorted)

        Call Line($"  单元体积分位（等效边长 = V^(1/3)）：")
        Call Line($"    min    = {Cbrt(sorted(0)):F3} m")
        Call Line($"    p05    = {Cbrt(sorted(CInt(0.05 * (n - 1)))):F3} m")
        Call Line($"    p50    = {Cbrt(sorted(n \ 2)):F3} m")
        Call Line($"    p95    = {Cbrt(sorted(CInt(0.95 * (n - 1)))):F3} m")
        Call Line($"    max    = {Cbrt(sorted(n - 1)):F3} m")

        Return 0
    End Function

    Private Function Cbrt(v As Double) As Double
        If v <= 0 Then Return 0
        Return Math.Exp(Math.Log(v) / 3.0)
    End Function

    ' ============================================================================
    '  [C] 求解 + 流线诊断
    ' ============================================================================

    Private Function RunSolve(zipPath As String, steps As Integer) As Integer
        Call Section($"[C] FVM 求解 + 流线诊断：{IO.Path.GetFileName(zipPath)}（{steps} 步）")

        If Not File.Exists(zipPath) Then
            Call Line($"[FAIL] 找不到网格包：{zipPath}")
            Return 1
        End If

        Call EnableGpu()

        ' ------------------------------------------------------------------
        ' 1. 加载网格（与 WindTunnelSimulationRunner.LoadMesh 完全一致）
        ' ------------------------------------------------------------------
        Sub_("1. 加载网格（WindTunnelSimulationRunner.LoadMesh 路径）")

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Call Line(mesh.Diagnostics.TrimEnd())

        ' ------------------------------------------------------------------
        ' 2. 重采样器（逐字复刻 Simulation\EulerianFieldSnapshot.vb）
        ' ------------------------------------------------------------------
        Sub_("2. 结构化重采样器（EulerianFieldSampler 复刻）")

        Dim sampler As New BoxSampler(mesh, 250000)

        Call Line($"重采样结构格 = {sampler.Nx} × {sampler.Ny} × {sampler.Nz}  " &
                  $"（{sampler.Nx * sampler.Ny * sampler.Nz} 节点，h={sampler.Spacing:G6} m）")
        Call Line($"结构格原点   = ({sampler.OriginX:G6}, {sampler.OriginY:G6}, {sampler.OriginZ:G6})")
        Call Line($"域外掩膜     = {sampler.OutsideCount} / {sampler.Nx * sampler.Ny * sampler.Nz} " &
                  $"({100.0 * sampler.OutsideCount / (sampler.Nx * sampler.Ny * sampler.Nz):F2}% 判为固体/域外)")

        ' 结构格 ↔ 单元的映射块状程度（本问题的核心指标）
        Call Line("")
        Call Line("  【映射块状度】相邻结构格节点是否落在同一个网格单元里：")

        Dim sameX As Double = sampler.SameCellFractionX()
        Dim sameY As Double = sampler.SameCellFractionY()
        Dim sameZ As Double = sampler.SameCellFractionZ()

        Call Line($"    +x 方向相邻节点同单元比例 = {100.0 * sameX:F2}%")
        Call Line($"    +y 方向相邻节点同单元比例 = {100.0 * sameY:F2}%")
        Call Line($"    +z 方向相邻节点同单元比例 = {100.0 * sameZ:F2}%")
        Call Line($"    → 平均 {100.0 * (sameX + sameY + sameZ) / 3.0:F2}% 的相邻节点速度完全相同")

        ' 结构格节点间隔 / 单元等效边长
        Call Line($"    结构格间距 h = {sampler.Spacing:F4} m，" &
                  $"网格 p50 单元等效边长 = {Cbrt(Median(mesh.CellVolumeArray())):F4} m " &
                  $"→ 每个单元平均覆盖 {(Cbrt(Median(mesh.CellVolumeArray())) / sampler.Spacing) ^ 3:F2} 个结构格节点")

        ' ------------------------------------------------------------------
        ' 3. 算例配置（与 FormWindTunnel.BuildOptions 的 UI 默认值一致）
        ' ------------------------------------------------------------------
        Sub_("3. 算例配置（FormWindTunnel.BuildOptions 默认值）")

        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = 30.0R,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.225R,
            .KinematicViscosity = 0.000015R,
            .Turbulence = WindTunnelTurbulence.KEpsilon,
            .TurbulenceIntensity = 0.02R,
            .Compressible = False,
            .UseGpu = _gpuReady,
            .ReferenceArea = 1.0R,
            .PressureMaxIterations = 200}

        Call Line($"InletSpeed      = {opts.InletSpeed} m/s (+x)")
        Call Line($"Density / Nu    = {opts.Density} kg/m³ / {opts.KinematicViscosity:G4} m²/s")
        Call Line($"Turbulence      = {opts.Turbulence}，Ti = {opts.TurbulenceIntensity}")
        Call Line($"UseGpu          = {opts.UseGpu}")
        Call Line($"TurbLengthScale = {opts.TurbulenceLengthScale}（GUI 未设置 → 用类默认值）")

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim testCase As New EulerianWindTunnelCase(mesh, opts)

        Call testCase.Configure()
        Call Line($"Configure 完成，用时 {sw.Elapsed.TotalSeconds:F2} s")

        Call Line($"  配置后：U NaN={NanCount(testCase.Fields.U.Values)}, p NaN={NanCount(testCase.Fields.P.Values)}")

        ' ------------------------------------------------------------------
        ' 4. 推进
        ' ------------------------------------------------------------------
        Sub_($"4. 推进 {steps} 步")

        ' 缓存若干步的快照，用于观察流线形态随收敛的演化
        Dim snapSteps As New List(Of Integer) From {1, Math.Max(1, steps \ 4), Math.Max(1, steps \ 2), steps}

        If _snapOverride IsNot Nothing Then snapSteps = _snapOverride
        Dim snapshots As New List(Of Tuple(Of Integer, BoxSnapshot))

        For Each s As Integer In snapSteps.Distinct().OrderBy(Function(x) x)
            Dim need As Integer = s - testCase.StepCount

            If need > 0 Then
                Dim t As Stopwatch = Stopwatch.StartNew()
                Call testCase.Run(need)
                t.Stop()

                Call Line($"  step {testCase.StepCount,5}：{t.Elapsed.TotalSeconds:F2} s " &
                          $"({t.Elapsed.TotalMilliseconds / need:F1} ms/step)，" &
                          $"res={LastResidual(testCase):E3}，cont={Continuity(testCase):E3}，" &
                          $"Cd={If(testCase.LastForces IsNot Nothing, testCase.LastForces.Cd, 0):G5}")
            End If

            snapshots.Add(Tuple.Create(testCase.StepCount, sampler.Sample(testCase)))
        Next

        Call Line("")
        Call Line(testCase.Report.TrimEnd())

        Dim final As BoxSnapshot = snapshots(snapshots.Count - 1).Item2

        ' ------------------------------------------------------------------
        ' 5. 重采样速度场质量
        ' ------------------------------------------------------------------
        Sub_("5. 重采样速度场质量（决定流线是否可能弯曲）")

        Call Line($"  快照：{final.Nx}×{final.Ny}×{final.Nz}，MaxSpeed={final.MaxSpeed:F4} m/s")

        Dim stats As FieldStats = FieldStats.Compute(final)

        Call Line($"  流体节点数        = {stats.Active}")
        Call Line($"  |u|  min/mean/max = {stats.SpeedMin:F4} / {stats.SpeedMean:F4} / {stats.SpeedMax:F4} m/s")
        Call Line($"  Ux   min/mean/max = {stats.UxMin:F4} / {stats.UxMean:F4} / {stats.UxMax:F4} m/s")
        Call Line($"  Uy   min/mean/max = {stats.UyMin:F4} / {stats.UyMean:F4} / {stats.UyMax:F4} m/s")
        Call Line($"  Uz   min/mean/max = {stats.UzMin:F4} / {stats.UzMean:F4} / {stats.UzMax:F4} m/s")
        Call Line("")
        Call Line($"  【零阶重采样证据】相邻结构格节点的速度差（分片常数 ⇒ 多数为 0）：")
        Call Line($"    |ΔUx| 沿 +x: 平均 {stats.dUxX:F4}，为 0 的比例 {100.0 * stats.ZeroUxX:F2}%")
        Call Line($"    |ΔUy| 沿 +x: 平均 {stats.dUyX:F4}，为 0 的比例 {100.0 * stats.ZeroUyX:F2}%")
        Call Line($"    |ΔUz| 沿 +x: 平均 {stats.dUzX:F4}，为 0 的比例 {100.0 * stats.ZeroUzX:F2}%")
        Call Line($"    |ΔUx| 沿 +y: 为 0 的比例 {100.0 * stats.ZeroUxY:F2}%")
        Call Line($"    |ΔUx| 沿 +z: 为 0 的比例 {100.0 * stats.ZeroUxZ:F2}%")
        Call Line("")
        Call Line($"  流向速度亏损（|Ux| < 0.9·U∞ 的流体节点）= {stats.SlowCount} " &
                  $"（{100.0 * stats.SlowCount / Math.Max(1, stats.Active):F2}%）")
        Call Line($"  横向速度显著（|Uy| 或 |Uz| > 1 m/s）的流体节点 = {stats.CrossCount} " &
                  $"（{100.0 * stats.CrossCount / Math.Max(1, stats.Active):F2}%）")

        ' ------------------------------------------------------------------
        ' 6. 流线诊断（WindTunnelBox 实际使用的路径）
        ' ------------------------------------------------------------------
        Sub_("6. 流线追踪（WindTunnelCanvas.RefreshField 路径 + StreamlineTracer 真身）")

        Dim allReports As New List(Of TraceReport)

        For Each tup As Tuple(Of Integer, BoxSnapshot) In snapshots
            Dim rep As TraceReport = TraceAndAnalyze($"step={tup.Item1} 零阶（当前实现）", tup.Item2, 4, 0.7F, 0.0F)
            allReports.Add(rep)
        Next

        Dim finalReport As TraceReport = allReports(allReports.Count - 1)

        ' ------------------------------------------------------------------
        ' 7. 对照实验
        ' ------------------------------------------------------------------
        Sub_("7. 对照实验：把『零阶分片常数』换成『节点插值 + 平滑』后流线是否还是直线")

        Dim smooth3 As BoxSnapshot = final.Smoothed(3)
        Dim smooth9 As BoxSnapshot = final.Smoothed(9)
        Dim smooth20 As BoxSnapshot = final.Smoothed(20)

        Call TraceAndAnalyze("step=final 平滑3轮", smooth3, 4, 0.7F, 0.0F)
        Call TraceAndAnalyze("step=final 平滑9轮", smooth9, 4, 0.7F, 0.0F)
        Call TraceAndAnalyze("step=final 平滑20轮", smooth20, 4, 0.7F, 0.0F)

        Call Line("")
        Call Line("  【步长敏感性】同一条零阶场，仅把 RK2 弧长步长从 0.7 缩小到 0.2：")

        Call TraceAndAnalyze("step=final 零阶 h=0.2", final, 4, 0.2F, 0.0F)

        Call Line("")
        Call Line("  【重采样方式对照】注意：下面用的是**最终帧**的速度场（与上面各 step 不同）；")
        Call Line("  同一帧、同一流线追踪器，只把『最近单元（零阶）』")
        Call Line("  换成『单元中心反距离平方插值』，看 Straightness 是否显著变化：")

        For Each tup As Tuple(Of Integer, BoxSnapshot) In snapshots
            Dim interp As BoxSnapshot = sampler.SampleInterpolated(testCase)
            Dim oc As Integer = 0

            For q As Integer = 0 To interp.Outside.Length - 1
                If interp.Outside(q) <> 0 Then oc += 1
            Next


            Call TraceAndAnalyze($"step={tup.Item1} 反距离插值重采样", interp, 4, 0.7F, 0.0F)
        Next

        Call Line("")
        Call Line("  【阈值敏感性】同一条零阶场，种子步长 8（种子更稀）：")

        Call TraceAndAnalyze("step=final 零阶 seedStride=8", final, 8, 0.7F, 0.0F)

        ' ------------------------------------------------------------------
        ' 8. WindTunnel CLI 路径交叉验证
        ' ------------------------------------------------------------------
        Sub_("8. 交叉验证：WindTunnel.vbproj CLI 路径（AutoConfigure 重采样 + .vti）")

        Call CrossCheckCli(zipPath, mesh, testCase)

        ' ------------------------------------------------------------------
        ' 9. 结论
        ' ------------------------------------------------------------------
        Sub_("9. 结论")

        Dim straightHidden As Integer = 0
        Dim straightTotal As Integer = 0

        For Each pl As StreamlinePolyline In finalReport.StreamSet.Polylines
            straightTotal += 1

            If pl.Straightness < StreamlineSet.DefaultStraightness Then
                straightHidden += 1
            End If
        Next

        Call Line($"  WindTunnelBox 最终帧：种子 {finalReport.StreamSet.SeedCount} 个 → 有效流线 {straightTotal} 条，" &
                  $"总线段 {finalReport.StreamSet.SegmentCount} 段")
        Call Line($"  Straightness 分布：min={finalReport.MinStraightness:G6}, " &
                  $"median={finalReport.MedianStraightness:G6}, mean={finalReport.MeanStraightness:G6}, " &
                  $"max={finalReport.MaxStraightness:G6}")
        Call Line($"  低于 StreamlineSet.DefaultStraightness ({StreamlineSet.DefaultStraightness}) 的流线：" &
                  $"{straightHidden} / {straightTotal}")
        Call Line($"  HideStraightStreamlines=True 时 BuildLines 输出线段数 = {finalReport.KeptSegments} " &
                  $"，光点粒子数 = {finalReport.ParticleCount}")
        Call Line($"")

        If finalReport.KeptSegments = 0 Then
            Call Line("  => 启用直线过滤后【画布上一条流线都没有】，与用户观察到的『流线图一片空白』一致。")
        Else
            Call Line("  => 启用直线过滤后仍有可见线段。")
        End If

        Call Line($"")
        Call Line("  判读要点（对照第 6 / 7 节）：")
        Call Line("    · 若零阶、连续插值、平滑、缩小步长 四种手段都推不过 0.05，")
        Call Line("      则根因是【速度场本身近乎均匀】—— 网格太粗（建筑不可分辨）。")
        Call Line("    · 若只有零阶路径推不过，而连续插值/平滑能过，")
        Call Line("      则根因是【EulerianFieldSampler 的最近单元零阶重采样使速度场分片常数】。")
        Return 0
    End Function

    Private Function LastResidual(c As EulerianWindTunnelCase) As Double
        If c.Incompressible Is Nothing Then Return 0

        Dim h As List(Of Double) = c.Incompressible.ResidualHistory

        If h Is Nothing OrElse h.Count = 0 Then Return 0

        Return h(h.Count - 1)
    End Function

    Private Function Continuity(c As EulerianWindTunnelCase) As Double
        If c.Incompressible Is Nothing Then Return 0
        Return c.Incompressible.ContinuityError
    End Function

    Private Function NanCount(a As Double()) As Integer
        If a Is Nothing Then Return -1

        Dim n As Integer = 0

        For i As Integer = 0 To a.Length - 1
            If Double.IsNaN(a(i)) OrElse Double.IsInfinity(a(i)) Then n += 1
        Next

        Return n
    End Function

    Private Function Median(a As Double()) As Double
        Dim b(a.Length - 1) As Double
        Array.Copy(a, b, a.Length)
        Array.Sort(b)
        Return b(b.Length \ 2)
    End Function

    ' ============================================================================
    '  [D] 求解稳定性对照矩阵
    ' ============================================================================

    ''' <summary>一组求解参数（覆盖 GUI 默认值）。</summary>
    Private Class CaseSpec

        Public Title As String
        Public RelaxU As Double = 0.7
        Public RelaxP As Double = 0.3
        Public PseudoCourant As Double = 0.5
        Public TurbLengthScale As Double = 0.0        ' 0 = 用 EulerianWindTunnelOptions 默认值 0.05
        Public Turb As WindTunnelTurbulence = WindTunnelTurbulence.KEpsilon
        Public UseGpu As Boolean = True
        Public Scheme As EulerianConvectionScheme = EulerianConvectionScheme.Upwind
        Public Transient As Boolean = False
        Public InletSpeed As Double = 30.0
        Public PseudoOverride As Boolean = False
        Public PressureIterations As Integer = 200
        Public PressureTolerance As Double = 1.0E-9
        Public PressureRelTol As Double = 0.01

    End Class

    Private Function RunCompare(zipPath As String, steps As Integer, args As String()) As Integer
        Call Section($"[D] 求解稳定性对照矩阵：{IO.Path.GetFileName(zipPath)}（每例 {steps} 步）")

        If Not File.Exists(zipPath) Then
            Call Line($"[FAIL] 找不到网格包：{zipPath}")
            Return 1
        End If

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Call Line(mesh.Diagnostics.TrimEnd())

        ' ---- 对照矩阵 ----
        Dim cases As New List(Of CaseSpec) From {
            New CaseSpec With {.Title = "① GUI 默认（基线：k-ε, RelaxU=0.7, L=0.05, 伪瞬态 Co=0.5, GPU）"},
            New CaseSpec With {.Title = "② 层流（Turbulence=Laminar）",
                               .Turb = WindTunnelTurbulence.Laminar},
            New CaseSpec With {.Title = "③ 关闭 GPU（CPU double 路径）", .UseGpu = False},
            New CaseSpec With {.Title = "④ 湍流长度尺度 = 域尺度自动值（≈115 m 而非 0.05 m）",
                               .TurbLengthScale = 115.0R},
            New CaseSpec With {.Title = "⑤ 湍流长度尺度 = 0.05 → 2000 m 量级（= 域尺寸）",
                               .TurbLengthScale = 2000.0R},
            New CaseSpec With {.Title = "⑥ 速度欠松弛 0.3（更稳）", .RelaxU = 0.3R},
            New CaseSpec With {.Title = "⑦ 速度欠松弛 0.9（更激进）", .RelaxU = 0.9R},
            New CaseSpec With {.Title = "⑧ 压力欠松弛 0.1", .RelaxP = 0.1R},
            New CaseSpec With {.Title = "⑨ 压力欠松弛 0.7", .RelaxP = 0.7R},
            New CaseSpec With {.Title = "⑩ 关闭伪瞬态（Co=0）", .PseudoCourant = 0.0R, .PseudoOverride = True},
            New CaseSpec With {.Title = "⑪ 伪瞬态 Co=0.1", .PseudoCourant = 0.1R, .PseudoOverride = True},
            New CaseSpec With {.Title = "⑫ 来流 5 m/s（低雷诺数）", .InletSpeed = 5.0R},
            New CaseSpec With {.Title = "⑬ 稳态 → 瞬态 PIMPLE", .Transient = True},
            New CaseSpec With {.Title = "⑭ 一阶迎风 → 中心/高阶（若支持）",
                               .Scheme = EulerianConvectionScheme.Upwind},
            New CaseSpec With {.Title = "⑮ CPU double + 压力迭代 2000", .UseGpu = False,
                               .PressureIterations = 2000},
            New CaseSpec With {.Title = "⑯ GPU + 压力迭代 5000", .PressureIterations = 5000},
            New CaseSpec With {.Title = "⑰ CPU + 压力迭代 2000 + RelaxU=0.5", .UseGpu = False,
                               .PressureIterations = 2000, .RelaxU = 0.5R},
            New CaseSpec With {.Title = "⑱ CPU + 压力迭代 2000 + 压力容差 1e-6", .UseGpu = False,
                               .PressureIterations = 2000, .PressureTolerance = 0.000001R},
            New CaseSpec With {.Title = "⑲ CPU + 压力迭代 2000 + 层流", .UseGpu = False,
                               .PressureIterations = 2000, .Turb = WindTunnelTurbulence.Laminar}}

        Call Line("")
        Call Line("  例                                          max|U|        residual     continuity    NaN  流线(直线度 max)  用时")
        Call Line("  " & New String("-"c, 130))

        Dim onlyNew As Boolean = args.Contains("only-new")

        For Each spec As CaseSpec In cases
            If onlyNew AndAlso Not spec.Title.StartsWith("⑮") AndAlso Not spec.Title.StartsWith("⑯") AndAlso
               Not spec.Title.StartsWith("⑰") AndAlso Not spec.Title.StartsWith("⑱") AndAlso
               Not spec.Title.StartsWith("⑲") Then
                Continue For
            End If

            Call RunOneCase(mesh, spec, steps)
        Next

        Return 0
    End Function

    Private Sub RunOneCase(mesh As EulerianFvMesh, spec As CaseSpec, steps As Integer)
        Dim title As String = If(spec.Title.Length > 42, spec.Title.Substring(0, 42), spec.Title.PadRight(42))

        Try
            Dim opts As New EulerianWindTunnelOptions With {
                .InletSpeed = spec.InletSpeed,
                .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
                .Density = 1.225R,
                .KinematicViscosity = 0.000015R,
                .Turbulence = spec.Turb,
                .TurbulenceIntensity = 0.02R,
                .Compressible = False,
                .UseGpu = spec.UseGpu AndAlso _gpuReady,
                .ReferenceArea = 1.0R,
                .PressureMaxIterations = spec.PressureIterations,
                .Transient = spec.Transient,
                .Scheme = spec.Scheme,
                .RelaxU = spec.RelaxU}

            If spec.TurbLengthScale > 0 Then opts.TurbulenceLengthScale = spec.TurbLengthScale

            Dim sw As Stopwatch = Stopwatch.StartNew()
            Dim testCase As New EulerianWindTunnelCase(mesh, opts)

            Call testCase.Configure()

            ' 覆盖伪瞬态 Courant（EulerianWindTunnelOptions 未暴露 → 直接改求解器选项）
            If spec.PseudoOverride AndAlso testCase.Incompressible IsNot Nothing Then
                testCase.Incompressible.Options.PseudoTransientCourant = spec.PseudoCourant
            End If

            If testCase.Incompressible IsNot Nothing Then
                testCase.Incompressible.Options.RelaxP = spec.RelaxP
                testCase.Incompressible.Options.PressureTolerance = spec.PressureTolerance
            End If

            Call testCase.Run(steps)
            sw.Stop()

            Dim u As Double() = testCase.Fields.U.Values
            Dim n As Integer = mesh.CellCount
            Dim umax As Double = 0, nan As Integer = 0

            For c As Integer = 0 To n - 1
                Dim s2 As Double = u(c) * u(c) + u(n + c) * u(n + c) + u(2 * n + c) * u(2 * n + c)

                If Double.IsNaN(s2) OrElse Double.IsInfinity(s2) Then
                    nan += 1
                ElseIf s2 > umax Then
                    umax = s2
                End If
            Next

            ' 流线形态（用与 WindTunnelBox 相同的零阶重采样）
            Dim sampler As New BoxSampler(mesh, 250000)
            Dim snap As BoxSnapshot = sampler.Sample(testCase)
            Dim straightText As String = "n/a"

            Try
                Dim set1 As StreamlineSet = StreamlineTracer.Trace(New DiagFlowField(snap), New StreamlineOptions With {
                    .SeedStride = 4, .StepLength = 0.7F, .MaxSteps = 3000})

                Dim smax As Double = 0, smin As Double = 1.0E+30, cnt As Integer = 0

                For Each pl As StreamlinePolyline In set1.Polylines
                    If pl Is Nothing OrElse pl.VertexCount < 2 Then Continue For

                    smax = Math.Max(smax, pl.Straightness)
                    smin = Math.Min(smin, pl.Straightness)
                    cnt += 1
                Next

                straightText = $"n={cnt} min={smin:G3} max={smax:G3}"
            Catch ex As Exception
                straightText = $"ERR {ex.Message}"
            End Try

            Dim res As Double = LastResidual(testCase)

            Dim preport As String = If(testCase.Incompressible IsNot Nothing,
                                       testCase.Incompressible.PressureReport.Iterations.ToString(),
                                       "n/a")

            Call Line($"  {title} {Math.Sqrt(umax),-13:E3} {res,-13:E3} {Continuity(testCase),-13:E3} " &
                      $"{nan,-5} {straightText,-24} pIt={preport,-6} {sw.Elapsed.TotalSeconds:F1} s")

        Catch ex As Exception
            Call Line($"  {title} 异常：{ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    Private Function IsNumeric(s As String) As Boolean
        Dim v As Double
        Return Double.TryParse(s, NumberStyles.Float, CI, v)
    End Function

    ' ============================================================================
    '  [J] 求解参数扫描（找可收敛的参数组合）
    ' ============================================================================

    Private Function RunParamScan(zipPath As String, steps As Integer, args As String()) As Integer
        Call Section($"[J] 求解参数扫描：{IO.Path.GetFileName(zipPath)}（每例 {steps} 步）")

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)

        Dim cases As New List(Of String())
        ' 每项：{标题, prIter, prTol, prRelTol, momSweeps, relaxU}
        cases.Add(New String() {"基线 prIter=200 tol=1e-9 rel=0.01 sweep=2 U=0.7", "200", "1e-9", "0.01", "2", "0.7"})
        cases.Add(New String() {"prIter=2000 tol=1e-9 rel=0.01 sweep=2 U=0.7", "2000", "1e-9", "0.01", "2", "0.7"})
        cases.Add(New String() {"prIter=2000 tol=1e-6 rel=0.01 sweep=2 U=0.7", "2000", "1e-6", "0.01", "2", "0.7"})
        cases.Add(New String() {"prIter=2000 tol=1e-6 rel=0.0 sweep=2 U=0.7", "2000", "1e-6", "0", "2", "0.7"})
        cases.Add(New String() {"prIter=2000 tol=1e-6 rel=0.0 sweep=10 U=0.7", "2000", "1e-6", "0", "10", "0.7"})
        cases.Add(New String() {"prIter=2000 tol=1e-6 rel=0.0 sweep=10 U=0.5", "2000", "1e-6", "0", "10", "0.5"})
        cases.Add(New String() {"prIter=2000 tol=1e-6 rel=0.0 sweep=20 U=0.3", "2000", "1e-6", "0", "20", "0.3"})
        cases.Add(New String() {"prIter=5000 tol=1e-8 rel=0.0 sweep=10 U=0.7", "5000", "1e-8", "0", "10", "0.7"})
        cases.Add(New String() {"prIter=5000 tol=1e-8 rel=0.0 sweep=20 U=0.5", "5000", "1e-8", "0", "20", "0.5"})

        Call Line("")
        Call Line("  例                                                       max|U|         cont          pIt    用时")

        For Each spec As String() In cases
            Try
                Dim opts As New EulerianWindTunnelOptions With {
                    .InletSpeed = 30.0R,
                    .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
                    .Density = 1.225R,
                    .KinematicViscosity = 0.000015R,
                    .Turbulence = WindTunnelTurbulence.Laminar,
                    .Compressible = False,
                    .UseGpu = _gpuReady,
                    .PressureMaxIterations = Integer.Parse(spec(1), CI),
                    .RelaxU = Double.Parse(spec(5), CI)}

                Dim tc As New EulerianWindTunnelCase(mesh, opts)

                Call tc.Configure()

                If tc.Incompressible IsNot Nothing Then
                    tc.Incompressible.Options.PressureTolerance = Double.Parse(spec(2), CI)
                    tc.Incompressible.Options.MomentumSweeps = Integer.Parse(spec(4), CI)
                End If

                Dim sw As Stopwatch = Stopwatch.StartNew()

                Call tc.Run(steps)
                sw.Stop()

                Dim um As Double = SafeMax(tc.Fields.U.Values)
                Dim pit As String = If(tc.Incompressible IsNot Nothing,
                                       tc.Incompressible.PressureReport.Iterations.ToString(), "n/a")

                Call Line($"  {spec(0).PadRight(52)} {um,-13:E3} {Continuity(tc),-13:E3} {pit,-6} {sw.Elapsed.TotalSeconds:F1} s")

            Catch ex As Exception
                Call Line($"  {spec(0).PadRight(52)} 异常 {ex.GetType().Name}: {ex.Message}")
            End Try
        Next

        Return 0
    End Function

    ' ============================================================================
    '  [I] 压力修正方向性探针（修正后散度是否下降）
    ' ============================================================================

    Private Function RunPcorrProbe(zipPath As String, steps As Integer) As Integer
        Call Section($"[I] 压力修正方向性探针：{IO.Path.GetFileName(zipPath)}")

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = 30.0R,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.225R,
            .KinematicViscosity = 0.000015R,
            .Turbulence = WindTunnelTurbulence.Laminar,
            .Compressible = False,
            .UseGpu = False,
            .PressureMaxIterations = 200}

        Dim testCase As New EulerianWindTunnelCase(mesh, opts)
        Call testCase.Configure()

        Dim inc As IncompressibleSolver = testCase.Incompressible
        Dim n As Integer = mesh.CellCount
        Dim p As EulerianField = testCase.Fields.P

        Call testCase.Run(Math.Max(1, steps))

        ' 反射取 private 数组
        Dim rauFace As Double() = PrivateArray(inc, "_rauFace")
        Dim phiH As Double() = PrivateArray(inc, "_phiHbyA")
        Dim flux As Double() = PrivateArray(inc, "_flux")

        Call Line($"  推进 {testCase.StepCount} 步后，max|U| = {SafeMax(testCase.Fields.U.Values):E3}")

        ' ---- A) phiHbyA 的散度（压力方程右端）----
        Dim divPhiH(n - 1) As Double

        Call EulerianOperators.FvcDiv(mesh, phiH, divPhiH)

        Call Line($"  div(phiHbyA)  maxAbs = {MaxAbs(divPhiH):E4}   ← 压力方程右端 b")

        ' ---- B) 反解压力方程（大迭代上限 + 紧容差），看它能否把散度降到 0 ----
        Dim mP As New FvMatrix(mesh, "p")

        mP.Symmetric = True

        Call EulerianOperators.FvmLaplacian(mP, p, 0, rauFace, sign:=-1.0)
        Call mP.AddSourceField(divPhiH)

        Dim hasFixed As Boolean = False

        For pi As Integer = 0 To mesh.PatchCount - 1
            If TypeOf p.Boundaries(pi).Condition Is FixedValueBc Then hasFixed = True
        Next

        If Not hasFixed Then Call mP.SetReference(0, 0.0)

        ' 从 0 压力起步反解
        Dim pr0 As Double() = New Double(n - 1) {}

        Call p.UpdateBoundaries()

        Dim x As Double() = Nothing
        Dim rpt As EulerianSolverReport = EulerianLinearSolve.Solve(
            mP, x, EulerianLinearSolverKind.Pcg, 1.0E-12, 0.0, 20000, 0, False)

        Call Line($"  反解压力方程（maxIter=20000, tol=1e-12）：{rpt.ToString()}")

        ' ---- C) 用反解出的 p 做通量修正：φ = phiHbyA − rAU·Δ·(p_Q − p_P) ----
        Dim corr(mesh.FaceCount - 1) As Double
        Dim lapFlux(mesh.FaceCount - 1) As Double

        ' 先把解写回场，LaplacianFaceFlux 才能看到
        Call p.SetFromComponent(0, x)
        Call p.UpdateBoundaries()
        Call EulerianOperators.LaplacianFaceFlux(mesh, rauFace, p, 0, lapFlux)

        For f As Integer = 0 To corr.Length - 1
            corr(f) = phiH(f) - lapFlux(f)
        Next

        Dim divCorr(n - 1) As Double

        Call EulerianOperators.FvcDiv(mesh, corr, divCorr)

        ' 同时用真实残差检查矩阵方程满足度
        Call Line($"  修正后 div(φ)  maxAbs = {MaxAbs(divCorr):E4}")
        Call Line($"  修正前 div(phiHbyA) maxAbs = {MaxAbs(divPhiH):E4}")
        Call Line($"  → 下降倍数 = {MaxAbs(divPhiH) / Math.Max(1.0E-300, MaxAbs(divCorr)):E3}")

        ' 符号检验：把修正反号
        Dim wrong(mesh.FaceCount - 1) As Double

        For f As Integer = 0 To wrong.Length - 1
            wrong(f) = phiH(f) + lapFlux(f)
        Next

        Dim divWrong(n - 1) As Double

        Call EulerianOperators.FvcDiv(mesh, wrong, divWrong)

        Call Line($"  若把修正反号 div(φ) maxAbs = {MaxAbs(divWrong):E4}（应远大于修正后，否则符号反了）")

        Return 0
    End Function

    Private Function MaxAbs(a As Double()) As Double
        Dim m As Double = 0

        For i As Integer = 0 To a.Length - 1
            If Math.Abs(a(i)) > m Then m = Math.Abs(a(i))
        Next

        Return m
    End Function

    ' ============================================================================
    '  [H] 网格散度-自由性探针（均匀通量应给出零散度）
    ' ============================================================================

    Private Function RunDivergenceProbe(zipPath As String) As Integer
        Call Section($"[H] 网格散度-自由性探针：{IO.Path.GetFileName(zipPath)}")

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Dim n As Integer = mesh.CellCount
        Dim nF As Integer = mesh.FaceCount
        Dim sf As EulerianVertex() = mesh.Sf

        ' ---- 1) 均匀 x 通量 F_f = Sf.X * 30 ----
        Dim flux(nF - 1) As Double

        For f As Integer = 0 To nF - 1
            flux(f) = sf(f).X * 30.0R
        Next

        Dim d1(n - 1) As Double

        Call EulerianOperators.FvcDiv(mesh, flux, d1)

        Dim m1 As Double = 0

        For c As Integer = 0 To n - 1
            m1 = Math.Max(m1, Math.Abs(d1(c)))
        Next

        Call Line($"  1) 均匀 x 通量 F=Sf.X*30：Σ|Sf.X| 参考，maxAbs(div) = {m1:E4}")

        If m1 > 0 Then
            Call Line($"     相对量级：|flux|max*30 ≈ {MaxAbsSf(sf):E4}  → 相对残差 = {m1 / Math.Max(1.0E-300, MaxAbsSf(sf)):E3}")
        End If

        ' ---- 2) 闭合性检验：Σ_f Sf = 0（单元闭合）----
        Dim worstClose As Double = 0
        Dim worstCell As Integer = -1

        For c As Integer = 0 To n - 1
            Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
            Dim cf As Integer() = mesh.CellFaces(c)

            For k As Integer = 0 To cf.Length - 1
                Dim f As Integer = cf(k)
                Dim sign As Double = If(mesh.Mesh.Faces(f).Owner = c, 1.0R, -1.0R)

                sx += sign * sf(f).X : sy += sign * sf(f).Y : sz += sign * sf(f).Z
            Next

            Dim mag As Double = Math.Sqrt(sx * sx + sy * sy + sz * sz)

            If mag > worstClose Then
                worstClose = mag : worstCell = c
            End If
        Next

        Call Line($"  2) Σ_f Sf（单元闭合性）：max|Σ Sf| = {worstClose:E4} @ cell {worstCell}" &
                  $"（应恒为 0；非 0 说明面法向/绕序不一致）")

        ' ---- 3) 每个 patch 的通量和与面积向量 ----
        Call Line("")
        Call Line("  3) 各 patch 面积向量和（应指向域外、量值 ≈ patch 面积）：")

        For pi As Integer = 0 To mesh.PatchCount - 1
            Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
            Dim sx As Double = 0, sy As Double = 0, sz As Double = 0, area As Double = 0

            For k As Integer = 0 To geo.FaceCount - 1
                sx += geo.Sf(k).X : sy += geo.Sf(k).Y : sz += geo.Sf(k).Z
                area += mesh.Mesh.FaceAreas(geo.FaceIndices(k))
            Next

            Call Line($"     {geo.Name,-14} faces={geo.FaceCount,5}  Σ Sf = ({sx,12:E3}, {sy,12:E3}, {sz,12:E3})  面积={area:E4}")
        Next

        ' ---- 4) 一个内部单元的逐面通量明细 ----
        Dim probe As Integer = EulerianDiagnostics.NearestCell(mesh, -500.8R + 16.0R * 32.866R, 800.0R, 90.0R)
        Call Line("")
        Call Line($"  4) 内部单元 #{probe} 的逐面 F = Sf.X*30（应求和为 0）：")

        Dim cf2 As Integer() = mesh.CellFaces(probe)
        Dim tot As Double = 0

        For k As Integer = 0 To cf2.Length - 1
            Dim f As Integer = cf2(k)
            Dim owner As Integer = mesh.Mesh.Faces(f).Owner
            Dim sign As Double = If(owner = probe, 1.0R, -1.0R)
            Dim contrib As Double = sign * sf(f).X * 30.0R

            tot += contrib

            Call Line($"     face {f,6} {If(owner = probe, "owner", "neigh")}  Sf=({sf(f).X,10:E3},{sf(f).Y,10:E3},{sf(f).Z,10:E3})  contrib={contrib,13:E4}")
        Next

        Call Line($"     Σ contrib = {tot:E4}（应 = 0）")

        ' ---- 5) 求解器初始化后的 _flux 是否散度自由（均匀 30 m/s 应给出 0）----
        Call Line("")
        Call Line("  5) 求解器 Initialize() 后的 _flux 散度（均匀 30 m/s 应 ≈ 0）：")

        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = 30.0R,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.225R,
            .KinematicViscosity = 0.000015R,
            .Turbulence = WindTunnelTurbulence.Laminar,
            .Compressible = False,
            .UseGpu = False}

        Dim tc As New EulerianWindTunnelCase(mesh, opts)

        Call tc.Configure()

        Dim inc As IncompressibleSolver = tc.Incompressible
        Dim f0 As Double() = PrivateArray(inc, "_flux")
        Dim d0(n - 1) As Double

        ' ---- 哨兵测试：确认反射拿到的 _flux 就是求解器使用的那个数组 ----
        Dim sentinel As Double = -12345.6789
        Dim savedF As Double() = CType(f0.Clone(), Double())

        f0(0) = sentinel

        Dim reRead As Double() = PrivateArray(inc, "_flux")

        Call Line($"     [哨兵] 写入 _flux(0)={sentinel}，重读 = {reRead(0)}（相等则反射数组即求解器数组）")
        Array.Copy(savedF, f0, f0.Length)

        ' ---- 显式重新 SetOutlet 后再算一次面通量（验证 BC 是否影响）----
        Dim fBC(mesh.FaceCount - 1) As Double

        Call tc.Fields.U.SetZeroGradient("outlet")
        Call tc.Fields.U.UpdateBoundaries()
        Call EulerianOperators.FaceVolumetricFlux(tc.Fields.U, fBC)

        Dim piOutB As Integer = mesh.FindPatch("outlet")

        If piOutB >= 0 Then
            Call Line($"     [BC 复现] SetZeroGradient(outlet) 后 face0 通量 = {fBC(mesh.Patches(piOutB).FaceIndices(0)):E3}" &
                      $"（若 = 1.701E4 说明该 BC 本身没问题）")
        End If

        ' ---- 直接复刻 FluxFromVectorField 的 outlet 分支 ----
        Dim piOut As Integer = mesh.FindPatch("outlet")

        If piOut >= 0 Then
            Dim geoOut As EulerianPatchGeometry = mesh.Patches(piOut)
            Dim pfOut As EulerianPatchField = tc.Fields.U.Boundaries(piOut)
            Dim bcOut As EulerianBoundaryCondition = pfOut.Condition

            Call Line($"     outlet: bc={If(bcOut Is Nothing, "(Nothing)", bcOut.GetType().Name)} " &
                      $"PrescribesVelocity={If(bcOut Is Nothing, "?", bcOut.PrescribesVelocity.ToString())}")

            For k As Integer = 0 To Math.Min(2, geoOut.FaceCount - 1)
                Dim f As Integer = geoOut.FaceIndices(k)
                Dim own As Integer = geoOut.OwnerCells(k)
                Dim s As EulerianVertex = geoOut.Sf(k)
                Dim uxOwn As Double = tc.Fields.U.Values(own)

                ' 顶点法独立复算该面的 Sf（验证 m.Sf(f) 是否可靠）
                Dim eFace As Moira.CFDEngine.Eulerian.EulerianFace = mesh.Mesh.Faces(f)
                Dim nv As Integer = eFace.PointIndices.Count
                Dim sxx As Double = 0, syy As Double = 0, szz As Double = 0

                For v As Integer = 0 To nv - 1
                    Dim p1 As EulerianVertex = mesh.Mesh.Points(eFace.PointIndices(v))
                    Dim p2 As EulerianVertex = mesh.Mesh.Points(eFace.PointIndices((v + 1) Mod nv))

                    sxx += (p1.Y - p2.Y) * (p1.Z + p2.Z)
                    syy += (p1.Z - p2.Z) * (p1.X + p2.X)
                    szz += (p1.X - p2.X) * (p1.Y + p2.Y)
                Next

                sxx *= 0.5 : syy *= 0.5 : szz *= 0.5

                Call Line($"       face#{f} own=#{own} Sf.X={s.X:E3} 顶点法 Sf.X={sxx:E3} " &
                          $"Ux(own)={uxOwn:E3} 实际 _flux={f0(f):E3}")
            Next
        End If

        ' 检查 IncompressibleSolver 是否真的被 Initialize 过：重算一次面通量对比
        Dim fRef(mesh.FaceCount - 1) As Double

        Call EulerianOperators.FaceVolumetricFlux(tc.Fields.U, fRef)

        Dim same As Boolean = True
        Dim diffCnt As Integer = 0

        For f As Integer = 0 To fRef.Length - 1
            If Math.Abs(fRef(f) - f0(f)) > 1.0E-9 Then same = False : diffCnt += 1
        Next

        Call Line($"     重算 FaceVolumetricFlux 与 _flux 是否一致：{same}（差异面数 {diffCnt}）")

        Dim frMax As Double = 0

        For f As Integer = 0 To fRef.Length - 1
            frMax = Math.Max(frMax, Math.Abs(fRef(f)))
        Next

        Call Line($"     重算 max|F| = {frMax:E3}")

        For Each pn As String In New String() {"inlet", "outlet"}
            Dim pi2 As Integer = mesh.FindPatch(pn)

            If pi2 >= 0 Then
                Dim geo2 As EulerianPatchGeometry = mesh.Patches(pi2)

                Call Line($"       patch {pn}: 重算净通量={EulerianOperators.PatchFlux(mesh, fRef, pi2):E3}  " &
                          $"实际净通量={EulerianOperators.PatchFlux(mesh, f0, pi2):E3}  " &
                          $"重算 face0={fRef(geo2.FaceIndices(0)):E3}  实际 face0={f0(geo2.FaceIndices(0)):E3}")
            End If
        Next

        Call EulerianOperators.FvcDiv(mesh, f0, d0)

        Dim myD0 As Double = MaxAbs(d0)
        Dim f0max As Double = 0

        For f As Integer = 0 To f0.Length - 1
            f0max = Math.Max(f0max, Math.Abs(f0(f)))
        Next

        Call Line($"     配置后 _flux: max|F|={f0max:E3}  maxAbs(div)={myD0:E3}  " &
                  $"（若不 ≈0，说明初始化通量本身不满足离散连续性）")

        ' 分 patch 通量（入口/出口应量值相等、符号相反）
        For pi As Integer = 0 To mesh.PatchCount - 1
            Dim s As Double = EulerianOperators.PatchFlux(mesh, f0, pi)
            Call Line($"       patch {mesh.Patches(pi).Name,-14} 净通量 = {s:E3}")
        Next

        ' 速度场是否真的是均匀 30
        Dim u As Double() = tc.Fields.U.Values
        Dim uxmin As Double = Double.MaxValue, uxmax As Double = Double.MinValue

        For c As Integer = 0 To n - 1
            uxmin = Math.Min(uxmin, u(c)) : uxmax = Math.Max(uxmax, u(c))
        Next

        Call Line($"     配置后 Ux 范围 = [{uxmin:E3}, {uxmax:E3}]（应恒为 30）")

        ' ---- 找出散度非零的单元，逐面追踪 ----
        Dim worstC As Integer = -1
        Dim worstV As Double = 0

        For c As Integer = 0 To n - 1
            If Math.Abs(d0(c)) > worstV Then
                worstV = Math.Abs(d0(c)) : worstC = c
            End If
        Next

        Call Line("")
        Dim ctr As EulerianVertex = mesh.CellCentre(worstC)

        Call Line($"     散度最大单元 #{worstC}：div={d0(worstC):E4}  中心=({ctr.X:E4}, {ctr.Y:E4}, {ctr.Z:E4})")

        ' 全局面积异常统计（切割单元/退化面）
        Dim ar As Double() = mesh.Mesh.FaceAreas
        Dim aMin As Double = Double.MaxValue, aMax As Double = Double.MinValue
        Dim tiny As Integer = 0, big As Integer = 0
        Dim expected As Double = 32.866R ^ 2

        For f As Integer = 0 To ar.Length - 1
            aMin = Math.Min(aMin, ar(f)) : aMax = Math.Max(aMax, ar(f))
            If ar(f) < 0.01 * expected Then tiny += 1
            If ar(f) > 10.0 * expected Then big += 1
        Next

        Call Line($"     面面积：min={aMin:E3} max={aMax:E3} 期望≈{expected:E3}；" &
                  $"<1% 期望的面数={tiny}，>10 倍期望={big}")

        ' ---- 统计"仅靠壁面封闭"的死端单元（x 面缺失 ⇒ 均匀来流下必然质量堆积）----
        Dim noDown As Integer = 0, noUp As Integer = 0, sealedX As Integer = 0
        Dim worstCell2 As Integer = -1
        Dim worstImb As Double = 0

        For c As Integer = 0 To n - 1
            Dim hasIn As Boolean = False, hasOut As Boolean = False
            Dim wallX As Integer = 0
            Dim cf4 As Integer() = mesh.CellFaces(c)

            For k As Integer = 0 To cf4.Length - 1
                Dim f As Integer = cf4(k)
                Dim eF As Moira.CFDEngine.Eulerian.EulerianFace = mesh.Mesh.Faces(f)
                Dim sgn As Double = If(eF.Owner = c, 1.0R, -1.0R)
                Dim sx As Double = sgn * mesh.Sf(f).X

                If Math.Abs(sx) < 1.0E-9 Then Continue For

                If eF.Neighbour < 0 Then
                    wallX += 1
                ElseIf sx < 0 Then
                    hasIn = True
                Else
                    hasOut = True
                End If
            Next

            If wallX > 0 AndAlso Not hasOut Then noDown += 1
            If wallX > 0 AndAlso Not hasIn Then noUp += 1

            If wallX >= 2 AndAlso Not hasOut Then
                sealedX += 1

                Dim imb As Double = Math.Abs(d0(c))

                If imb > worstImb Then
                    worstImb = imb : worstCell2 = c
                End If
            End If
        Next

        Call Line("")
        Call Line($"     【死端单元统计】x 向无出流面的单元 = {noDown}（占 {100.0 * noDown / n:F3}%）；" &
                  $"x 向无入流面的单元 = {noUp}")
        Call Line($"       x 向被壁面封死（≥2 个 x 壁面且无出流）的单元 = {sealedX}" &
                  $"（占 {100.0 * sealedX / n:F3}%），其中散度最大 = {worstImb:E3} @ #{worstCell2}")
        Call Line("       （这些单元在均匀来流下必然质量堆积，是压力方程病态/发散的几何根源）")

        If worstC >= 0 Then
            Dim cf3 As Integer() = mesh.CellFaces(worstC)

            For k As Integer = 0 To cf3.Length - 1
                Dim f As Integer = cf3(k)
                Dim eF As Moira.CFDEngine.Eulerian.EulerianFace = mesh.Mesh.Faces(f)
                Dim isB As Boolean = eF.Neighbour < 0
                Dim sign As Double = If(eF.Owner = worstC, 1.0R, -1.0R)
                Dim patchOf As String = ""

                For pi3 As Integer = 0 To mesh.PatchCount - 1
                    If mesh.Patches(pi3).FaceIndices.Contains(f) Then patchOf &= mesh.Patches(pi3).Name & " "
                Next

                Dim sgn As EulerianVertex = mesh.Sf(f)

                Call Line($"       face {f,6} own={eF.Owner,6} nb={eF.Neighbour,6} bnd={isB,-6} " &
                          $"patch=[{patchOf.TrimEnd}] sign={sign,3} " &
                          $"signedSf.X={sign * sgn.X,12:E4} F={f0(f):E4} contrib={sign * f0(f):E4}")
            Next
        End If

        ' 逐 patch：BC 类型 / PrescribesVelocity / 前 3 个面的 U 边界值与算出的通量
        Call Line("")
        Call Line("     逐 patch 边界诊断：")

        For pi As Integer = 0 To mesh.PatchCount - 1
            Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
            Dim pf As EulerianPatchField = tc.Fields.U.Boundaries(pi)
            Dim bc As EulerianBoundaryCondition = pf.Condition

            Dim bcInfo As String = $"       {geo.Name,-14} type={geo.PatchType,-10} " &
                                   $"bc={If(bc Is Nothing, "(Nothing)", bc.GetType().Name),-18} " &
                                   $"prescribes={If(bc IsNot Nothing, bc.PrescribesVelocity.ToString(), "?")}"

            Call Line(bcInfo)

            If geo.FaceCount > 0 Then
                Dim s0 As EulerianVertex = geo.Sf(0)

                Dim vb0 As Double() = pf.Values()
                Dim ub As Double = pf.Value(0, 0)

                Call Line($"           face0: Sf=({s0.X:E3},{s0.Y:E3},{s0.Z:E3})  U_b.x={ub:E3}  " &
                          $"patchValues.len={vb0.Length}  patchValues(0)={vb0(0):E3}  " &
                          $"F_actual={f0(geo.FaceIndices(0)):E3}  " &
                          $"F_expected(owner*Sf)={uxmax * s0.X:E3}")
            End If
        Next

        Return 0
    End Function

    Private Function MaxAbsSf(sf As EulerianVertex()) As Double
        Dim m As Double = 0

        For i As Integer = 0 To sf.Length - 1
            Dim v As Double = Math.Sqrt(sf(i).X ^ 2 + sf(i).Y ^ 2 + sf(i).Z ^ 2)

            If v > m Then m = v
        Next

        Return m
    End Function

    ' ============================================================================
    '  [G] 动量方程对角元探针
    ' ============================================================================

    Private Function RunMomentumDiag(zipPath As String, steps As Integer) As Integer
        Call Section($"[G] 动量方程对角元探针：{IO.Path.GetFileName(zipPath)}")

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = 30.0R,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.225R,
            .KinematicViscosity = 0.000015R,
            .Turbulence = WindTunnelTurbulence.Laminar,
            .Compressible = False,
            .UseGpu = False,
            .PressureMaxIterations = 200}

        Dim testCase As New EulerianWindTunnelCase(mesh, opts)
        Call testCase.Configure()

        ' 用层流 + CPU 隔离变量
        Call Line("  逐步：手工装配动量矩阵 U0（与 MomentumPredictor 逐行等价），读对角元/源项/rau")

        Call Line("")
        Call Line("  step   a_P(min)      a_P(median)    a_P(max)      |b|(L2)        rauCell(min)   rauCell(max)")

        For s As Integer = 1 To Math.Max(1, steps)
            Call testCase.Run(1)

            Dim inc As IncompressibleSolver = testCase.Incompressible
            Dim n As Integer = mesh.CellCount
            Dim rho As Double() = testCase.Fields.Rho.Values
            Dim vol As Double() = mesh.CellVolume
            Dim phi As Double() = inc.Flux
            Dim uComp As Double() = testCase.Fields.U.ToComponent(0)

            Dim m As New FvMatrix(mesh, "U0")

            m.Symmetric = False

            ' pseudo-transient（与源码一致：coeff = rho * 0.5*Σ|rhoPhi| / Co）
            Dim co As Double = 0.5R
            Dim fluxSum(n - 1) As Double

            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                Dim a As Double = Math.Abs(phi(f))

                fluxSum(mesh.InternalOwner(i)) += a
                fluxSum(mesh.InternalNeighbour(i)) += a
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    fluxSum(geo.OwnerCells(k)) += Math.Abs(phi(geo.FaceIndices(k)))
                Next
            Next

            Dim pseudo(n - 1) As Double

            For c As Integer = 0 To n - 1
                pseudo(c) = If(fluxSum(c) > 0, rho(c) * 0.5 * fluxSum(c) / co, 0.0R)
            Next

            For c As Integer = 0 To n - 1
                m.AddDiag(c, pseudo(c))
                m.AddSource(c, pseudo(c) * uComp(c))
            Next

            ' 对流（用 solver 当前的 _rhoPhi 面通量）
            Dim mf As Double() = inc.MassFluxFace
            Dim rhoPhi(mf.Length - 1) As Double

            Array.Copy(mf, rhoPhi, mf.Length)

            Call EulerianOperators.FvmDiv(m, rhoPhi, testCase.Fields.U, 0, EulerianConvectionScheme.Upwind)

            ' 扩散
            Dim gamma As Double() = EulerianOperators.UniformFaceGamma(mesh, opts.Density * opts.KinematicViscosity)

            Call EulerianOperators.FvmLaplacian(m, testCase.Fields.U, 0, gamma, sign:=-1.0)

            Dim de As Integer() = mesh.DiagEntry
            Dim vals(n - 1) As Double

            For c As Integer = 0 To n - 1
                vals(c) = m.Values(de(c))
            Next

            Array.Sort(vals)

            Dim rau As Double() = PrivateArray(inc, "_rauCell")
            Dim rmin As Double = Double.MaxValue, rmax As Double = Double.MinValue
            Dim rauSorted(n - 1) As Double

            Array.Copy(rau, rauSorted, n)
            Array.Sort(rauSorted)

            For c As Integer = 0 To n - 1
                rmin = Math.Min(rmin, rau(c)) : rmax = Math.Max(rmax, rau(c))
            Next

            ' 分位 + 异常单元定位（rAU 大 ⇒ a_P 小 ⇒ 压力方程近奇异）
            Dim p99 As Double = rauSorted(CInt(0.99 * (n - 1)))
            Dim p999 As Double = rauSorted(CInt(0.999 * (n - 1)))
            Dim bigCount As Integer = 0
            Dim firstBig As Integer = -1

            For c As Integer = 0 To n - 1
                If rau(c) > 1.0E-6 Then
                    bigCount += 1

                    If firstBig < 0 Then firstBig = c
                End If
            Next

            Call Line($"        rAU 分位: min={rauSorted(0):E3} p50={rauSorted(n \ 2):E3} p99={p99:E3} p99.9={p999:E3} max={rauSorted(n - 1):E3}" &
                      $"  |  rAU>1e-6 的单元数={bigCount}（首个 #{firstBig}）")

            ' 对最病态的若干单元打印几何/邻接
            Dim order(n - 1) As Integer

            For c As Integer = 0 To n - 1
                order(c) = c
            Next

            Array.Sort(rau, order)
            Array.Reverse(order)
            Array.Reverse(rau)

            For k As Integer = 0 To Math.Min(3, n - 1)
                Dim c As Integer = order(k)
                Dim vols As Double() = mesh.CellVolume
                Dim dmin As Double = Double.MaxValue

                For i As Integer = 0 To mesh.InternalFaceCount - 1
                    If mesh.InternalOwner(i) = c OrElse mesh.InternalNeighbour(i) = c Then
                        dmin = Math.Min(dmin, mesh.DeltaCoeff(i))
                    End If
                Next

                Call Line($"        病态单元 #{c}: rAU={rau(k):E3} (a_P={SafeDiv(1.0, rau(k)):E3})  V={vols(c):E3}  " &
                          $"faces={mesh.CellFaces(c).Length}  min|Delta_f|={dmin:E3}")
            Next

            Call Line($"  {s,4}   {vals(0),-13:E3} {vals(n \ 2),-13:E3} {vals(n - 1),-13:E3} " &
                      $"{m.SourceNorm(),-13:E3} {rmin,-13:E3} {rmax,-13:E3}")

            ' ---- 通量规模诊断（判断连续性误差是否量级合理）----
            Dim fmax As Double = 0, fsum As Double = 0

            For f As Integer = 0 To phi.Length - 1
                Dim a As Double = Math.Abs(phi(f))

                If a > fmax Then fmax = a
                fsum += a
            Next

            Dim phiH As Double() = PrivateArray(inc, "_phiHbyA")
            Dim phimax As Double = 0, phisum As Double = 0

            For f As Integer = 0 To phiH.Length - 1
                Dim a As Double = Math.Abs(phiH(f))

                If a > phimax Then phimax = a
                phisum += a
            Next

            ' 面面积中位数
            Dim sfv As Double() = New Double(mesh.FaceCount - 1) {}

            For f As Integer = 0 To mesh.FaceCount - 1
                sfv(f) = mesh.Mesh.FaceAreas(f)
            Next

            Array.Sort(sfv)

            Call Line($"        |flux|: max={fmax:E3} Σ={fsum:E3}   |phiHbyA|: max={phimax:E3} Σ={phisum:E3}   " &
                      $"faceArea p50={sfv(sfv.Length \ 2):E3}   cellVol={mesh.CellVolume(0):E3}   " &
                      $"continuity={inc.ContinuityError:E3}")

            ' ---- 独立复算连续性（自写散度，验证 FvcDiv / MaxContinuityError 本身是否正确）----
            Dim myDiv(n - 1) As Double

            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                myDiv(mesh.InternalOwner(i)) += phi(f)
                myDiv(mesh.InternalNeighbour(i)) -= phi(f)
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    myDiv(geo.OwnerCells(k)) += phi(geo.FaceIndices(k))
                Next
            Next

            Dim myMax As Double = 0

            For c As Integer = 0 To n - 1
                myMax = Math.Max(myMax, Math.Abs(myDiv(c)))
            Next

            Dim patchFaceSum As Integer = 0

            For pi As Integer = 0 To mesh.PatchCount - 1
                patchFaceSum += mesh.Patches(pi).FaceCount
            Next

            Call Line($"        自写散度 maxAbs = {myMax:E3}（应等于 continuity）；" &
                      $"boundary faces = {mesh.Mesh.BoundaryFaceCount}，patch 面数和 = {patchFaceSum}，" &
                      $"internal faces = {mesh.InternalFaceCount}，total = {mesh.FaceCount}")
        Next

        Return 0
    End Function

    ' ============================================================================
    '  [F] EulerianToCartesianResampler.AutoConfigure 探针
    ' ============================================================================

    Private Function RunResamplerProbe(zipPath As String) As Integer
        Call Section($"[F] EulerianToCartesianResampler.AutoConfigure 探针：{IO.Path.GetFileName(zipPath)}")

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)

        Dim bmin As EulerianVertex = Nothing
        Dim bmax As EulerianVertex = Nothing

        Call mesh.Mesh.GetBounds(bmin, bmax)

        Dim ex As Double = bmax.X - bmin.X
        Dim ey As Double = bmax.Y - bmin.Y
        Dim ez As Double = bmax.Z - bmin.Z
        Dim h0 As Double = Math.Pow(Math.Max(mesh.Mesh.TotalVolume, 1.0E-300) / Math.Max(1, mesh.CellCount), 1.0 / 3.0)

        Call Line($"  网格：cells={mesh.CellCount}, totalVolume={mesh.Mesh.TotalVolume:G6}")
        Call Line($"  bounds：min=({bmin.X:G6}, {bmin.Y:G6}, {bmin.Z:G6})  max=({bmax.X:G6}, {bmax.Y:G6}, {bmax.Z:G6})")
        Call Line($"  extent = ({ex:G6}, {ey:G6}, {ez:G6})")
        Call Line($"  平均单元尺度 h0 = (V/N)^(1/3) = {h0:G6}")
        Call Line($"  手算 nx = floor({ex:G6}/{h0:G6})+1 = {Math.Max(1, CInt(Math.Floor(ex / h0)) + 1)}")

        ' ---- 手工复算 AutoConfigure 的内部算术 ----
        Dim e2 As EulerianVertex = bmax - bmin

        Call Line($"  手工复算：extent = bmax - bmin = ({e2.X:G6}, {e2.Y:G6}, {e2.Z:G6})")
        Call Line($"    (V/N)^(1/3) = {h0:G6}")
        Call Line($"    floor(extent.X/h0)+1 = {Math.Floor(e2.X / h0) + 1}")
        Call Line($"    floor(extent.Y/h0)+1 = {Math.Floor(e2.Y / h0) + 1}")
        Call Line($"    floor(extent.Z/h0)+1 = {Math.Floor(e2.Z / h0) + 1}")
        Call Line($"    → 期望 {Math.Max(1, CInt(Math.Floor(e2.X / h0)) + 1)}x" &
                  $"{Math.Max(1, CInt(Math.Floor(e2.Y / h0)) + 1)}x" &
                  $"{Math.Max(1, CInt(Math.Floor(e2.Z / h0)) + 1)}")

        ' ---- 默认 maxCells = 4_000_000 ----
        Dim rs As New EulerianToCartesianResampler(mesh.Mesh)

        Call Line($"  构造后（AutoConfigure 之前）：{rs.Nx}x{rs.Ny}x{rs.Nz}, h={rs.SpacingX}, " &
                  $"origin=({rs.OriginX}, {rs.OriginY}, {rs.OriginZ})")

        Call rs.AutoConfigure()

        Call Line($"  AutoConfigure 之后（立即读）：{rs.Nx}x{rs.Ny}x{rs.Nz}")
        Call Line($"  类型 = {rs.GetType().AssemblyQualifiedName}")
        Call Line($"  AutoConfigure 方法 = {rs.GetType().GetMethod("AutoConfigure").ToString()}")

        ' 用反射读 Nx / Ny / Nz 的编译期后备字段（自动属性 ⇒ <Nx>k__BackingField）
        For Each f As Reflection.FieldInfo In rs.GetType().GetFields(
                Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Public)
            Call Line($"    字段 {f.Name,-40} {f.FieldType.Name,-10} = {f.GetValue(rs)}")
        Next

        For Each pi As Reflection.PropertyInfo In rs.GetType().GetProperties(Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public)
            If pi.Name = "Nx" OrElse pi.Name = "Ny" OrElse pi.Name = "Nz" Then
                Call Line($"    属性 {pi.Name} CanWrite={pi.CanWrite} 值={pi.GetValue(rs)}")
            End If
        Next

        ' 用 Reflection.Emit 无从取得中间量 → 直接内联复算（与源码逐行等价）
        Dim bx As Double = ex / h0
        Call Line($"    内联复算：extent.X/h0 = {bx:G17}")
        Call Line($"              Floor = {Math.Floor(bx):G17}")
        Call Line($"              CInt(Floor) = {CInt(Math.Floor(bx))}")
        Call Line($"              Math.Max(1, CInt+1) = {Math.Max(1, CInt(Math.Floor(bx)) + 1)}")

        ' 直接从 AutoConfigure 源码逐行复算一个局部实例（对照）
        Dim rtest As New EulerianToCartesianResampler(mesh.Mesh)
        Dim tbmin As EulerianVertex = Nothing
        Dim tbmax As EulerianVertex = Nothing

        Call rtest.Mesh.GetBounds(tbmin, tbmax)

        Dim text2 As EulerianVertex = tbmax - tbmin
        Dim th As Double = Math.Pow(Math.Max(rtest.Mesh.TotalVolume, 1.0E-300) / Math.Max(1, rtest.Mesh.CellCount), 1.0 / 3.0)
        Dim tnx As Integer = Math.Max(1, CInt(Math.Floor(text2.X / th)) + 1)
        Dim tny As Integer = Math.Max(1, CInt(Math.Floor(text2.Y / th)) + 1)
        Dim tnz As Integer = Math.Max(1, CInt(Math.Floor(text2.Z / th)) + 1)

        Call Line($"  逐行复算（同源码）：{tnx}x{tny}x{tnz}  (h={th:G6})")

        Dim rtest2 As New EulerianToCartesianResampler(mesh.Mesh)

        rtest2.Nx = tnx : rtest2.Ny = tny : rtest2.Nz = tnz
        Call Line($"  直接给属性赋值后读回：{rtest2.Nx}x{rtest2.Ny}x{rtest2.Nz}")

        Dim rtest3 As New EulerianToCartesianResampler(mesh.Mesh)

        rtest3.AutoConfigure(th)
        Call Line($"  AutoConfigure(cellSize=逐行复算的 h={th:G6}) -> {rtest3.Nx}x{rtest3.Ny}x{rtest3.Nz}, h={rtest3.SpacingX:G6}")

        Call Line($"")
        Call Line($"  AutoConfigure()（默认 maxCells=4000000）→ {rs.Nx}x{rs.Ny}x{rs.Nz}, " &
                  $"h={rs.SpacingX:G6}, origin=({rs.OriginX:G6}, {rs.OriginY:G6}, {rs.OriginZ:G6})")
        Call Line($"    CellCount = {rs.CellCount}")

        ' ---- 显式 maxCells 扫描 ----
        For Each mc As Integer In New Integer() {1000000, 4000000, 10000000}
            Dim r2 As New EulerianToCartesianResampler(mesh.Mesh)

            Call r2.AutoConfigure(0.0R, mc)

            Call Line($"  AutoConfigure(cellSize=0, maxCells={mc}) → {r2.Nx}x{r2.Ny}x{r2.Nz}, h={r2.SpacingX:G6}")
        Next

        ' ---- 显式 cellSize 扫描 ----
        For Each cs As Double In New Double() {8.0R, 16.0R, 33.0R}
            Dim r3 As New EulerianToCartesianResampler(mesh.Mesh)

            Call r3.AutoConfigure(cs)

            Call Line($"  AutoConfigure(cellSize={cs:G4}) → {r3.Nx}x{r3.Ny}x{r3.Nz}, h={r3.SpacingX:G6}")
        Next

        ' ---- Resample 抽样（看是否真的找得到单元）----
        Dim rs2 As New EulerianToCartesianResampler(mesh.Mesh)

        Call rs2.AutoConfigure()

        Dim n As Integer = rs2.CellCount
        Dim u As Double() = New Double(3 * mesh.CellCount - 1) {}
        Dim sampled As Single() = rs2.Resample(u, 3)
        Dim nz As Integer = 0

        For i As Integer = 0 To sampled.Length - 1
            If sampled(i) <> 0 Then nz += 1
        Next

        Call Line($"")
        Call Line($"  Resample 输出长度 = {sampled.Length}（={n} 节点 × 3 分量），非零项 = {nz}")

        Return 0
    End Function

    ' ============================================================================
    '  [E] 压力方程线性求解探针
    ' ============================================================================

    Private Function RunPressureDiag(zipPath As String, steps As Integer, gpu As Boolean) As Integer
        Call Section($"[E] 压力方程线性求解探针：{IO.Path.GetFileName(zipPath)}（先推进 {steps} 步）")

        Call EnableGpu()

        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Dim opts As New EulerianWindTunnelOptions With {
            .InletSpeed = If(_speedOverride >= 0, _speedOverride, 30.0R),
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = 1.225R,
            .KinematicViscosity = 0.000015R,
            .Turbulence = WindTunnelTurbulence.KEpsilon,
            .TurbulenceIntensity = 0.02R,
            .Compressible = False,
            .UseGpu = gpu,
            .PressureMaxIterations = 200}

        Dim testCase As New EulerianWindTunnelCase(mesh, opts)
        Call testCase.Configure()

        ' ---- 逐步诊断：看清在哪一步开始崩坏（pressure / continuity / max|U|）----
        Sub_("逐步诊断（每步打印 U / p / 连续性 / 压力求解器）")

        Call Line("  step    max|U|        max|p|       cont           pIt  pRes(init->final)         RauCell(max)")

        If _prIterOverride > 0 Then testCase.Incompressible.Options.PressureMaxIterations = _prIterOverride
        If _prTolOverride > 0 Then testCase.Incompressible.Options.PressureTolerance = _prTolOverride
        If _momSweepOverride > 0 Then testCase.Incompressible.Options.MomentumSweeps = _momSweepOverride
        If _coOverride > 0 Then testCase.Incompressible.Options.PseudoTransientCourant = _coOverride

        For s As Integer = 1 To Math.Max(1, steps)
            Call testCase.Run(1)

            Dim inc0 As IncompressibleSolver = testCase.Incompressible
            Dim rc As Double() = PrivateArray(inc0, "_rauCell")
            Dim rcPeak As Double = 0

            For i As Integer = 0 To rc.Length - 1
                If rc(i) > rcPeak Then rcPeak = rc(i)
            Next

            Dim pr As EulerianSolverReport = inc0.PressureReport

            Call Line($"  {s,4}    {SafeMax(testCase.Fields.U.Values),-13:E3} " &
                      $"{SafeMax(testCase.Fields.P.Values),-13:E3} {inc0.ContinuityError,-13:E3} " &
                      $"{pr.Iterations,-4} {pr.InitialResidual:E3}->{pr.FinalResidual:E3}   {rcPeak:E3}")

            If s >= Math.Max(1, steps) Then Exit For
        Next

        Dim inc As IncompressibleSolver = testCase.Incompressible
        Dim n As Integer = mesh.CellCount
        Dim nF As Integer = mesh.FaceCount

        Call Line($"  推进 {testCase.StepCount} 步后：max|U|={SafeMax(testCase.Fields.U.Values):E3}，" &
                  $"continuity={inc.ContinuityError:E3}")
        Call Line($"  速度方程最近报告数 = {inc.MomentumReports.Count}")

        For i As Integer = Math.Max(0, inc.MomentumReports.Count - 6) To inc.MomentumReports.Count - 1
            Call Line($"    {inc.MomentumReports(i).ToString()}")
        Next

        Call Line($"  压力方程最近报告：{inc.PressureReport.ToString()}")

        ' ---- 重建压力方程（与 IncompressibleSolver.PressureCorrection 完全一致）----
        Sub_("重建压力方程 mP 并逐一求解")

        Dim p As EulerianField = testCase.Fields.P
        Dim mP As New FvMatrix(mesh, "p")

        mP.Symmetric = True

        ' IncompressibleSolver 未公开 rAU 面值 / phiHbyA → 用反射取私有字段
        Dim rauFace As Double() = PrivateArray(inc, "_rauFace")
        Dim phiHbyA As Double() = PrivateArray(inc, "_phiHbyA")

        Call Line($"  反射取得 _rauFace(len={rauFace.Length}) 与 _phiHbyA(len={phiHbyA.Length})")

        ' ---- rAU 量级（决定压力方程对角元）----
        Dim rauMin As Double = Double.MaxValue, rauMax As Double = Double.MinValue, rauSum As Double = 0

        For i As Integer = 0 To rauFace.Length - 1
            Dim v As Double = rauFace(i)

            rauMin = Math.Min(rauMin, v) : rauMax = Math.Max(rauMax, v) : rauSum += Math.Abs(v)
        Next

        Dim rauCell As Double() = PrivateArray(inc, "_rauCell")
        Dim rcMin As Double = Double.MaxValue, rcMax As Double = Double.MinValue

        For i As Integer = 0 To rauCell.Length - 1
            rcMin = Math.Min(rcMin, rauCell(i)) : rcMax = Math.Max(rcMax, rauCell(i))
        Next

        Call Line($"  _rauCell 范围 = [{rcMin:E4}, {rcMax:E4}]  （= 1/a_P，动量方程对角元的倒数）")
        Call Line($"  _rauFace 范围 = [{rauMin:E4}, {rauMax:E4}]，均值 |rAU| = {rauSum / rauFace.Length:E4}")
        Call Line($"  → 动量方程对角元 a_P ≈ [{SafeDiv(1.0, rcMax):E4}, {SafeDiv(1.0, rcMin):E4}]")
        Call Line($"  → 压力方程对角元 ≈ a_P × 面积(≈{Median(mesh.Mesh.FaceAreas):E4}) " &
                  $"= [{SafeDiv(Median(mesh.Mesh.FaceAreas), rcMax):E4}, {SafeDiv(Median(mesh.Mesh.FaceAreas), rcMin):E4}]")

        ' ---- phiHbyA 量级（连续性的散度源）----
        Dim phMin As Double = Double.MaxValue, phMax As Double = Double.MinValue, phAbs As Double = 0

        For i As Integer = 0 To phiHbyA.Length - 1
            phMin = Math.Min(phMin, phiHbyA(i)) : phMax = Math.Max(phMax, phiHbyA(i))
            phAbs += Math.Abs(phiHbyA(i))
        Next

        Call Line($"  _phiHbyA 范围 = [{phMin:E4}, {phMax:E4}]，Σ|phiHbyA| = {phAbs:E4}")

        Call EulerianOperators.FvmLaplacian(mP, p, 0, rauFace, sign:=-1.0)

        Dim divPhi As Double() = New Double(n - 1) {}

        Call EulerianOperators.FvcDiv(mesh, phiHbyA, divPhi)
        Call mP.AddSourceField(divPhi)

        ' 参考单元（全 Neumann 时）
        Dim hasFixedValue As Boolean = False

        For pi As Integer = 0 To mesh.PatchCount - 1
            If TypeOf p.Boundaries(pi).Condition Is FixedValueBc Then hasFixedValue = True
        Next

        Call Line($"  压力边界是否含 FixedValue（定压）：{hasFixedValue}")

        If Not hasFixedValue Then
            Call mP.SetReference(0, 0.0)
            Call Line("  → 已注入 SetReference(0, 0)（与 NeedsPressureReference 分支一致）")
        End If

        Dim p0 As Double() = p.ToComponent(0)
        Dim rpt0 As New EulerianSolverReport With {
            .Equation = "mP", .Solver = EulerianLinearSolverKind.Pcg,
            .InitialResidual = mP.NormalisedResidual(p0),
            .FinalResidual = mP.NormalisedResidual(p0)}
        Dim m0 As New StringBuilder()

        Call Line($"  |b| (L2) = {mP.SourceNorm():E6}，对角元范围 = {DiagRange(mesh, mP)}")
        Call Line($"  初始解 p 的归一化残差 = {rpt0.InitialResidual:E6}")

        ' ---- 不同迭代上限 ----
        For Each it As Integer In New Integer() {1, 2, 5, 20, 100, 500, 2000}
            Dim x As Double() = Nothing
            Dim sw As Stopwatch = Stopwatch.StartNew()
            Dim r As EulerianSolverReport = EulerianLinearSolve.Solve(
                mP, x, EulerianLinearSolverKind.Pcg, 1.0E-9, 0.0, it, 0, gpu)
            sw.Stop()

            Call Line($"  maxIter={it,-5} → {r.ToString()}")
        Next

        ' ---- 相对容差 0.01（求解器实际使用的值）----
        Sub_("相对容差 relTol = 0.01（PressureCorrection 实际调用参数）")

        For Each it As Integer In New Integer() {1, 5, 50, 500}
            For Each useGpu As Boolean In New Boolean() {gpu, False}
                Dim x As Double() = Nothing
                Dim r As EulerianSolverReport = EulerianLinearSolve.Solve(
                    mP, x, EulerianLinearSolverKind.Pcg, 1.0E-9, 0.01, it, 0, useGpu)

                Call Line($"  maxIter={it,-5} useGpu={useGpu,-6} → {r.ToString()}")
            Next
        Next

        ' ---- 手工 PCG 残差历史（CPU double，纯 FvMatrix API）----
        Sub_("手工 PCG 残差历史（CPU double）")

        Dim xm(n - 1) As Double
        Dim rm(n - 1) As Double
        Dim pm(n - 1) As Double
        Dim apm(n - 1) As Double
        Dim zm(n - 1) As Double
        Dim de As Integer() = mesh.DiagEntry

        Array.Copy(p0, xm, n)
        Call mP.ResidualVector(xm, rm)

        Dim rnorm As Double = mP.NormalisedResidual(xm)
        Dim den As Double = 1.0E-30 + mP.SourceNorm()

        Call Line($"  iter=0   normRes={rnorm:E4}")

        For c As Integer = 0 To n - 1
            zm(c) = rm(c) / mP.Values(de(c))
        Next

        Array.Copy(zm, pm, n)

        Dim rho As Double = Dot(rm, zm)

        For iter As Integer = 1 To 200
            Call mP.Multiply(pm, apm)

            Dim pAp As Double = Dot(pm, apm)

            If Math.Abs(pAp) < 1.0E-300 Then
                Call Line($"  iter={iter}  pAp≈0（矩阵奇异/预条件失效），终止")
                Exit For
            End If

            Dim alpha As Double = rho / pAp

            For c As Integer = 0 To n - 1
                xm(c) += alpha * pm(c)
                rm(c) -= alpha * apm(c)
            Next

            rnorm = Math.Sqrt(Dot(rm, rm)) / den

            If iter <= 5 OrElse iter Mod 20 = 0 Then
                Call Line($"  iter={iter,-5} normRes={rnorm:E4}  alpha={alpha:E3}  rho={rho:E3}")
            End If

            For c As Integer = 0 To n - 1
                zm(c) = rm(c) / mP.Values(de(c))
            Next

            Dim rhoNew As Double = Dot(rm, zm)

            If Math.Abs(rho) < 1.0E-300 Then Exit For

            Dim beta As Double = rhoNew / rho

            rho = rhoNew

            For c As Integer = 0 To n - 1
                pm(c) = zm(c) + beta * pm(c)
            Next
        Next

        Return 0
    End Function

    Private Function SafeDiv(a As Double, b As Double) As Double
        If Math.Abs(b) < 1.0E-300 Then Return 0
        Return a / b
    End Function

    ''' <summary>轻量：只统计"死端单元"占比（网格质量指标）。</summary>
    Private Function RunDeadEndScan(zipPath As String) As Integer
        Dim mesh As EulerianFvMesh = EulerianFvMesh.FromFile(zipPath)
        Dim n As Integer = mesh.CellCount
        Dim noOut As Integer = 0, noIn As Integer = 0, both As Integer = 0

        For c As Integer = 0 To n - 1
            Dim hasIn As Boolean = False, hasOut As Boolean = False
            Dim cf As Integer() = mesh.CellFaces(c)

            For k As Integer = 0 To cf.Length - 1
                Dim f As Integer = cf(k)
                Dim eF As Moira.CFDEngine.Eulerian.EulerianFace = mesh.Mesh.Faces(f)

                If eF.Neighbour >= 0 Then
                    Dim sgn As Double = If(eF.Owner = c, 1.0R, -1.0R)
                    Dim sx As Double = sgn * mesh.Sf(f).X

                    If sx < -1.0E-9 Then hasIn = True
                    If sx > 1.0E-9 Then hasOut = True
                End If
            Next

            If Not hasOut Then noOut += 1
            If Not hasIn Then noIn += 1
            If Not hasIn AndAlso Not hasOut Then both += 1
        Next

        Console.WriteLine($"{IO.Path.GetFileName(zipPath),-42} cells={n,9}  " &
                          $"无x出流面={noOut,7} ({100.0 * noOut / n,6:F3}%)  " &
                          $"无x入流面={noIn,7} ({100.0 * noIn / n,6:F3}%)  两者皆无={both,6}")

        Return 0
    End Function

    ''' <summary>用反射读取对象的私有 Double() 字段（诊断用，绕过内部封装）。</summary>
    Private Function PrivateArray(obj As Object, fieldName As String) As Double()
        Dim f As Reflection.FieldInfo = obj.GetType().GetField(
            fieldName, Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)

        If f Is Nothing Then
            Throw New MissingFieldException($"{obj.GetType().Name} 上找不到字段 {fieldName}")
        End If

        Return CType(f.GetValue(obj), Double())
    End Function

    Private Function Dot(a As Double(), b As Double()) As Double
        Dim s As Double = 0

        For i As Integer = 0 To a.Length - 1
            s += a(i) * b(i)
        Next

        Return s
    End Function

    Private Function SafeMax(a As Double()) As Double
        Dim m As Double = 0

        For i As Integer = 0 To a.Length - 1
            If Double.IsNaN(a(i)) OrElse Double.IsInfinity(a(i)) Then Return Double.NaN

            If Math.Abs(a(i)) > m Then m = Math.Abs(a(i))
        Next

        Return m
    End Function

    Private Function DiagRange(mesh As EulerianFvMesh, m As FvMatrix) As String
        Dim de As Integer() = mesh.DiagEntry
        Dim mn As Double = Double.MaxValue, mx As Double = Double.MinValue

        For c As Integer = 0 To mesh.CellCount - 1
            Dim v As Double = m.Values(de(c))

            mn = Math.Min(mn, v) : mx = Math.Max(mx, v)
        Next

        Return $"[{mn:E3}, {mx:E3}]"
    End Function

    ' ============================================================================
    '  流线追踪 + 指标统计
    ' ============================================================================

    Private Class TraceReport

        Public Title As String
        Public StreamSet As StreamlineSet
        Public MinStraightness As Double
        Public MedianStraightness As Double
        Public MeanStraightness As Double
        Public MaxStraightness As Double
        Public KeptSegments As Integer
        Public ParticleCount As Integer
        Public ValidLines As Integer
        Public CurvedLines As Integer
        Public MaxDeviation As Double        ' 顶点的最大 |Δy|+|Δz| 偏离种子点
        Public MaxCurvedStraightness As Double
        Public BestLineDescription As String = ""
        Public AllStraight As Boolean

    End Class

    ''' <summary>追踪一遍流线并打印统计。</summary>
    Private Function TraceAndAnalyze(label As String, snap As BoxSnapshot,
                                     seedStride As Integer, stepLength As Single, minCurvature As Single) As TraceReport

        Dim rep As New TraceReport With {.Title = label}
        Dim field As New DiagFlowField(snap)

        Dim opts As New StreamlineOptions With {
            .SeedStride = Math.Max(1, seedStride),
            .StepLength = stepLength,
            .MaxSteps = 3000,
            .MinSpeed = 0.0005F,
            .MaxSegments = 400000}

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim set1 As StreamlineSet = Nothing

        Try
            set1 = StreamlineTracer.Trace(field, opts)
        Catch ex As Exception
            Call Line($"  [{label}] 追踪异常：{ex.GetType().Name}: {ex.Message}")
            rep.StreamSet = New StreamlineSet With {.Polylines = New StreamlinePolyline() {}}
            Return rep
        End Try

        sw.Stop()
        rep.StreamSet = set1

        ' 直线度统计
        Dim vals As New List(Of Double)
        Dim maxDev As Double = 0
        Dim bestMax As Double = -1
        Dim bestDesc As String = ""

        For Each pl As StreamlinePolyline In set1.Polylines
            If pl Is Nothing OrElse pl.VertexCount < 2 Then Continue For

            vals.Add(pl.Straightness)

            If pl.Straightness > bestMax Then
                bestMax = pl.Straightness

                Dim dy As Double = pl.Ys(pl.VertexCount - 1) - pl.Ys(0)
                Dim dz As Double = pl.Zs(pl.VertexCount - 1) - pl.Zs(0)
                Dim dyy As Double = 0, dzz As Double = 0

                For s As Integer = 0 To pl.VertexCount - 1
                    dyy = Math.Max(dyy, Math.Abs(pl.Ys(s) - pl.Ys(0)))
                    dzz = Math.Max(dzz, Math.Abs(pl.Zs(s) - pl.Zs(0)))
                Next

                bestDesc = $"v={pl.VertexCount}, L={pl.TotalLength:G6}, " &
                           $"种子(i0,j,k) 位移 Δy={dy:G4}/max|Δy|={dyy:G4}, Δz={dz:G4}/max|Δz|={dzz:G4}"
            End If

            Dim dev As Double = 0

            For s As Integer = 0 To pl.VertexCount - 1
                dev = Math.Max(dev, Math.Abs(pl.Ys(s) - pl.Ys(0)) + Math.Abs(pl.Zs(s) - pl.Zs(0)))
            Next

            If dev > maxDev Then maxDev = dev
        Next

        vals.Sort()

        rep.ValidLines = vals.Count
        rep.MinStraightness = If(vals.Count > 0, vals(0), 0)
        rep.MedianStraightness = If(vals.Count > 0, vals(vals.Count \ 2), 0)
        rep.MeanStraightness = If(vals.Count > 0, vals.Average(), 0)
        rep.MaxStraightness = If(vals.Count > 0, vals(vals.Count - 1), 0)
        rep.MaxDeviation = maxDev
        rep.MaxCurvedStraightness = bestMax
        rep.BestLineDescription = bestDesc
        rep.AllStraight = vals.Count > 0 AndAlso vals(vals.Count - 1) < StreamlineSet.DefaultStraightness

        ' 过滤后的可见线段（WindTunnelCanvas 走的就是这条路）
        set1.MinCurvature = If(minCurvature > 0, minCurvature, StreamlineSet.DefaultStraightness)

        Dim lines As LineSegment() = set1.BuildLines(Nothing, 210, 0, 0, 0)

        rep.KeptSegments = lines.Length

        Dim anim As New StreamlineAnimator(set1, 2)
        rep.ParticleCount = anim.ParticleCount

        rep.CurvedLines = set1.CurvedPolylines().Count()

        ' 打印
        Call Line("")
        Call Line($"  [{label}]  追踪 {sw.Elapsed.TotalMilliseconds:F1} ms")
        Call Line($"      种子数={set1.SeedCount}，有效流线={rep.ValidLines}，总线={set1.SegmentCount}，" &
                  $"截断={set1.Truncated}")
        Call Line($"      Straightness: min={rep.MinStraightness:G6} median={rep.MedianStraightness:G6} " &
                  $"mean={rep.MeanStraightness:G6} max={rep.MaxStraightness:G6}  " &
                  $"(阈值 {StreamlineSet.DefaultStraightness})")
        Call Line($"      最大横向偏移 max(|Δy|+|Δz|) = {rep.MaxDeviation:G6} 格")
        Call Line($"      过滤后可见线段 = {rep.KeptSegments}，曲线流线 = {rep.CurvedLines}，" &
                  $"光点粒子 = {rep.ParticleCount}")
        Call Line($"      最弯的一条：straightness={rep.MaxCurvedStraightness:G6}  {rep.BestLineDescription}")

        Return rep
    End Function

    ' ============================================================================
    '  WindTunnel CLI 路径交叉验证
    ' ============================================================================

    Private Sub CrossCheckCli(zipPath As String, mesh As EulerianFvMesh, testCase As EulerianWindTunnelCase)
        ' CLI 路径用的是 EulerianToCartesianResampler.AutoConfigure（Origin = 包围盒最小角，
        ' 且 Resample() 走的是同一个最近单元零阶逻辑）。
        Dim rs As New EulerianToCartesianResampler(mesh.Mesh)
        Call rs.AutoConfigure()

        Call Line($"  CLI AutoConfigure: {rs.Nx}×{rs.Ny}×{rs.Nz}, h={rs.SpacingX:G6} m, " &
                  $"origin=({rs.OriginX:G6}, {rs.OriginY:G6}, {rs.OriginZ:G6})")

        Dim n As Integer = rs.Nx * rs.Ny * rs.Nz
        Dim u As Double() = testCase.Fields.U.Values

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim uRes As Single() = rs.Resample(u, 3)
        sw.Stop()

        Call Line($"  Resample(velocity) 用时 {sw.Elapsed.TotalSeconds:F2} s，输出 {uRes.Length} 个 float")

        ' 拆成三个分量数组（Resample 输出为 idx*3 + comp 交错）
        Dim ux(n - 1) As Single, uy(n - 1) As Single, uz(n - 1) As Single
        Dim outside(n - 1) As Byte
        Dim nonZero As Integer = 0
        Dim outsideCount As Integer = 0

        For i As Integer = 0 To rs.Nx - 1
            Dim x As Double = rs.OriginX + i * rs.SpacingX

            For j As Integer = 0 To rs.Ny - 1
                Dim y As Double = rs.OriginY + j * rs.SpacingY

                For k As Integer = 0 To rs.Nz - 1
                    Dim z As Double = rs.OriginZ + k * rs.SpacingZ
                    Dim idx As Integer = (i * rs.Ny + j) * rs.Nz + k
                    Dim c As Integer = rs.NearestCell(x, y, z)

                    If c < 0 Then
                        outside(idx) = 1
                        outsideCount += 1
                        Continue For
                    End If

                    ux(idx) = uRes(idx * 3 + 0)
                    uy(idx) = uRes(idx * 3 + 1)
                    uz(idx) = uRes(idx * 3 + 2)
                    nonZero += 1
                Next
            Next
        Next

        Call Line($"  映射成功 {nonZero} / {n} 节点，域外（未找到单元） {outsideCount} " &
                  $"（{100.0 * outsideCount / n:F2}%）")

        Dim snap As New BoxSnapshot(rs.Nx, rs.Ny, rs.Nz, ux, uy, uz, outside)

        Call TraceAndAnalyze("WindTunnel CLI（AutoConfigure 零阶）", snap, 4, 0.7F, 0.0F)

        ' CLI 的 .vti 输出是给离线查看器的；同样用零阶，结论一致。
        Call Line("")
        Call Line("  说明：CLI 的 --format vti 与 WindTunnelBox 的重采样都是『最近单元中心（零阶）』，")
        Call Line("        因此两条路径产出的流线形态属于同一类，可直接对照上面的数字。")
    End Sub

End Module
