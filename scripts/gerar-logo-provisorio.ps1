<#
.SYNOPSIS
    Gera o logo e o icone PROVISORIOS do InfoPC (D-008) em Assets\.

.DESCRIPTION
    Desenho: um "i" (de Info) num quadrado arredondado. Texto "InfoPC" nao
    cabe legivel num icone de 16 px. Quando o logo definitivo chegar, basta
    trocar os arquivos de Assets\ (mesmos nomes e tamanhos) e apagar este script.

    Arquivos gerados (mesmos nomes e tamanhos do projeto original):
      logo.png        256 px  claro, "i" vazado (menu no tema escuro)
      logo-dark.png   256 px  escuro, "i" vazado (menu no tema claro)
      logo-badge.png  128 px  quadrado escuro com "i" claro (relatorio HTML)
      app.ico         16 a 256 px, mesmo desenho do badge (janela e .exe)

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gerar-logo-provisorio.ps1
#>
param(
    # Pasta de saida (padrao: Assets\ na raiz do repositorio)
    [string]$Saida = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# $PSScriptRoot fica vazio no valor padrao do param() no PowerShell 5.1
if (-not $Saida) { $Saida = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets' }

$escuro = '#141414'
$claro = '#F2F2F2'

function New-QuadradoArredondado([float]$x, [float]$y, [float]$lado) {
    $d = 2 * 0.22 * $lado
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $lado - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $lado - $d, $y + $lado - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $lado - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# Ponto e haste do "i", alinhados ao pixel e centralizados em uma imagem de $s px
function Get-LetraI([int]$s) {
    $w = [int][Math]::Floor(0.16 * $s)
    if (($s - $w) % 2 -ne 0) { $w++ }
    $w = [Math]::Max(2, $w)
    $x = [int](($s - $w) / 2)
    $topo = [int][Math]::Round(0.20 * $s)
    $vao = [Math]::Max(1, [int][Math]::Round(0.07 * $s))
    $haste = $topo + $w + $vao
    @(
        (New-Object System.Drawing.Rectangle($x, $topo, $w, $w)),
        (New-Object System.Drawing.Rectangle($x, $haste, $w, ($s - $topo - $haste)))
    )
}

# $letra: cor do "i", ou '' para vazar (transparente)
function New-Imagem([int]$s, [string]$fundo, [string]$letra, [int]$margem) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $pincel = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($fundo))
        $g.FillPath($pincel, (New-QuadradoArredondado $margem $margem ($s - 2 * $margem)))

        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        if ($letra) {
            $corLetra = [System.Drawing.ColorTranslator]::FromHtml($letra)
        } else {
            $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $corLetra = [System.Drawing.Color]::Transparent
        }
        $pincelLetra = New-Object System.Drawing.SolidBrush($corLetra)
        foreach ($r in (Get-LetraI $s)) { $g.FillRectangle($pincelLetra, $r) }
    } finally {
        $g.Dispose()
    }
    $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$nome) {
    $bmp.Save((Join-Path $Saida $nome), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $nome"
}

# ICO com uma imagem PNG por tamanho (formato aceito pelo Windows Vista em diante)
function Save-Ico([int[]]$tamanhos, [string]$nome) {
    $pngs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($s in $tamanhos) {
        $bmp = New-Imagem $s $escuro $claro 0
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs.Add($ms.ToArray())
    }
    $fs = [System.IO.File]::Create((Join-Path $Saida $nome))
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$tamanhos.Length)
        $offset = 6 + 16 * $tamanhos.Length
        for ($i = 0; $i -lt $tamanhos.Length; $i++) {
            $lado = [byte]($tamanhos[$i] -band 0xFF)   # 256 vira 0, como manda o formato
            $bw.Write($lado); $bw.Write($lado); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
            $offset += $pngs[$i].Length
        }
        foreach ($p in $pngs) { $bw.Write($p) }
    } finally {
        $bw.Dispose()
    }
    Write-Host "  $nome ($($tamanhos -join ', ') px)"
}

Write-Host "Gerando logo provisorio em $Saida"
Save-Png (New-Imagem 256 $claro '' 8) 'logo.png'
Save-Png (New-Imagem 256 $escuro '' 8) 'logo-dark.png'
Save-Png (New-Imagem 128 $escuro $claro 0) 'logo-badge.png'
Save-Ico @(16, 24, 32, 48, 64, 128, 256) 'app.ico'
