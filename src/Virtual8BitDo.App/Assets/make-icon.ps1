param([string]$Out, [string]$PreviewOut)
Add-Type -AssemblyName System.Drawing

function C([string]$hex, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, [System.Drawing.ColorTranslator]::FromHtml($hex)) }
function P([float]$x, [float]$y) { [System.Drawing.PointF]::new($x, $y) }

# Contorno do corpo do Ultimate 2 (metade direita em coordenadas 0..1; a esquerda é espelhada).
function BodyPath([float]$S) {
    $right = @(
        @((P 0.50 0.215)),
        @((P 0.62 0.215), (P 0.70 0.205), (P 0.80 0.215)),
        @((P 0.90 0.225), (P 0.955 0.30), (P 0.965 0.42)),
        @((P 0.975 0.56), (P 0.975 0.70), (P 0.935 0.80)),
        @((P 0.895 0.885), (P 0.80 0.895), (P 0.755 0.835)),
        @((P 0.715 0.78), (P 0.69 0.715), (P 0.62 0.705)),
        @((P 0.57 0.70), (P 0.53 0.70), (P 0.50 0.70))
    )
    $pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
    $pts.Add($right[0][0])
    for ($i = 1; $i -lt $right.Count; $i++) { foreach ($p in $right[$i]) { $pts.Add($p) } }
    # espelha a metade direita, em ordem inversa, para fechar a forma
    $mirror = New-Object System.Collections.Generic.List[System.Drawing.PointF]
    for ($i = $pts.Count - 2; $i -ge 0; $i--) { $mirror.Add((P (1 - $pts[$i].X) $pts[$i].Y)) }
    $all = $pts + $mirror
    $scaled = $all | ForEach-Object { P ($_.X * $S) ($_.Y * $S) }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBeziers([System.Drawing.PointF[]]$scaled)
    $path.CloseFigure()
    return $path
}

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max($r * 2, 0.01)
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Circle($g, $brush, [float]$cx, [float]$cy, [float]$r) { $g.FillEllipse($brush, $cx - $r, $cy - $r, $r * 2, $r * 2) }

function Stick($g, [float]$S, [float]$cx, [float]$cy, [bool]$detail) {
    $cx *= $S; $cy *= $S
    # brilho do LED (halo)
    if ($detail) {
        foreach ($k in @(@(0.150, 40), @(0.132, 70))) {
            Circle $g (New-Object System.Drawing.SolidBrush (C '#FFB020' $k[1])) $cx $cy ($S * $k[0])
        }
    }
    Circle $g (New-Object System.Drawing.SolidBrush (C '#FFA51F')) $cx $cy ($S * 0.118)   # anel laranja
    Circle $g (New-Object System.Drawing.SolidBrush (C '#FFE07A')) $cx $cy ($S * 0.100)   # centro do anel (mais claro)
    Circle $g (New-Object System.Drawing.SolidBrush (C '#55594F')) $cx $cy ($S * 0.088)   # base do stick
    $cap = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((P ($cx - $S*0.07) ($cy - $S*0.07))), ((P ($cx + $S*0.07) ($cy + $S*0.07))), (C '#8A8E86'), (C '#4E524A')
    Circle $g $cap $cx $cy ($S * 0.072)                                                    # cabeça do stick
    if ($detail) { Circle $g (New-Object System.Drawing.SolidBrush (C '#6E7269')) $cx $cy ($S * 0.050) }
}

function Draw-Icon([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $detail = $S -ge 48
    $tiny = $S -le 24

    # Bumpers (atrás do corpo)
    $bump = New-Object System.Drawing.SolidBrush (C '#5FA51B')
    $g.FillPath($bump, (RoundRect ($S*0.12) ($S*0.165) ($S*0.27) ($S*0.10) ($S*0.045)))
    $g.FillPath($bump, (RoundRect ($S*0.61) ($S*0.165) ($S*0.27) ($S*0.10) ($S*0.045)))

    # Corpo verde-limão com degradê e contorno
    $body = BodyPath $S
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((P 0 ($S*0.15))), ((P 0 ($S*0.95))), (C '#A2E640'), (C '#66B01B')
    $grad.WrapMode = 'TileFlipXY'
    $g.FillPath($grad, $body)
    $pen = New-Object System.Drawing.Pen (C '#3F7A0E'), ([Math]::Max(1, $S * 0.012))
    $g.DrawPath($pen, $body)

    # Analógicos com anel de LED (esquerdo em cima, direito embaixo, como no Ultimate 2)
    Stick $g $S 0.255 0.385 $detail
    Stick $g $S 0.625 0.585 $detail

    # D-pad grafite
    $dark = New-Object System.Drawing.SolidBrush (C '#4A4E47')
    $cx = $S*0.385; $cy = $S*0.585; $a = $S*0.072; $l = $S*0.215
    $g.FillPath($dark, (RoundRect ($cx - $l/2) ($cy - $a/2) $l $a ($S*0.015)))
    $g.FillPath($dark, (RoundRect ($cx - $a/2) ($cy - $l/2) $a $l ($S*0.015)))

    # ABXY grafite com letras verde-claras
    $bx = $S*0.775; $by = $S*0.375; $o = $S*0.078; $r = $S*0.046
    if ($tiny) { $r = $S*0.055; $o = $S*0.085 }
    $letters = @(@(0, -1, 'Y'), @(1, 0, 'B'), @(0, 1, 'A'), @(-1, 0, 'X'))
    foreach ($d in $letters) {
        $x = $bx + $d[0] * $o; $y = $by + $d[1] * $o
        Circle $g $dark $x $y $r
        if ($S -ge 64) {
            $font = New-Object System.Drawing.Font 'Segoe UI', ([float]($S * 0.050)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
            $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
            $rect = [System.Drawing.RectangleF]::new(($x - $r), ($y - $r + $S*0.003), ($r*2), ($r*2))
            $g.DrawString($d[2], $font, (New-Object System.Drawing.SolidBrush (C '#B8F06A')), $rect, $sf)
        }
    }

    # Botão Home central com anel claro, e botões menores
    if ($S -ge 32) {
        Circle $g (New-Object System.Drawing.SolidBrush (C '#E8FFD0')) ($S*0.5) ($S*0.265) ($S*0.040)
        Circle $g (New-Object System.Drawing.SolidBrush (C '#7CC72A')) ($S*0.5) ($S*0.265) ($S*0.028)
    }
    if ($detail) {
        $small = New-Object System.Drawing.SolidBrush (C '#5FA51B')
        foreach ($sx in 0.40, 0.60) { Circle $g $small ($S*$sx) ($S*0.265) ($S*0.022) }
        foreach ($sx in 0.435, 0.565) { Circle $g $small ($S*$sx) ($S*0.355) ($S*0.022) }
        $g.FillPath($small, (RoundRect ($S*0.475) ($S*0.43) ($S*0.05) ($S*0.03) ($S*0.015)))
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    ,$ms.ToArray()
    $b.Dispose()
}

$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $w.Write($data) }
$w.Close()

# Folha de prévia: todos os tamanhos, em fundo escuro e claro (os pequenos ampliados 4x para inspeção)
$sheet = New-Object System.Drawing.Bitmap 900, 640
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.FillRectangle((New-Object System.Drawing.SolidBrush (C '#202020')), 0, 0, 900, 320)
$g.FillRectangle((New-Object System.Drawing.SolidBrush (C '#F3F3F3')), 0, 320, 900, 320)
foreach ($row in 0, 1) {
    $y0 = 20 + $row * 320
    $g.DrawImage((Draw-Icon 256), 20, $y0, 256, 256)
    $x = 300
    foreach ($s in 64, 48, 32, 24, 16) {
        $g.DrawImage((Draw-Icon $s), $x, $y0, $s, $s)
        $g.InterpolationMode = 'NearestNeighbor'
        $g.DrawImage((Draw-Icon $s), $x, $y0 + 80, $s * 2, $s * 2)
        $x += [Math]::Max($s * 2, 40) + 20
    }
}
$sheet.Save($PreviewOut, [System.Drawing.Imaging.ImageFormat]::Png)
"OK"
