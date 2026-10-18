param([string]$OutDir)
Add-Type -AssemblyName System.Drawing
function RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function Render([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.ScaleTransform($size / 256.0, $size / 256.0)

  # tile with diagonal gradient
  $tile = RoundedRect 8 8 240 240 56
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(256, 256)),
    [System.Drawing.Color]::FromArgb(255, 30, 41, 59), [System.Drawing.Color]::FromArgb(255, 13, 148, 136))
  $g.FillPath($brush, $tile)

  # path: a dotted curve from a start dot to the click point
  $white = [System.Drawing.Color]::White
  $trail = New-Object System.Drawing.Pen($white, 15)
  $trail.StartCap = 'Round'; $trail.EndCap = 'Round'; $trail.DashCap = 'Round'
  $trail.DashPattern = [single[]]@(0.05, 1.35)
  $g.DrawBezier($trail, 58, 196, 50, 112, 128, 168, 152, 96)
  $dot = New-Object System.Drawing.SolidBrush($white)
  $g.FillEllipse($dot, 36, 174, 44, 44)

  # click ripples around the pointer tip
  foreach ($ring in @(@(30, 190), @(50, 100))) {
    $rp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($ring[1], 255, 255, 255), 8)
    $g.DrawEllipse($rp, 152 - $ring[0], 96 - $ring[0], 2 * $ring[0], 2 * $ring[0])
  }

  # pointer arrow, tip on the end of the path
  $g.TranslateTransform(0, -46)
  $arrow = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF(150, 142)), (New-Object System.Drawing.PointF(150, 226)),
    (New-Object System.Drawing.PointF(172, 205)), (New-Object System.Drawing.PointF(188, 240)),
    (New-Object System.Drawing.PointF(206, 232)), (New-Object System.Drawing.PointF(190, 198)),
    (New-Object System.Drawing.PointF(220, 198)))
  $outline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 15, 23, 42), 10)
  $outline.LineJoin = 'Round'
  $g.DrawPolygon($outline, $arrow)
  $g.FillPolygon([System.Drawing.Brushes]::White, $arrow)

  $g.Dispose()
  return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $bmp = Render $s
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
  $bmp.Dispose()
  , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $out.ToArray())
'done'
