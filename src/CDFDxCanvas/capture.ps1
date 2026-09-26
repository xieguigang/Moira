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

$exe = "g:\Moira\src\CDFDxCanvas\bin\Debug\net10.0-windows\CDFDxCanvas.exe"
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
Start-Sleep -Seconds 35   # 等 demo 数据全量加载

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

function Toggle($id) {
    $el = Find-ById $root $id
    if ($el -eq $null) { Write-Output "MISS $id"; return }
    $pat = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) {
        $pat.Toggle()
        Write-Output "TOGGLED $id"
    } else { Write-Output "NO TOGGLE $id" }
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class K {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@

function SetRange($id, $val) {
    $el = Find-ById $root $id
    if ($el -eq $null) { Write-Output "MISS $id"; return }
    $el.SetFocus()
    Start-Sleep -Milliseconds 300
    for ($i = 0; $i -lt $val; $i++) {
        [K]::keybd_event(0x27, 0, 0, [UIntPtr]::Zero)   # VK_RIGHT down
        [K]::keybd_event(0x27, 0, 2, [UIntPtr]::Zero)   # up
        Start-Sleep -Milliseconds 15
    }
    Write-Output "RANGE $id -> $val"
}

function SelectItem($id, $index) {
    $el = Find-ById $root $id
    if ($el -eq $null) { Write-Output "MISS $id"; return }
    $expand = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand)) {
        $expand.Expand()
        Start-Sleep -Milliseconds 400
        $items = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $i = 0
        foreach ($item in $items) {
            if ($i -eq $index) {
                $sel = $null
                if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) {
                    $sel.Select()
                }
                break
            }
            $i++
        }
        Start-Sleep -Milliseconds 300
        $expand.Collapse()
        Write-Output "SELECTED $id[$index]"
    } else { Write-Output "NO EXPAND $id" }
}

# 1) 拾取：直接对画布中心做一次真实鼠标点击（UIA Invoke 不触发拾取逻辑）
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class M {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@
$rect = New-Object W+RECT
[W]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$cx = [int](($rect.Left + $rect.Right) / 2); $cy = [int](($rect.Top + $rect.Bottom) / 2)
[M]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 150
[M]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 16
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_pick.png"
Write-Output "PICK DONE"

# 2) 显示箭头
Toggle "chkArrows"
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_arrows.png"

# 3) 横截面：启用 + Y 轴 + 位置 48
Toggle "chkSection"
Start-Sleep -Milliseconds 800
SetRange "trackSectionPos" 48
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_section.png"

# 4) 阈值 30%
SetRange "trackThreshold" 30
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_threshold.png"

# 5) 调色板切换 + 播放 6 秒
SelectItem "cboPalette" 6
Start-Sleep -Seconds 4
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_palette.png"

$play = Find-ById $root "btnPlay"
if ($play -ne $null) {
    $inv = $null
    if ($play.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke() }
}
Start-Sleep -Seconds 8
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_play.png"

Stop-Process -Id $p.Id -Force
Write-Output "ALL DONE"
