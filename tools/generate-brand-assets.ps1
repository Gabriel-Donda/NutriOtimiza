# Gera os recursos visuais do NutriOtimiza a partir do logo da Nutriport (branding/nutriport-logo.png):
#   src/OtimizaPC.App/Assets/app.ico      → ícone do executável e da janela (16 a 256 px)
#   src/OtimizaPC.App/Assets/app-256.png  → logo em quadrado grafite (barra de título e menu)
#   installer/wizard-large.bmp / -small.bmp (+ versões 2x) → imagens do instalador
# Uso: pwsh tools/generate-brand-assets.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$logoPath = Join-Path $root 'branding/nutriport-logo.png'
$assets = Join-Path $root 'src/OtimizaPC.App/Assets'
$installer = Join-Path $root 'installer'
New-Item -ItemType Directory -Force -Path $assets, $installer | Out-Null

# Cores da Nutriport (extraídas do logo)
$graphite = [System.Drawing.Color]::FromArgb(255, 0x20, 0x20, 0x20)
$gold = [System.Drawing.Color]::FromArgb(255, 0xD2, 0xC0, 0x68)
$orange = [System.Drawing.Color]::FromArgb(255, 0xF5, 0x86, 0x34)

$logo = [System.Drawing.Image]::FromFile($logoPath)

function New-Graphics($bitmap) {
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return $g
}

# Desenha o logo centralizado dentro do retângulo, mantendo a proporção
function Draw-Logo($g, [float]$x, [float]$y, [float]$w, [float]$h) {
    $scale = [Math]::Min($w / $logo.Width, $h / $logo.Height)
    $lw = $logo.Width * $scale; $lh = $logo.Height * $scale
    $g.DrawImage($logo, $x + ($w - $lw) / 2, $y + ($h - $lh) / 2, $lw, $lh)
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Quadrado grafite arredondado com o logo (ícone)
function New-Tile([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::Transparent)
    $margin = [Math]::Max(1, $size * 0.03)
    $g.FillPath((New-Object System.Drawing.SolidBrush $graphite), (New-RoundedPath $margin $margin ($size - 2 * $margin) ($size - 2 * $margin) ($size * 0.2)))
    # Em tamanhos pequenos o logo ocupa mais área, para continuar legível
    $pad = if ($size -le 32) { $size * 0.08 } else { $size * 0.14 }
    Draw-Logo $g $pad $pad ($size - 2 * $pad) ($size - 2 * $pad)
    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bitmap) {
    $ms = New-Object System.IO.MemoryStream
    $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# ---------- Ícone (PNG dentro de ICO) ----------
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { $t = New-Tile $_; $bytes = Get-PngBytes $t; $t.Dispose(); , $bytes }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app-256.png'), $frames[-1])

# ---------- Imagens do instalador (BMP 24 bits, sem transparência) ----------
function Save-Bmp($bitmap, [string]$path) {
    $flat = New-Object System.Drawing.Bitmap $bitmap.Width, $bitmap.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($flat)
    $g.DrawImage($bitmap, 0, 0, $bitmap.Width, $bitmap.Height)
    $g.Dispose()
    $flat.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $flat.Dispose()
}

# Painel lateral das páginas de boas-vindas/conclusão: grafite, logo, nome e faixa laranja
function New-WizardLarge([float]$k) {
    $width = [int](164 * $k); $height = [int](314 * $k)
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = New-Graphics $bmp
    $g.Clear($graphite)
    Draw-Logo $g (30 * $k) (52 * $k) (104 * $k) (130 * $k)

    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', (15 * $k), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $nutriSize = $g.MeasureString('Nutri', $font)
    $otimizaSize = $g.MeasureString('Otimiza', $font)
    $textX = ($width - ($nutriSize.Width + $otimizaSize.Width - 6 * $k)) / 2
    $textY = 206 * $k
    $g.DrawString('Nutri', $font, (New-Object System.Drawing.SolidBrush $gold), $textX, $textY)
    $g.DrawString('Otimiza', $font, (New-Object System.Drawing.SolidBrush $orange), $textX + $nutriSize.Width - 6 * $k, $textY)

    $small = New-Object System.Drawing.Font 'Segoe UI', (9 * $k), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('Otimização, limpeza e diagnóstico', $small, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xB8, 0xB8, 0xB8))), (New-Object System.Drawing.RectangleF 0, (232 * $k), $width, (20 * $k)), $format)

    $g.FillRectangle((New-Object System.Drawing.SolidBrush $orange), 0, $height - 6 * $k, $width, 6 * $k)
    $g.Dispose()
    return $bmp
}

function New-WizardSmall([float]$k) {
    $size = [int](55 * $k)
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::White)
    $tile = New-Tile $size
    $g.DrawImage($tile, 0, 0, $size, $size)
    $tile.Dispose()
    $g.Dispose()
    return $bmp
}

foreach ($k in 1, 2) {
    $suffix = if ($k -eq 1) { '' } else { '-2x' }
    $large = New-WizardLarge $k; Save-Bmp $large (Join-Path $installer "wizard-large$suffix.bmp"); $large.Dispose()
    $smallBmp = New-WizardSmall $k; Save-Bmp $smallBmp (Join-Path $installer "wizard-small$suffix.bmp"); $smallBmp.Dispose()
}

$logo.Dispose()
"Recursos da marca gerados em $assets e $installer"
