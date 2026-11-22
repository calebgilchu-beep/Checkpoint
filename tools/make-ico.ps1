# Crea app.ico (varios tamaños en PNG) a partir de una captura de 256 px del icono.
# Uso: pwsh tools/make-ico.ps1 -Source icon256.png -Out src/ListaJuegos.App/app.ico
param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Out)
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile($Source)
$crop = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 60; $d = $r * 2
$path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc(256 - $d, 0, $d, $d, 270, 90)
$path.AddArc(256 - $d, 256 - $d, $d, $d, 0, 90); $path.AddArc(0, 256 - $d, $d, $d, 90, 90); $path.CloseFigure()
$g.SetClip($path)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, 256, 256), (New-Object System.Drawing.Rectangle 0, 0, 256, 256), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = 'HighQualityBicubic'; $gg.SmoothingMode = 'AntiAlias'; $gg.PixelOffsetMode = 'HighQuality'
    $gg.DrawImage($crop, 0, 0, $s, $s); $gg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$crop.Dispose()

$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()
Write-Output "app.ico: $((Get-Item $Out).Length) bytes, $($sizes.Count) tamaños"
