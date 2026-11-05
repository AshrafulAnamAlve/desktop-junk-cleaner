# Draws SafeClean's app icon (the blue shield from the sidebar) and writes it to
# src\SafeClean.App\Resources\SafeClean.ico. Uses only what ships with Windows, so no icon tool is needed.
# It writes that one file and refuses to replace it unless -Force is given. It deletes nothing.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repo 'src\SafeClean.App\Resources\SafeClean.ico'
if ((Test-Path -LiteralPath $target) -and -not $Force) { throw "$target already exists. Use -Force to replace it." }

function New-RoundedSquare([float]$size, [float]$radius) {
  $d = $radius * 2
  $p = New-Object Drawing.Drawing2D.GraphicsPath
  $p.AddArc(0, 0, $d, $d, 180, 90)
  $p.AddArc($size - $d, 0, $d, $d, 270, 90)
  $p.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
  $p.AddArc(0, $size - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function New-IconFrame([int]$size) {
  $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([Drawing.Color]::Transparent)

  $background = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#1B2B40'))
  $shieldBrush = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#5AA2FF'))
  $square = New-RoundedSquare $size ($size * 0.22)
  $g.FillPath($background, $square)

  # The shield, in a 0..1 box, so every size has the same shape.
  $s = [float]$size
  $shield = New-Object Drawing.Drawing2D.GraphicsPath
  $shield.AddBezier(0.50 * $s, 0.13 * $s, 0.60 * $s, 0.20 * $s, 0.71 * $s, 0.24 * $s, 0.81 * $s, 0.25 * $s)
  $shield.AddBezier(0.81 * $s, 0.25 * $s, 0.83 * $s, 0.52 * $s, 0.74 * $s, 0.74 * $s, 0.50 * $s, 0.88 * $s)
  $shield.AddBezier(0.50 * $s, 0.88 * $s, 0.26 * $s, 0.74 * $s, 0.17 * $s, 0.52 * $s, 0.19 * $s, 0.25 * $s)
  $shield.AddBezier(0.19 * $s, 0.25 * $s, 0.29 * $s, 0.24 * $s, 0.40 * $s, 0.20 * $s, 0.50 * $s, 0.13 * $s)
  $shield.CloseFigure()
  $g.FillPath($shieldBrush, $shield)

  # A tick for "safe". At very small sizes it would only be a smudge, so the plain shield is kept there.
  if ($size -ge 24) {
    $pen = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#0B1524')), ([float][Math]::Max(2, 0.085 * $s))
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@(
      (New-Object Drawing.PointF (0.35 * $s), (0.49 * $s)),
      (New-Object Drawing.PointF (0.46 * $s), (0.60 * $s)),
      (New-Object Drawing.PointF (0.66 * $s), (0.38 * $s)))
    $g.DrawLines($pen, $points)
    $pen.Dispose()
  }

  $g.Dispose(); $background.Dispose(); $shieldBrush.Dispose(); $square.Dispose(); $shield.Dispose()
  $stream = New-Object IO.MemoryStream
  $bmp.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return ,$stream.ToArray()
}

# An .ico file: a 6-byte header, a 16-byte entry per size, then each image (PNG frames work on Windows 10 and 11).
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) { ,(New-IconFrame $size) }

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
  $w.Write([byte]$dimension); $w.Write([byte]$dimension)
  $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
  $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $w.Write([byte[]]$frame) }
$w.Flush()
[IO.File]::WriteAllBytes($target, $out.ToArray())
"Wrote $target ($($out.Length) bytes, sizes: $($sizes -join ', '))"
