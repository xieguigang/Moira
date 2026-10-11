' /********************************************************************************/
'
'   CliOptions.vb
'
'   EulerianFvm 命令行参数模型与解析（风格对齐 src\EulerianMesh\Program.vb 与
'   src\FVMEngine\test\Program.vb）。
'
'   子命令：
'       windtunnel   风洞绕流（F1 / 飞机 / 城市建筑群）
'       tank         发酵罐搅拌（内流 + MRF 桨盘 + VOF 自由液面）
'
'   通用选项见 <see cref="Usage"/>。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports Moira.FVMEngine.Eulerian

Namespace WindTunnel

    ''' <summary>输出格式。</summary>
    Public Enum OutputFormat
        ''' <summary>仅非结构 .vtu（原生网格，推荐）。</summary>
        Vtu = 0
        ''' <summary>仅结构化 .vti（由 Eulerian 解重采样到笛卡尔格）。</summary>
        Vti = 1
        ''' <summary>both：同时输出 .vtu 与 .vti 两套动画序列（写入 out/vtu 与 out/vti）。</summary>
        Both = 2
    End Enum

    ''' <summary>子命令。</summary>
    Public Enum CliCommand
        None = 0
        WindTunnel = 1
        Tank = 2
    End Enum

    ''' <summary>命令行选项集合。</summary>
    Public Class CliOptions

        Public Property Command As CliCommand = CliCommand.None

        ''' <summary>Eulerian 网格 zip 路径（tank 可省略：现场由 ZMesh 生成）。</summary>
        Public Property MeshZip As String = Nothing

        ''' <summary>输出目录。</summary>
        Public Property OutputDir As String = "."

        ''' <summary>输出格式。</summary>
        Public Property Format As OutputFormat = OutputFormat.Vtu

        ''' <summary>物理模型：incompressible | compressible。</summary>
        Public Property Physics As String = "incompressible"

        ''' <summary>湍流模型：none | ke | sst。</summary>
        Public Property Turbulence As String = "ke"

        ''' <summary>求解器：simple | pimple（不可压）。</summary>
        Public Property Solver As String = "simple"

        ''' <summary>对流格式：upwind | central | linear-upwind | quick。</summary>
        Public Property Scheme As String = "upwind"

        ''' <summary>可压缩通量格式：ausm+ | hllc。</summary>
        Public Property FluxScheme As String = "AUSM+"

        ''' <summary>来流速度（m/s）。</summary>
        Public Property InletSpeed As Double = 30.0

        Public Property InletDirX As Double = 1.0
        Public Property InletDirY As Double = 0.0
        Public Property InletDirZ As Double = 0.0

        ''' <summary>密度（kg/m³）。</summary>
        Public Property Density As Double = 1.225

        ''' <summary>运动粘度（m²/s）。</summary>
        Public Property Nu As Double = 1.5E-5

        ''' <summary>参考压力 / 总压（Pa）。</summary>
        Public Property Pressure As Double = 101325.0

        ''' <summary>总温（K）。</summary>
        Public Property Temperature As Double = 288.15

        ''' <summary>马赫数（可压缩）。</summary>
        Public Property Mach As Double = 0.1

        ''' <summary>湍流强度。</summary>
        Public Property TurbulenceIntensity As Double = 0.01

        ''' <summary>湍流长度尺度（m；&lt;= 0 时按模型尺度自动）。</summary>
        Public Property TurbulenceLengthScale As Double = 0.0

        ''' <summary>总步数（稳态迭代 / 瞬态时间步）。</summary>
        Public Property Steps As Integer = 1500

        ''' <summary>时间步长（瞬态；&lt;= 0 自动）。</summary>
        Public Property DeltaT As Double = 0

        ''' <summary>CFL 数（可压缩）。</summary>
        Public Property Cfl As Double = 0.8

        ''' <summary>速度欠松弛。</summary>
        Public Property RelaxU As Double = 0.7

        ''' <summary>压力方程最大迭代数。</summary>
        Public Property PressureIterations As Integer = 200

        ''' <summary>快照采样间隔（步）。</summary>
        Public Property Interval As Integer = 10

        ''' <summary>是否使用 GPU float32 后端。</summary>
        Public Property UseGpu As Boolean = True

        ''' <summary>参考面积（m²；&lt;= 0 时按网格/模型自动）。</summary>
        Public Property ReferenceArea As Double = 0

        ''' <summary>参考长度（m）。</summary>
        Public Property ReferenceLength As Double = 0

        ''' <summary>求解完成后是否输出力 / 功率报告（默认输出）。</summary>
        Public Property Report As Boolean = True

        ''' <summary>探针（x,y,z 列表，可多次指定）。</summary>
        Public ReadOnly Property Probes As New List(Of Double())()

        ''' <summary>verbose 日志（逐方程残差）。</summary>
        Public Property Verbose As Boolean = False

        ''' <summary>tank：罐内径（m）。</summary>
        Public Property TankDiameter As Double = 0.3

        ''' <summary>tank：罐高（m；&lt;= 0 时取 1.5·D）。</summary>
        Public Property TankHeight As Double = 0

        ''' <summary>tank：液面高度（m；&lt;= 0 时取 2/3·H）。</summary>
        Public Property LiquidLevel As Double = 0

        ''' <summary>tank：转速（rpm）。</summary>
        Public Property Rpm As Double = 300

        ''' <summary>tank：网格相对分辨率。</summary>
        Public Property Resolution As Double = 0.04

        ''' <summary>tank：是否使用切割单元。</summary>
        Public Property CutCells As Boolean = True

        ''' <summary>tank：虚拟桨盘驱动强度（相对 ω 的倍数）。</summary>
        Public Property DriveStrengthRatio As Double = 0.0

        ''' <summary>tank：几何文件目录。</summary>
        Public Property GeometryDir As String = Nothing

        ''' <summary>tank：是否瞬态（默认 True，VOF 演化）。</summary>
        Public Property Transient As Boolean = True

        ''' <summary>tank：网格输出 zip（可选保存）。</summary>
        Public Property MeshOutZip As String = Nothing

        ''' <summary>请求帮助。</summary>
        Public Property Help As Boolean = False

        ''' <summary>解析错误信息（Nothing = 解析成功）。</summary>
        Public Property ErrorMessage As String = Nothing

#Region "解析"

        ''' <summary>
        ''' 解析命令行。
        ''' </summary>
        Public Shared Function Parse(args As String()) As CliOptions
            Dim o As New CliOptions()

            If args Is Nothing OrElse args.Length = 0 Then
                o.Help = True
                Return o
            End If

            Dim i As Integer = 0
            Dim first As String = args(0)

            If Not first.StartsWith("-") Then
                Select Case first.ToLowerInvariant
                    Case "windtunnel", "wind", "wt"
                        o.Command = CliCommand.WindTunnel
                    Case "tank", "fermenter", "fermentation"
                        o.Command = CliCommand.Tank
                    Case Else
                        o.ErrorMessage = $"未知子命令 '{first}'（可用：windtunnel | tank）"
                        Return o
                End Select

                i = 1
            End If

            While i < args.Length
                Dim key As String = args(i)

                If key = "--help" OrElse key = "-h" OrElse key = "/?" Then
                    o.Help = True
                    Return o
                End If

                If Not key.StartsWith("-") Then
                    o.ErrorMessage = $"意外的位置参数 '{key}'"
                    Return o
                End If

                Dim value As String = Nothing

                ' 支持 --key=value 与 --key value 两种写法
                Dim eq As Integer = key.IndexOf("="c)

                If eq > 0 Then
                    value = key.Substring(eq + 1)
                    key = key.Substring(0, eq)
                End If

                key = key.TrimStart("-"c).ToLowerInvariant()

                If value Is Nothing Then
                    If i + 1 < args.Length AndAlso Not args(i + 1).StartsWith("-") Then
                        value = args(i + 1)
                        i += 1
                    End If
                End If

                i += 1

                Select Case key
                    Case "mesh", "mesh-zip", "i"
                        o.MeshZip = value
                    Case "out", "o"
                        o.OutputDir = value
                    Case "format"
                        Select Case LCase(value)
                            Case "vtu" : o.Format = OutputFormat.Vtu
                            Case "vti" : o.Format = OutputFormat.Vti
                            Case "both" : o.Format = OutputFormat.Both
                            Case Else
                                o.ErrorMessage = "--format 只能是 vtu | vti | both"
                                Return o
                        End Select
                    Case "physics"
                        o.Physics = LCase(value)
                    Case "turbulence", "turb"
                        o.Turbulence = LCase(value)
                    Case "solver"
                        o.Solver = LCase(value)
                    Case "scheme"
                        o.Scheme = LCase(value)
                    Case "flux"
                        o.FluxScheme = value
                    Case "inlet-u", "u"
                        If Not TryParseDouble(value, o.InletSpeed, o) Then Return o
                    Case "inlet-dir"
                        If Not TryParseVec3(value, o, Sub(x, y, z)
                                                    o.InletDirX = x : o.InletDirY = y : o.InletDirZ = z
                                                End Sub) Then Return o
                    Case "rho"
                        If Not TryParseDouble(value, o.Density, o) Then Return o
                    Case "nu"
                        If Not TryParseDouble(value, o.Nu, o) Then Return o
                    Case "p0", "p"
                        If Not TryParseDouble(value, o.Pressure, o) Then Return o
                    Case "t0"
                        If Not TryParseDouble(value, o.Temperature, o) Then Return o
                    Case "mach"
                        If Not TryParseDouble(value, o.Mach, o) Then Return o
                    Case "ti"
                        If Not TryParseDouble(value, o.TurbulenceIntensity, o) Then Return o
                    Case "tl"
                        If Not TryParseDouble(value, o.TurbulenceLengthScale, o) Then Return o
                    Case "steps", "iterations", "n"
                        If Not TryParseInt(value, o.Steps, o) Then Return o
                    Case "dt"
                        If Not TryParseDouble(value, o.DeltaT, o) Then Return o
                    Case "cfl"
                        If Not TryParseDouble(value, o.Cfl, o) Then Return o
                    Case "relax-u"
                        If Not TryParseDouble(value, o.RelaxU, o) Then Return o
                    Case "relax-p"
                        ' 压力耦合的欠松弛已由 rAU 承担；此参数保留仅为兼容（未使用）
                    Case "p-iter"
                        If Not TryParseInt(value, o.PressureIterations, o) Then Return o
                    Case "interval"
                        If Not TryParseInt(value, o.Interval, o) Then Return o
                    Case "gpu"
                        o.UseGpu = True
                    Case "no-gpu"
                        o.UseGpu = False
                    Case "ref-area"
                        If Not TryParseDouble(value, o.ReferenceArea, o) Then Return o
                    Case "ref-length"
                        If Not TryParseDouble(value, o.ReferenceLength, o) Then Return o
                    Case "probe"
                        If Not TryParseVec3(value, o, Sub(x, y, z) o.Probes.Add(New Double() {x, y, z})) Then Return o
                    Case "no-report"
                        o.Report = False
                    Case "verbose", "v"
                        o.Verbose = True
                    Case "transient"
                        o.Transient = True
                    Case "steady"
                        o.Transient = False
                    Case "d", "diameter"
                        If Not TryParseDouble(value, o.TankDiameter, o) Then Return o
                    Case "h", "height"
                        If Not TryParseDouble(value, o.TankHeight, o) Then Return o
                    Case "liquid-level", "level"
                        If Not TryParseDouble(value, o.LiquidLevel, o) Then Return o
                    Case "rpm"
                        If Not TryParseDouble(value, o.Rpm, o) Then Return o
                    Case "resolution", "r"
                        If Not TryParseDouble(value, o.Resolution, o) Then Return o
                    Case "cut-cells"
                        o.CutCells = True
                    Case "no-cut-cells"
                        o.CutCells = False
                    Case "drive"
                        If Not TryParseDouble(value, o.DriveStrengthRatio, o) Then Return o
                    Case "geometry-dir"
                        o.GeometryDir = value
                    Case "mesh-out"
                        o.MeshOutZip = value
                    Case Else
                        o.ErrorMessage = $"未知选项 '{key}'（用 --help 查看用法）"
                        Return o
                End Select
            End While

            If o.Command = CliCommand.None Then
                o.ErrorMessage = "缺少子命令（windtunnel | tank）"
            End If

            Return o
        End Function

        Private Shared Function TryParseDouble(value As String, ByRef result As Double, o As CliOptions) As Boolean
            If value Is Nothing Then
                o.ErrorMessage = "缺少数值参数"
                Return False
            End If

            Dim v As Double

            If Not Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then
                o.ErrorMessage = $"无法解析数值 '{value}'"
                Return False
            End If

            result = v
            Return True
        End Function

        Private Shared Function TryParseInt(value As String, ByRef result As Integer, o As CliOptions) As Boolean
            Dim v As Integer

            If value Is Nothing OrElse Not Integer.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then
                o.ErrorMessage = $"无法解析整数 '{If(value, "")}'"
                Return False
            End If

            result = v
            Return True
        End Function

        Private Shared Function TryParseVec3(value As String, o As CliOptions, apply As Action(Of Double, Double, Double)) As Boolean
            If value Is Nothing Then
                o.ErrorMessage = "缺少三分量向量参数（格式 x,y,z）"
                Return False
            End If

            Dim parts As String() = value.Split(","c, ";"c)

            If parts.Length <> 3 Then
                o.ErrorMessage = $"三分量向量格式应为 x,y,z（收到 '{value}'）"
                Return False
            End If

            Dim v(2) As Double

            For i As Integer = 0 To 2
                If Not Double.TryParse(parts(i), NumberStyles.Float, CultureInfo.InvariantCulture, v(i)) Then
                    o.ErrorMessage = $"无法解析向量分量 '{parts(i)}'"
                    Return False
                End If
            Next

            Call apply(v(0), v(1), v(2))
            Return True
        End Function

#End Region

#Region "用法与映射"

        ''' <summary>转换到 FVMEngine 的湍流枚举。</summary>
        Public Function TurbulenceKind() As WindTunnelTurbulence
            Select Case Me.Turbulence.ToLowerInvariant
                Case "none", "laminar", "off"
                    Return WindTunnelTurbulence.Laminar
                Case "sst", "kw-sst", "k-omega-sst"
                    Return WindTunnelTurbulence.KOmegaSST
                Case Else
                    Return WindTunnelTurbulence.KEpsilon
            End Select
        End Function

        ''' <summary>转换到对流格式枚举。</summary>
        Public Function ConvectionScheme() As EulerianConvectionScheme
            Select Case Me.Scheme.ToLowerInvariant
                Case "central", "cd"
                    Return EulerianConvectionScheme.Central
                Case "linear-upwind", "linearupwind", "lu"
                    Return EulerianConvectionScheme.LinearUpwind
                Case "quick"
                    Return EulerianConvectionScheme.Quick
                Case Else
                    Return EulerianConvectionScheme.Upwind
            End Select
        End Function

        ''' <summary>是否为可压缩物理。</summary>
        Public Function IsCompressible() As Boolean
            Return String.Equals(Physics, "compressible", StringComparison.OrdinalIgnoreCase)
        End Function

        Public Shared ReadOnly Usage As String = BuildUsage()

        Private Shared Function BuildUsage() As String
            Dim sb As New StringBuilder(4096)

            sb.AppendLine("EulerianFvm —— 以 Eulerian（非结构多面体）网格为基础的 FVM CFD 求解器")
            sb.AppendLine()
            sb.AppendLine("用法：")
            sb.AppendLine("  EulerianFvm windtunnel --mesh <eulerian.zip> [选项]")
            sb.AppendLine("  EulerianFvm tank [--mesh <eulerian.zip>] [选项]")
            sb.AppendLine()
            sb.AppendLine("通用选项：")
            sb.AppendLine("  --mesh <path>          Eulerian 网格 zip（由 src\EulerianMesh 生成）")
            sb.AppendLine("  --out <dir>            输出目录（默认当前目录）")
            sb.AppendLine("  --format vtu|vti|both  输出格式（默认 vtu；both 会写入 out/vtu 与 out/vti）")
            sb.AppendLine("  --steps <n>            总步数（稳态迭代 / 瞬态时间步，默认 1500）")
            sb.AppendLine("  --dt <sec>             瞬态时间步长（<=0 自动按 CFL/叶尖速度）")
            sb.AppendLine("  --interval <n>         快照采样间隔（默认 10）")
            sb.AppendLine("  --p-iter <n>           压力方程最大迭代数（默认 200）")
            sb.AppendLine("  --[no-]gpu             是否使用 CUDA float32 后端（默认启用，失败自动回落）")
            sb.AppendLine("  --verbose              输出逐方程残差")
            sb.AppendLine("  --probe x,y,z          探针点（可重复）")
            sb.AppendLine()
            sb.AppendLine("物理与湍流：")
            sb.AppendLine("  --physics incompressible|compressible")
            sb.AppendLine("  --turbulence none|ke|sst")
            sb.AppendLine("  --scheme upwind|central|linear-upwind|quick")
            sb.AppendLine("  --flux AUSM+|HLLC      可压缩通量格式")
            sb.AppendLine()
            sb.AppendLine("风洞（windtunnel）：")
            sb.AppendLine("  --inlet-u <m/s>        来流速度（默认 30）")
            sb.AppendLine("  --inlet-dir x,y,z      来流方向（默认 1,0,0）")
            sb.AppendLine("  --rho <kg/m3>  --nu <m2/s>")
            sb.AppendLine("  --ti <I>  --tl <L>     湍流强度 / 长度尺度")
            sb.AppendLine("  --ref-area <m2>  --ref-length <m>")
            sb.AppendLine("  --mach <Ma> --p0 <Pa> --t0 <K>   （可压缩）")
            sb.AppendLine()
            sb.AppendLine("发酵罐（tank）：")
            sb.AppendLine("  -d/--diameter <m>      罐内径（默认 0.30）")
            sb.AppendLine("  -h/--height <m>        罐高（默认 1.5·D）")
            sb.AppendLine("  --liquid-level <m>     液面高度（默认 2/3·H，即 2/3 液体积）")
            sb.AppendLine("  --rpm <n>              转速（默认 300）")
            sb.AppendLine("  -r/--resolution <f>    网格相对分辨率（默认 0.04）")
            sb.AppendLine("  --[no-]cut-cells       切割单元（默认启用）")
            sb.AppendLine("  --drive <ratio>        虚拟桨盘驱动强度（相对 ω，默认 0 = 仅桨叶壁面驱动）")
            sb.AppendLine("  --steady|--transient   稳态伪瞬态 / 瞬态 VOF（默认 transient）")
            sb.AppendLine("  --geometry-dir <dir>   STL 几何输出目录")
            sb.AppendLine("  --mesh-out <zip>       同时保存生成的网格 zip")
            sb.AppendLine()
            sb.AppendLine("输出：")
            sb.AppendLine("  <out>/frame_*.vtu|.vti, animation.pvd, frames.json, metadata.json, summary.json")

            Return sb.ToString()
        End Function

#End Region

    End Class

End Namespace
