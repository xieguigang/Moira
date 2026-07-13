' /********************************************************************************/
'
'   WindTunnelTest.vb
'
'   风洞外流动模拟功能模块测试（控制台 PASS / FAIL 风格）
'
'   测试内容：
'       1. 从 JSON 加载体素模型（airplane1_28x9x32_voxels.json），
'          断言维度 28×9×32、固体体素数 > 0（反转映射正确）。
'       2. 按 domainScale 放大计算空间并把模型定位到指定离地高度，
'          断言放大空间维度 = 模型维度 × domainScale，
'          断言模型最低固体体素 j = groundClearance（离地高度正确）。
'       3. 初始化水平向右 (+X) 来流并运行若干时间步。
'       4. 计算并打印湍流 / 物理指标（最大 / 平均速度、enstrophy、尾流亏损），
'          断言无 NaN、enstrophy > 0、下游尾流亏损 > 0（模型阻挡形成尾流）。
'
'   运行：
'       在 test 项目中： dotnet run -- --windtunnel [模型路径]
'
' /********************************************************************************/

Imports Moira.CFDEngine
Imports std = System.Math

Module WindTunnelTest

    ''' <summary>
    ''' 运行风洞模块测试。返回 True 表示全部断言通过。
    ''' </summary>
    ''' <param name="modelPath">
    ''' 体素模型 JSON 路径；为空时默认在输出目录查找 airplane1_28x9x32_voxels.json。
    ''' </param>
    ''' <param name="domainScale">计算空间放大倍数（默认 2）</param>
    ''' <param name="groundClearance">模型离地高度（默认 0，贴地）</param>
    Public Function RunWindTunnelTest(Optional modelPath As String = Nothing,
                                      Optional domainScale As Double = 2.0,
                                      Optional groundClearance As Integer = 0) As Boolean

        Console.WriteLine(New String("="c, 70))
        Console.WriteLine("  风洞外流动模拟 —— 功能模块测试")
        Console.WriteLine("  Wind Tunnel External Flow - Module Test")
        Console.WriteLine(New String("="c, 70))
        Console.WriteLine()

        Dim allPass As Boolean = True

        ' ---- 解析模型路径 ----
        If String.IsNullOrEmpty(modelPath) Then
            modelPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "airplane1_28x9x32_voxels.json")
        End If
        Console.WriteLine($"[模型] {modelPath}")

        If Not System.IO.File.Exists(modelPath) Then
            Console.WriteLine($"[FAIL] 找不到模型文件：{modelPath}")
            Return False
        End If

        ' ---- 1. 加载体素模型 ----
        Console.WriteLine()
        Console.WriteLine("[1] 加载体素模型 JSON ...")
        Dim model As VoxelModel
        Try
            model = VoxelModelLoader.Load(modelPath)
        Catch ex As Exception
            Console.WriteLine($"[FAIL] 加载模型异常：{ex.Message}")
            Return False
        End Try

        Console.WriteLine($"    来源模型   : {model.SourceModel}")
        Console.WriteLine($"    模型维度   : {model.Width}×{model.Height}×{model.Depth}")
        Console.WriteLine($"    固体体素数 : {model.SolidVoxelCount}")
        Dim b = model.SolidBounds
        Console.WriteLine($"    固体包围盒 : X[{b.minX}..{b.maxX}] Y[{b.minY}..{b.maxY}] Z[{b.minZ}..{b.maxZ}]")

        allPass = Assert(model.Width = 28 AndAlso model.Height = 9 AndAlso model.Depth = 32,
                         "模型维度应为 28×9×32", allPass)
        allPass = Assert(model.SolidVoxelCount > 0, "固体体素数应 > 0（反转映射正确）", allPass)

        ' ---- 2. 构建风洞（放大空间 + 离地定位）----
        Console.WriteLine()
        Console.WriteLine($"[2] 构建风洞：domainScale={domainScale}, groundClearance={groundClearance} ...")
        Dim freestream As Double = 3.0
        Dim tunnel As WindTunnel
        Try
            tunnel = FluidSim.CreateWindTunnel(model,
                                               freestream:=freestream,
                                               domainScale:=domainScale,
                                               groundClearance:=groundClearance,
                                               viscosity:=0.0005)
        Catch ex As Exception
            Console.WriteLine($"[FAIL] 构建风洞异常：{ex.Message}")
            Return False
        End Try

        Dim f = tunnel.Field
        Console.WriteLine($"    计算空间维度 : {f.Nx}×{f.Ny}×{f.Nz}（共 {f.TotalVoxels} 体素）")
        Console.WriteLine($"    来流速度 U∞  : {tunnel.FreestreamVelocity}")

        ' 断言放大空间维度 = 模型维度 × domainScale（与 BuildDomain 的 Ceiling 一致）
        Dim expNx = std.Max(model.Width, CInt(std.Ceiling(model.Width * domainScale)))
        Dim expNy = std.Max(model.Height, CInt(std.Ceiling(model.Height * domainScale)))
        Dim expNz = std.Max(model.Depth, CInt(std.Ceiling(model.Depth * domainScale)))
        allPass = Assert(f.Nx = expNx AndAlso f.Ny = expNy AndAlso f.Nz = expNz,
                         $"放大空间维度应为 {expNx}×{expNy}×{expNz}", allPass)

        ' 断言模型最低固体体素 j = groundClearance（离地高度正确）
        Dim lowestSolidJ = FindLowestSolidJ(f)
        Console.WriteLine($"    放大空间中模型最低固体体素 j = {lowestSolidJ}（期望 {groundClearance}）")
        allPass = Assert(lowestSolidJ = groundClearance,
                         $"模型最低固体体素 j 应等于离地高度 {groundClearance}", allPass)

        ' 断言放大空间固体体素数 = 模型固体体素数（无丢失）
        Dim domainSolid = CountSolid(f)
        allPass = Assert(domainSolid = model.SolidVoxelCount,
                         $"放大空间固体体素数({domainSolid})应等于模型固体数({model.SolidVoxelCount})", allPass)

        ' ---- 3. 初始化来流并运行 ----
        Console.WriteLine()
        Console.WriteLine("[3] 初始化来流并运行模拟 ...")
        tunnel.InitializeFlow()

        Dim steps As Integer = 100
        Dim dt As Double = 0.1
        Dim startTime = DateTime.Now
        tunnel.Run(steps, dt,
                   Sub(stepIdx, t)
                       If stepIdx Mod 20 = 0 OrElse stepIdx = steps Then
                           Console.WriteLine($"    步 {stepIdx,3}/{steps}  时间={t:F2}  " &
                                             $"最大速度={tunnel.ComputeMaxSpeed():F3}")
                       End If
                   End Sub)
        Dim elapsed = (DateTime.Now - startTime).TotalSeconds
        Console.WriteLine($"    完成，耗时 {elapsed:F2} 秒")

        ' ---- 4. 计算并断言湍流 / 物理指标 ----
        Console.WriteLine()
        Console.WriteLine("[4] 湍流 / 物理结果指标：")
        Dim maxSpeed = tunnel.ComputeMaxSpeed()
        Dim avgSpeed = tunnel.ComputeAverageSpeed()
        Dim enstrophy = tunnel.ComputeEnstrophy()
        ' 下游平面：取模型固体包围盒下游若干格处（放大空间坐标）
        Dim downstreamI = CInt(std.Min(f.Nx - 2, (f.Nx \ 2) + model.Width \ 2 + 2))
        Dim wakeDeficit = tunnel.ComputeWakeDeficit(downstreamI)

        Console.WriteLine($"    最大速度       : {maxSpeed:F4}")
        Console.WriteLine($"    平均速度       : {avgSpeed:F4}")
        Console.WriteLine($"    总涡量 enstrophy: {enstrophy:F4}")
        Console.WriteLine($"    下游尾流亏损(i={downstreamI}): {wakeDeficit:F4}  (= U∞ - 平面平均U)")

        allPass = Assert(Not Double.IsNaN(maxSpeed) AndAlso Not Double.IsInfinity(maxSpeed),
                         "最大速度应为有限值（无 NaN / Inf）", allPass)
        allPass = Assert(Not Double.IsNaN(enstrophy) AndAlso Not Double.IsInfinity(enstrophy),
                         "enstrophy 应为有限值（无 NaN / Inf）", allPass)
        allPass = Assert(enstrophy > 0.0, "enstrophy 应 > 0（流场中形成涡旋 / 湍流结构）", allPass)
        allPass = Assert(maxSpeed > tunnel.FreestreamVelocity * 0.5,
                         "最大速度应达到来流量级（来流有效建立）", allPass)
        allPass = Assert(wakeDeficit > 0.0, "下游尾流亏损应 > 0（模型阻挡形成尾流）", allPass)

        ' ---- 5. 打印中截面速度大小切片（直观观察尾流）----
        Console.WriteLine()
        Console.WriteLine($"[5] 中截面 (k={f.Nz \ 2}) 速度大小切片（左入流 → 右出流，模型处为空洞）：")
        PrintSpeedSlice(tunnel, f.Nz \ 2)

        ' ---- 结论 ----
        Console.WriteLine()
        Console.WriteLine(New String("="c, 70))
        If allPass Then
            Console.WriteLine("  结果：全部断言通过 [PASS]")
        Else
            Console.WriteLine("  结果：存在失败断言 [FAIL]")
        End If
        Console.WriteLine(New String("="c, 70))

        Return allPass

    End Function

#Region "断言与辅助"

    ''' <summary>打印单条断言结果并累积总体状态。</summary>
    Private Function Assert(condition As Boolean, message As String, prev As Boolean) As Boolean
        Console.WriteLine($"    [{If(condition, "PASS", "FAIL")}] {message}")
        Return prev AndAlso condition
    End Function

    ''' <summary>在计算空间中查找模型最低固体体素的 j 坐标；无固体返回 -1。</summary>
    Private Function FindLowestSolidJ(f As FluidField) As Integer
        For j = 0 To f.Ny - 1
            For i = 0 To f.Nx - 1
                For k = 0 To f.Nz - 1
                    If Not f.IsActive(i, j, k) Then
                        Return j
                    End If
                Next
            Next
        Next
        Return -1
    End Function

    ''' <summary>统计计算空间中的固体（非活动）体素数。</summary>
    Private Function CountSolid(f As FluidField) As Integer
        Dim count = 0
        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    If Not f.IsActive(i, j, k) Then count += 1
                Next
            Next
        Next
        Return count
    End Function

    ''' <summary>打印指定 k 平面的速度大小切片（i 横向，j 纵向）。</summary>
    Private Sub PrintSpeedSlice(tunnel As WindTunnel, k As Integer)
        Dim f = tunnel.Field
        Dim chars = " .:-=+*#%@".ToCharArray()
        Dim maxS = tunnel.ComputeMaxSpeed()
        If maxS <= 0.000001 Then maxS = 0.000001
        For j = f.Ny - 1 To 0 Step -1
            Console.Write("    ")
            For i = 0 To f.Nx - 1
                If Not f.IsActive(i, j, k) Then
                    Console.Write("X"c)   ' 固体（模型本体）
                Else
                    Dim u = f.U(i, j, k), v = f.V(i, j, k), w = f.W(i, j, k)
                    Dim spd = std.Sqrt(u * u + v * v + w * w)
                    Dim normalized = spd / maxS
                    Dim idx = CInt(std.Min(chars.Length - 1, std.Max(0, normalized * (chars.Length - 1))))
                    Console.Write(chars(idx))
                End If
            Next
            Console.WriteLine()
        Next
    End Sub

#End Region

End Module
