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

function Keys($el, $vk, $n) {
    $el.SetFocus()
    Start-Sleep -Milliseconds 300
    for ($i = 0; $i -lt $n; $i++) {
        [K]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
        [K]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 20
    }
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
Start-Sleep -Seconds 35

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# 1) 截面：启用 + X 轴位置 18（网格中线，应切掉一半罐体）
Toggle "chkSection"
Start-Sleep -Milliseconds 600
Keys (Find-ById $root "trackSectionPos") 0x27 18
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_section_mid.png"

# 2) 阈值 0.9（密度场几乎全隐藏 → 空视口）
Keys (Find-ById $root "trackThreshold") 0x27 90
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_threshold90.png"

# 3) 阈值回 0（恢复完整视图）
Keys (Find-ById $root "trackThreshold") 0x25 90
Start-Sleep -Seconds 5
Shot $hwnd "g:\Moira\src\CDFDxCanvas\shot_threshold_reset.png"

Stop-Process -Id $p.Id -Force
Write-Output "DONE"
