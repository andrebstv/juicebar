# Gera o juicebar.ico: uma pilha em pe com "suco" laranja dentro.
# Cada tamanho e desenhado do zero (16, 20, 24, 32, 40, 48, 64, 256) para ficar nitido.
# Uso: powershell -ExecutionPolicy Bypass -File tools\gerar-icone.ps1
param([string]$Saida = (Join-Path $PSScriptRoot '..\juicebar.ico'), [string]$Png = (Join-Path $PSScriptRoot '..\docs\icone.png'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function Novo-Arredondado([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Desenhar([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $k = $s / 32.0                                   # tudo pensado numa grade de 32
    $linha = [Math]::Max(1.0, 2.0 * $k)
    if ($s -le 16) { $linha = 1.25 }

    # corpo da pilha
    $bx = 8 * $k; $by = 6.5 * $k; $bw = 16 * $k; $bh = 23.5 * $k; $br = 3.5 * $k
    if ($s -le 16) { $bx = 3.5; $by = 3.5; $bw = 9; $bh = 12; $br = 2 }
    $corpo = Novo-Arredondado $bx $by $bw $bh $br

    # polo de cima
    $pw = $bw * 0.42; $ph = [Math]::Max(1.5, 2.6 * $k)
    $polo = Novo-Arredondado ($bx + ($bw - $pw) / 2) ($by - $ph + 0.5) $pw $ph ([Math]::Max(0.5, 1 * $k))

    $vidro = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 246, 246, 246))
    $g.FillPath($vidro, $corpo)

    # suco: degrade laranja -> amarelo com superficie em onda
    $nivel = $by + $bh * 0.36
    $suco = New-Object System.Drawing.Drawing2D.GraphicsPath
    $suco.StartFigure()
    $suco.AddLine($bx, $nivel, $bx, $by + $bh)
    $suco.AddLine($bx, $by + $bh, $bx + $bw, $by + $bh)
    $suco.AddLine($bx + $bw, $by + $bh, $bx + $bw, $nivel)
    $amp = [Math]::Max(0.6, 1.4 * $k)
    $suco.AddBezier($bx + $bw, $nivel, $bx + $bw * 0.72, $nivel - 2 * $amp, $bx + $bw * 0.5, $nivel + 2 * $amp, $bx + $bw * 0.25, $nivel)
    $suco.AddBezier($bx + $bw * 0.25, $nivel, $bx + $bw * 0.15, $nivel - $amp, $bx + $bw * 0.05, $nivel - $amp, $bx, $nivel)
    $suco.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $bx, $nivel, $bw, ($by + $bh - $nivel)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 255, 196, 0)), ([System.Drawing.Color]::FromArgb(255, 255, 112, 0)), 90
    $g.SetClip($corpo)
    $g.FillPath($grad, $suco)

    # bolhas e brilho (so nos tamanhos grandes)
    if ($s -ge 32) {
        $bolha = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
        $g.FillEllipse($bolha, $bx + $bw * 0.62, $by + $bh * 0.62, 2.2 * $k, 2.2 * $k)
        $g.FillEllipse($bolha, $bx + $bw * 0.34, $by + $bh * 0.80, 1.5 * $k, 1.5 * $k)
        $g.FillEllipse($bolha, $bx + $bw * 0.70, $by + $bh * 0.84, 1.2 * $k, 1.2 * $k)
        $brilho = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 255, 255, 255))
        $g.FillRectangle($brilho, $bx + 2.6 * $k, $by + 3 * $k, 1.6 * $k, $bh - 6 * $k)
    }
    $g.ResetClip()

    # contorno escuro: aparece bem no tema claro e no escuro (o vidro claro destaca no escuro)
    $tinta = [System.Drawing.Color]::FromArgb(255, 38, 38, 38)
    $caneta = New-Object System.Drawing.Pen $tinta, $linha
    $caneta.LineJoin = 'Round'
    $g.DrawPath($caneta, $corpo)
    $g.FillPath((New-Object System.Drawing.SolidBrush $tinta), $polo)

    # raio (carga) no meio, nos tamanhos grandes
    if ($s -ge 48) {
        $cx = $bx + $bw / 2; $cy = $by + $bh * 0.66; $u = 1.0 * $k
        $raio = New-Object System.Drawing.Drawing2D.GraphicsPath
        $raio.AddPolygon(@(
            (New-Object System.Drawing.PointF ($cx + 1.2 * $u), ($cy - 5.5 * $u)),
            (New-Object System.Drawing.PointF ($cx - 3.2 * $u), ($cy + 0.8 * $u)),
            (New-Object System.Drawing.PointF ($cx - 0.2 * $u), ($cy + 0.8 * $u)),
            (New-Object System.Drawing.PointF ($cx - 1.2 * $u), ($cy + 5.5 * $u)),
            (New-Object System.Drawing.PointF ($cx + 3.2 * $u), ($cy - 0.8 * $u)),
            (New-Object System.Drawing.PointF ($cx + 0.2 * $u), ($cy - 0.8 * $u))
        ))
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))), $raio)
    }

    $g.Dispose()
    return $bmp
}

# Tamanhos ate 64 vao como bitmap classico (32 bits + mascara), que todo leitor entende;
# o de 256 vai em PNG.
function Para-Dib([System.Drawing.Bitmap]$b) {
    $t = $b.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$t); $w.Write([Int32]($t * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $mascara = [int]([Math]::Ceiling($t / 32.0) * 4) * $t
    $w.Write([UInt32]($t * $t * 4 + $mascara))
    $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $t - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $t; $x++) {
            $c = $b.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] $mascara))
    $w.Flush()
    return , $ms.ToArray()
}

$tamanhos = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = foreach ($t in $tamanhos) {
    $b = Desenhar $t
    if ($t -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($Png) { $b.Save($Png, [System.Drawing.Imaging.ImageFormat]::Png) }
        $dados = $ms.ToArray()
    } else { $dados = Para-Dib $b }
    $b.Dispose()
    , $dados
}

# ICO: cabecalho, diretorio e as imagens
$fs = [System.IO.File]::Create($Saida)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]
    $w.Write([byte]($(if ($t -ge 256) { 0 } else { $t })))
    $w.Write([byte]($(if ($t -ge 256) { 0 } else { $t })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Output "Gerado: $Saida"
