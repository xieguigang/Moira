' /********************************************************************************/
'
'   Program.vb
'
'   CFD 引擎演示入口
'
'   演示内容：
'       1. 创建一个 24×24×24 的发酵罐，放置旋转搅拌器
'       2. 在搅拌器位置注入示踪剂（密度）
'       3. 运行 80 个时间步的搅拌模拟
'       4. 打印叶轮高度水平切片的速度场、压力场、密度场
'       5. 导出完整三维结果到 VTK 文件（可用 ParaView 打开）
'       6. 打印统计信息（最大速度、平均速度等）
'
'   运行：
'       dotnet run
'   或编译后：
'       dotnet CFDEngine.dll
'
' /********************************************************************************/

Imports CFDEngine.CFDEngine
Imports std = System.Math

Module Program

    Sub Main()

        Console.WriteLine("="c, 70)
        Console.WriteLine("  基础 CFD 引擎演示 —— 发酵罐搅拌模拟")
        Console.WriteLine("  Basic CFD Engine Demo - Fermentation Tank Stirring Simulation")
        Console.WriteLine("="c, 70)
        Console.WriteLine()

        ' ---- 1. 创建引擎 ----
        Dim nx, ny, nz As Integer
        nx = 48 : ny = 48 : nz = 48

        Console.WriteLine($"[1] 创建 {nx}×{ny}×{nz} 发酵罐，放置旋转搅拌器...")
        Dim engine = FluidSim.CreateDefault(nx, ny, nz, angularVelocity:=4.0)

        Dim tank = engine.Tank
        Console.WriteLine($"    搅拌器位置: 轴=({tank.Stirrer.CenterX:F1}, {tank.Stirrer.CenterY:F1}), " &
                          $"高度={tank.Stirrer.ZCenter:F1}, 半径={tank.Stirrer.Radius:F1}")
        Console.WriteLine($"    角速度: {tank.Stirrer.AngularVelocity} rad/单位时间")
        Console.WriteLine($"    运动粘度: {tank.Viscosity}")
        Console.WriteLine()

        ' ---- 2. 注入示踪剂 ----
        Console.WriteLine("[2] 在搅拌器位置注入示踪剂（密度=1.0）...")
        engine.InjectDyeAtStirrer(1.0)
        Console.WriteLine()

        ' ---- 3. 运行模拟 ----
        Dim steps As Integer = 80
        Dim dt As Double = 0.1
        Console.WriteLine($"[3] 运行 {steps} 个时间步，dt={dt}...")
        Console.WriteLine()

        Dim startTime = DateTime.Now
        engine.Run(steps, dt,
                   Sub(stepIdx, time)
                       If stepIdx Mod 10 = 0 OrElse stepIdx = steps Then
                           Console.WriteLine($"    步 {stepIdx,3} / {steps}  时间={time:F2}  " &
                                             $"最大速度={MaxSpeed(tank):F3}")
                       End If
                   End Sub)
        Dim elapsed = (DateTime.Now - startTime).TotalSeconds
        Console.WriteLine($"    完成！耗时 {elapsed:F2} 秒")
        Console.WriteLine()

        ' ---- 4. 打印切片 ----
        Dim kSlice = CInt(std.Floor(tank.Stirrer.ZCenter))
        Console.WriteLine($"[4] 打印叶轮高度水平切片 (k={kSlice}) 的速度场：")
        PrintVelocitySlice(tank, kSlice)
        Console.WriteLine()

        Console.WriteLine($"[5] 打印叶轮高度水平切片 (k={kSlice}) 的密度场（示踪剂分布）：")
        PrintDensitySlice(tank, kSlice)
        Console.WriteLine()

        Console.WriteLine($"[6] 打印垂直纵切面 (j={ny \ 2}) 的速度场：")
        PrintVerticalSlice(tank, ny \ 2)
        Console.WriteLine()

        ' ---- 5. 打印统计 ----
        Console.WriteLine("[7] 全场统计：")
        PrintStatistics(tank)
        Console.WriteLine()

        ' ---- 6. 打印若干体素详情 ----
        Console.WriteLine("[8] 采样体素详情（速度、压力、密度）：")
        PrintSampleVoxels(tank)
        Console.WriteLine()

        ' ---- 7. 导出 VTK ----
        Dim vtkPath = "fermentation_tank_stirring.vtk"
        Console.WriteLine($"[9] 导出 VTK 文件: {vtkPath}")
        Dim vtkFull = System.IO.Path.Combine(System.AppContext.BaseDirectory, vtkPath)
        VTKExporter.Export(tank, vtkFull)
        Console.WriteLine($"    已保存: {vtkFull}")
        Console.WriteLine("    可用 ParaView (https://www.paraview.org) 打开查看三维结果。")
        Console.WriteLine()

        ' 同时导出一份到 download 目录
        Dim downloadDir = "/home/z/my-project/download/CFDEngine"
        If System.IO.Directory.Exists(downloadDir) Then
            Dim vtkCopy = System.IO.Path.Combine(downloadDir, vtkPath)
            VTKExporter.Export(tank, vtkCopy)
            Console.WriteLine($"    副本已保存: {vtkCopy}")
        End If

        Console.WriteLine("="c, 70)
        Console.WriteLine("  演示完成。")
        Console.WriteLine("="c, 70)

    End Sub

#Region "辅助打印函数"

    ''' <summary>计算全场最大速度</summary>
    Function MaxSpeed(tank As FermentationTank) As Double
        Dim f = tank.Field
        Dim maxS = 0.0
        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    Dim u = f.U(i, j, k)
                    Dim v = f.V(i, j, k)
                    Dim w = f.W(i, j, k)
                    Dim s = std.Sqrt(u * u + v * v + w * w)
                    If s > maxS Then maxS = s
                Next
            Next
        Next
        Return maxS
    End Function

    ''' <summary>打印水平切片的速度向量（用箭头字符简化表示）</summary>
    Sub PrintVelocitySlice(tank As FermentationTank, k As Integer)
        Dim f = tank.Field
        Dim stepSize = 2  ' 每隔2格打印一个，避免太密
        Console.WriteLine("    速度方向（→←↑↓ 等），长度反映速度大小：")
        For i = 0 To f.Nx - 1 Step stepSize
            Console.Write("    ")
            For j = 0 To f.Ny - 1 Step stepSize
                Dim u = f.U(i, j, k)
                Dim v = f.V(i, j, k)
                Dim spd = std.Sqrt(u * u + v * v)
                If spd < 0.05 Then
                    Console.Write("· ")
                Else
                    ' 用角度判断方向
                    Dim angle = std.Atan2(v, u) * 180.0 / std.PI  ' -180~180
                    Dim ch As Char
                    If angle >= -22.5 AndAlso angle < 22.5 Then
                        ch = "→"
                    ElseIf angle >= 22.5 AndAlso angle < 67.5 Then
                        ch = "↗"
                    ElseIf angle >= 67.5 AndAlso angle < 112.5 Then
                        ch = "↑"
                    ElseIf angle >= 112.5 AndAlso angle < 157.5 Then
                        ch = "↖"
                    ElseIf angle >= 157.5 OrElse angle < -157.5 Then
                        ch = "←"
                    ElseIf angle >= -157.5 AndAlso angle < -112.5 Then
                        ch = "↙"
                    ElseIf angle >= -112.5 AndAlso angle < -67.5 Then
                        ch = "↓"
                    Else
                        ch = "↘"
                    End If
                    Console.Write(ch & " ")
                End If
            Next
            Console.WriteLine()
        Next
    End Sub

    ''' <summary>打印水平切片的密度场（用字符浓度表示）</summary>
    Sub PrintDensitySlice(tank As FermentationTank, k As Integer)
        Dim f = tank.Field
        Dim chars = " .:-=+*#%@".ToCharArray()
        ' 先找全场最大密度，用于归一化显示
        Dim maxD = 0.000001
        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For kk = 0 To f.Nz - 1
                    If f.Density(i, j, kk) > maxD Then maxD = f.Density(i, j, kk)
                Next
            Next
        Next
        Console.WriteLine("    密度（空=0，满=@），最大值=" & maxD.ToString("F4") & "：")
        For i = 0 To f.Nx - 1
            Console.Write("    ")
            For j = 0 To f.Ny - 1
                Dim d = f.Density(i, j, k)
                Dim normalized = d / maxD
                Dim idx = CInt(std.Min(chars.Length - 1, std.Max(0, normalized * (chars.Length - 1))))
                Console.Write(chars(idx))
            Next
            Console.WriteLine()
        Next
    End Sub

    ''' <summary>打印垂直纵切面的速度大小</summary>
    Sub PrintVerticalSlice(tank As FermentationTank, j As Integer)
        Dim f = tank.Field
        Dim chars = " .:-=+*#%@".ToCharArray()
        ' 先找全场最大速度，用于归一化显示
        Dim maxS = 0.000001
        For i = 0 To f.Nx - 1
            For jj = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    Dim s = std.Sqrt(f.U(i, jj, k) ^ 2 + f.V(i, jj, k) ^ 2 + f.W(i, jj, k) ^ 2)
                    If s > maxS Then maxS = s
                Next
            Next
        Next
        Console.WriteLine("    速度大小（k 从下到上，i 从左到右），最大值=" & maxS.ToString("F2") & "：")
        For k = f.Nz - 1 To 0 Step -1
            Console.Write("    ")
            For i = 0 To f.Nx - 1
                Dim u = f.U(i, j, k)
                Dim v = f.V(i, j, k)
                Dim w = f.W(i, j, k)
                Dim spd = std.Sqrt(u * u + v * v + w * w)
                Dim normalized = spd / maxS
                Dim idx = CInt(std.Min(chars.Length - 1, std.Max(0, normalized * (chars.Length - 1))))
                Console.Write(chars(idx))
            Next
            Console.WriteLine()
        Next
    End Sub

    ''' <summary>打印全场统计信息</summary>
    Sub PrintStatistics(tank As FermentationTank)
        Dim f = tank.Field
        Dim maxSpeed = 0.0, sumSpeed = 0.0
        Dim maxPressure = Double.NegativeInfinity, minPressure = Double.PositiveInfinity
        Dim sumDensity = 0.0, maxDensity = 0.0
        Dim count = 0

        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    Dim u = f.U(i, j, k)
                    Dim v = f.V(i, j, k)
                    Dim w = f.W(i, j, k)
                    Dim spd = std.Sqrt(u * u + v * v + w * w)
                    If spd > maxSpeed Then maxSpeed = spd
                    sumSpeed += spd

                    Dim p = f.Pressure(i, j, k)
                    If p > maxPressure Then maxPressure = p
                    If p < minPressure Then minPressure = p

                    Dim d = f.Density(i, j, k)
                    sumDensity += d
                    If d > maxDensity Then maxDensity = d

                    count += 1
                Next
            Next
        Next

        Console.WriteLine($"    网格: {f.Nx}×{f.Ny}×{f.Nz} = {count} 个体素")
        Console.WriteLine($"    最大速度:   {maxSpeed:F4}")
        Console.WriteLine($"    平均速度:   {sumSpeed / count:F4}")
        Console.WriteLine($"    压力范围:   [{minPressure:F4}, {maxPressure:F4}]")
        Console.WriteLine($"    最大密度:   {maxDensity:F4}")
        Console.WriteLine($"    平均密度:   {sumDensity / count:F6}")
        Console.WriteLine($"    模拟时间:   {tank.Time:F2}")
        Console.WriteLine($"    时间步数:   {tank.StepCount}")
    End Sub

    ''' <summary>打印若干采样体素的详细信息</summary>
    Sub PrintSampleVoxels(tank As FermentationTank)
        Dim f = tank.Field
        Dim samples = {
            (f.Nx \ 2, f.Ny \ 2, CInt(std.Floor(tank.Stirrer.ZCenter))),  ' 叶轮中心
            (f.Nx \ 2, f.Ny \ 2, f.Nz \ 2),                                ' 罐中央
            (f.Nx \ 2, f.Ny \ 2, f.Nz - 2),                                ' 罐顶
            (f.Nx \ 2, f.Ny \ 2, 1),                                       ' 罐底
            (1, f.Ny \ 2, CInt(std.Floor(tank.Stirrer.ZCenter))),         ' 叶轮高度侧壁
            (f.Nx - 2, f.Ny \ 2, CInt(std.Floor(tank.Stirrer.ZCenter)))   ' 叶轮高度对侧壁
        }
        Console.WriteLine("    {0,-12} {1,-14} {2,-10} {3,-10} {4,-10} {5,-12} {6,-10}",
                          "位置(i,j,k)", "速度(u,v,w)", "速度大小", "压力", "密度", "说明", "")
        Dim labels = {"叶轮中心", "罐中央", "罐顶", "罐底", "侧壁(左)", "侧壁(右)"}
        For idx = 0 To samples.Length - 1
            Dim s = samples(idx)
            Dim vox = tank.GetVoxel(s.Item1, s.Item2, s.Item3)
            Console.WriteLine("    ({0,2},{1,2},{2,2})     ({3:F2},{4:F2},{5:F2}) {6:F4}      {7:F4}    {8:F4}      {9}",
                              s.Item1, s.Item2, s.Item3,
                              vox.U, vox.V, vox.W, vox.Speed, vox.Pressure, vox.Density, labels(idx))
        Next
    End Sub

#End Region

End Module
