$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class K {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@

function Shot($hwnd, $path) {
    Start-Sleep -Milliseconds 800
    $rect = New-Object W+RECT
    [W]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Find-ById($root, $id) {
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}

function Toggle($id) {
    $el = Find-ById $root $id
    $pat = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { $pat.Toggle() }
}

function SelectItemByText($id, $text) {
    $el = Find-ById $root $id
    $expand = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand)) {
        $expand.Expand(); Start-Sleep -Milliseconds 500
        $items = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($item in $items) {
            if ($item.Current.Name -like "*$text*") {
                $sel = $null
                if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select() }
                break
            }
        }
        Start-Sleep -Milliseconds 400
        $expand.Collapse()
        Write-Output "SELECT $id ~ $text"
        return
    }
    Write-Output "NO EXPAND $id"
}

function Keys($el, $vk, $n) {
    $el.SetFocus()
    Start-Sleep -Milliseconds 300
    for ($i = 0; $i -lt $n; $i++) {
        [K]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
        [K]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 20
    }
}

$exe = "g:\Moira\src\CFDEngine\test\bin\Debug\net10.0-windows\test.exe"
$p = Start-Process -FilePath $exe -PassThru

Start-Sleep -Seconds 3
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    $p.Refresh()
    if ($p.MainWindowHandle -ne 0) { $hwnd = $p.MainWindowHandle; break }
    Start-Sleep -Milliseconds 500
}
[W]::SetWindowPos($hwnd, [IntPtr]::Zero, 50, 50, 1200, 760, 0x0040) | Out-Null
[W]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 35

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# 1) 开启悬停提示 + 鼠标移到画布中某个体素上
Toggle "chkTooltip"
Start-Sleep -Milliseconds 500
$rect = New-Object W+RECT
[W]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$hx = $rect.Left + 620; $hy = $rect.Top + 330
[K]::SetCursorPos($hx, $hy) | Out-Null
Start-Sleep -Milliseconds 120
[K]::mouse_event(1, 2, 0, 0, [UIntPtr]::Zero)   # 相对移动触发 WM_MOUSEMOVE
Start-Sleep -Seconds 3
Shot $hwnd "g:\Moira\src\CFDEngine\test\uishot_tooltip.png"
Write-Output "TOOLTIP SHOT"

# 2) 点击拾取（验证 NRE 修复；若崩溃进程会退出）
[K]::SetCursorPos($hx, $hy) | Out-Null
Start-Sleep -Milliseconds 150
[K]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [K]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 12
if ($p.HasExited) { Write-Output "CRASHED AFTER PICK"; exit 1 }
Shot $hwnd "g:\Moira\src\CFDEngine\test\uishot_pick.png"
Write-Output "PICK OK"

# 3) 切片模式：单层 + X 轴位置 18
SelectItemByText "cboSectionMode" "切片模式"
Start-Sleep -Milliseconds 500
Keys (Find-ById $root "trackSectionPos") 0x27 18
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CFDEngine\test\uishot_slice.png"
Write-Output "SLICE SHOT"

Stop-Process -Id $p.Id -Force
Write-Output "DONE"
