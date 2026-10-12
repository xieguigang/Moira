' IL 转储探针：把 EulerianToCartesianResampler.AutoConfigure 的真实 IL 打印出来，
' 用于确认"实际编译进 DLL 的代码"与源文件是否一致。
'
' 用法：StreamlineDiag il <CFDEngine.dll 路径>

Imports System.Reflection
Imports System.Reflection.Emit
Imports System.Text

Module IlDump

    Public Function RunIlDump(asmPath As String, typeName As String, methodName As String) As Integer
        Console.WriteLine($"=== IL 转储：{asmPath} :: {typeName}.{methodName} ===")

        Dim asm As Assembly = Assembly.LoadFrom(asmPath)

        Console.WriteLine($"程序集 = {asm.FullName}")

        Dim t As Type = asm.GetType(typeName)

        If t Is Nothing Then
            Console.WriteLine($"找不到类型 {typeName}")
            Return 1
        End If

        For Each m As MethodInfo In t.GetMethods(BindingFlags.Instance Or BindingFlags.Public Or BindingFlags.NonPublic)
            If m.Name <> methodName Then Continue For

            Console.WriteLine("")
            Console.WriteLine($"--- 方法 {m} ---")

            Dim body As MethodBody = m.GetMethodBody()

            If body Is Nothing Then
                Console.WriteLine("(无方法体)")
                Continue For
            End If

            Dim il As Byte() = body.GetILAsByteArray()
            Dim sb As New StringBuilder()

            sb.Append("  IL 字节数 = " & il.Length & vbLf)
            sb.Append("  ")

            For i As Integer = 0 To il.Length - 1
                sb.Append(il(i).ToString("X2"))

                If (i + 1) Mod 32 = 0 Then sb.Append(vbLf & "  ")
            Next

            Console.WriteLine(sb.ToString())

            ' ---- 反汇编（用 OpCodes 双表查找）----
            Console.WriteLine("")
            Console.WriteLine("  反汇编：")

            Dim one As New Dictionary(Of Short, OpCode)
            Dim two As New Dictionary(Of Byte, OpCode)

            For Each f As Reflection.FieldInfo In GetType(OpCodes).GetFields(BindingFlags.Public Or BindingFlags.Static)
                Dim op As OpCode = CType(f.GetValue(Nothing), OpCode)

                If op.Size = 1 Then
                    one(op.Value) = op
                Else
                    two(CByte(op.Value And &HFF)) = op
                End If
            Next

            Dim p As Integer = 0

            While p < il.Length
                Dim offset As Integer = p
                Dim op As OpCode

                If il(p) = &HFE Then
                    op = two(il(p + 1))
                    p += 2
                Else
                    op = one(CShort(il(p)))
                    p += 1
                End If

                Dim operand As String = ""

                Select Case op.OperandType
                    Case OperandType.InlineNone
                        operand = ""
                    Case OperandType.ShortInlineI, OperandType.ShortInlineVar
                        operand = il(p).ToString()
                        p += 1
                    Case OperandType.InlineVar
                        operand = BitConverter.ToUInt16(il, p).ToString()
                        p += 2
                    Case OperandType.InlineI
                        operand = BitConverter.ToInt32(il, p).ToString()
                        p += 4
                    Case OperandType.InlineI8
                        operand = BitConverter.ToInt64(il, p).ToString()
                        p += 8
                    Case OperandType.ShortInlineR
                        operand = BitConverter.ToSingle(il, p).ToString("R")
                        p += 4
                    Case OperandType.InlineR
                        operand = BitConverter.ToDouble(il, p).ToString("R")
                        p += 8
                    Case OperandType.ShortInlineBrTarget
                        operand = "IL_" & (p + 1 + CInt(CSByte(il(p)))).ToString("X4")
                        p += 1
                    Case OperandType.InlineBrTarget
                        operand = "IL_" & (p + 4 + BitConverter.ToInt32(il, p)).ToString("X4")
                        p += 4
                    Case OperandType.InlineSwitch
                        Dim n As Integer = BitConverter.ToInt32(il, p)
                        p += 4 + 4 * n
                        operand = $"switch({n})"
                    Case OperandType.InlineString
                        Dim tok As Integer = BitConverter.ToInt32(il, p)
                        p += 4

                        Try
                            operand = """" & m.Module.ResolveString(tok) & """"
                        Catch
                            operand = $"strtok(0x{tok:X8})"
                        End Try
                    Case OperandType.InlineMethod, OperandType.InlineField, OperandType.InlineType, OperandType.InlineTok
                        Dim tok As Integer = BitConverter.ToInt32(il, p)
                        p += 4

                        Try
                            Dim mb As MethodBase = m.Module.ResolveMethod(tok)
                            operand = mb.DeclaringType.Name & "::" & mb.Name
                        Catch
                            Try
                                Dim fi As Reflection.FieldInfo = m.Module.ResolveField(tok)
                                operand = fi.DeclaringType.Name & "::" & fi.Name
                            Catch
                                operand = $"tok(0x{tok:X8})"
                            End Try
                        End Try
                    Case OperandType.InlineSig
                        operand = $"sig(0x{BitConverter.ToInt32(il, p):X8})"
                        p += 4
                    Case Else
                        operand = "?"
                        p += 4
                End Select

                Console.WriteLine($"    IL_{offset:X4}: {op.Name,-14} {operand}")
            End While
        Next

        Return 0
    End Function

End Module
