# Renders tnote.ico (T series style: blue glyph on transparent background) and a 256px icon.png preview
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'tnote.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Rounded($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

function Render([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $u = $s / 64.0
    $blue = [Drawing.Color]::FromArgb(58, 120, 180)

    # transparent background; enlarge glyph to fill the canvas
    $g.TranslateTransform(32*$u, 32*$u); $g.ScaleTransform(1.26, 1.26); $g.TranslateTransform(-32.5*$u, -31.5*$u)

    # selection box (outline only)
    $box = Rounded (10*$u) (10*$u) (34*$u) (34*$u) (5*$u)
    $g.DrawPath((New-Object Drawing.Pen $blue, ([Math]::Max(1.5, 4*$u))), $box)

    # series letter T inside the box
    $tb = New-Object Drawing.SolidBrush $blue
    $g.FillPath($tb, (Rounded (16.5*$u) (16*$u) (17*$u) (5*$u) (1.2*$u)))
    $g.FillPath($tb, (Rounded (22.5*$u) (16*$u) (5*$u) (19*$u) (1.2*$u)))

    # pencil overlapping bottom-right corner, tip pointing inward (up-left), transparent gap ring
    $st = $g.Save()
    $g.TranslateTransform(42.5*$u, 42.5*$u); $g.RotateTransform(45)
    $shape = New-Object Drawing.Drawing2D.GraphicsPath
    $shape.AddPolygon([Drawing.PointF[]]@(
        (New-Object Drawing.PointF (-14.5*$u), 0), (New-Object Drawing.PointF (-7*$u), (-5*$u)),
        (New-Object Drawing.PointF (13*$u), (-5*$u)), (New-Object Drawing.PointF (13*$u), (5*$u)),
        (New-Object Drawing.PointF (-7*$u), (5*$u))))
    $gap = New-Object Drawing.Pen ([Drawing.Color]::Transparent), ([Math]::Max(1.5, 6*$u)); $gap.LineJoin = 'Round'
    $g.CompositingMode = 'SourceCopy'; $g.DrawPath($gap, $shape); $g.CompositingMode = 'SourceOver'
    $bb = New-Object Drawing.SolidBrush $blue
    $g.FillPath($bb, $shape)
    # wood band (white) + lead tip (blue) + eraser ferrule line
    $g.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]@(
        (New-Object Drawing.PointF (-11*$u), (-2*$u)), (New-Object Drawing.PointF (-7.5*$u), (-4.4*$u)),
        (New-Object Drawing.PointF (-7.5*$u), (4.4*$u)), (New-Object Drawing.PointF (-11*$u), (2*$u))))
    if ($s -ge 24) {
        $wp = New-Object Drawing.Pen ([Drawing.Color]::White), ([Math]::Max(1, 2*$u))
        $g.DrawLine($wp, (6*$u), (-5*$u), (6*$u), (5*$u))
    }
    $g.Restore($st)
    $g.Dispose(); $bmp
}

# write ICO with PNG frames
$frames = foreach ($s in $sizes) {
    $ms = New-Object IO.MemoryStream; $bm = Render $s
    $bm.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bm.Save((Join-Path $PSScriptRoot 'icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $bm.Dispose(); ,@($s, $ms.ToArray())
}
$fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $d = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f[1].Length); $w.Write([uint32]$offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()
"wrote $out"
