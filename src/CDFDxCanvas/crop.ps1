Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("g:\Moira\src\CDFDxCanvas\shot_frame20.png")
$rect = New-Object System.Drawing.Rectangle(0, 0, 500, 32)
$crop = New-Object System.Drawing.Bitmap(750, 48)
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0,0,750,48)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$crop.Save("g:\Moira\src\CDFDxCanvas\crop_title.png", [System.Drawing.Imaging.ImageFormat]::Png)
$src.Dispose(); $crop.Dispose()
Write-Output "OK"
