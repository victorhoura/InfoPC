<#
.SYNOPSIS
    Procura um nome (ex.: o nome do projeto original) no conteudo e nos nomes
    de arquivos/pastas do repositorio. Usado na renomeacao para InfoPC.

.DESCRIPTION
    Ocorrencias em arquivos de licenca, creditos e coordenacao (colab/) sao
    "esperadas": devem ser mantidas e aparecem so no resumo. As demais sao
    "inesperadas" e sao listadas uma a uma.

    Linhas com um texto de -LinhasEsperadas (padrao: as formas usadas nos
    creditos ao original, como "baseado no RESET EXPLODE" e "RESET EXPLODE v0.16")
    tambem contam como esperadas, em qualquer arquivo. Uma marca esquecida
    ("abra o RESET EXPLODE") continua aparecendo.

    A busca nao diferencia maiusculas de minusculas, a menos que se use
    -DiferenciarMaiusculas. Rode tambem as variacoes do nome (com espaco,
    hifen, sublinhado, abreviacoes).

    Codigo de saida: 0 = nenhuma ocorrencia inesperada; 1 = ha ocorrencias.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verificar-nome-antigo.ps1 -Nome "ProjetoOriginal"

.EXAMPLE
    # Marca tambem a tela "Sobre" como esperada (credito ao projeto original)
    ... -Nome "ProjetoOriginal" -TambemEsperados "src/AboutForm.cs"

.EXAMPLE
    # So em maiusculas: acha "RESET BENCH" sem listar "reset do driver"
    ... -Nome "RESET " -DiferenciarMaiusculas
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Nome,

    # Raiz do repositorio (padrao: a pasta acima de scripts\)
    [string]$Raiz = '',

    # Pastas ignoradas em qualquer nivel
    [string[]]$IgnorarPastas = @('.git', '.vs', '.claude', '.codex', 'bin', 'obj', 'node_modules', 'dist'),

    # Onde o nome antigo deve permanecer. Padrao sem "/" compara com o nome do
    # arquivo; com "/" compara com o caminho relativo a raiz.
    [string[]]$Esperados = @(
        'LICENSE', 'LICENSE.*', 'COPYING', 'COPYING.*', 'NOTICE', 'NOTICE.*',
        'AUTHORS', 'AUTHORS.*', 'CREDITS', 'CREDITS.*',
        'docs/UPSTREAM.md', 'docs/DECISOES.md', 'colab/*',
        'scripts/verificar-nome-antigo.ps1'
    ),

    # Padroes extras somados a -Esperados
    [string[]]$TambemEsperados = @(),

    # Linhas que contem um destes textos contam como esperadas (sem virgula no texto)
    [string[]]$LinhasEsperadas = @(
        'baseado no RESET EXPLODE', 'RESET EXPLODE v0.16', '[RESET EXPLODE](',
        'Gustavo (GustavoReset)', 'Gustavo/GustavoReset'
    ),

    # Diferencia maiusculas de minusculas no conteudo e nos nomes de arquivos
    [switch]$DiferenciarMaiusculas
)

$ErrorActionPreference = 'Stop'

# Com "powershell -File", uma lista "a,b" chega como um unico texto: separa aqui
function Split-Lista([string[]]$valores) {
    @($valores | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# $PSScriptRoot fica vazio no valor padrao do param() no PowerShell 5.1
if (-not $Raiz) { $Raiz = Split-Path -Parent $PSScriptRoot }

$raizCompleta = (Resolve-Path -LiteralPath $Raiz).Path.TrimEnd('\')
$padrao = [regex]::Escape($Nome)
$opcoes = if ($DiferenciarMaiusculas) {
    [System.Text.RegularExpressions.RegexOptions]::None
} else {
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
}
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$limiteBytes = 20MB
$IgnorarPastas = Split-Lista $IgnorarPastas
$todosEsperados = Split-Lista ($Esperados + $TambemEsperados)
$LinhasEsperadas = Split-Lista $LinhasEsperadas

$inesperadas = New-Object System.Collections.Generic.List[object]
$esperadasPorArquivo = @{}

function Get-CaminhoRelativo([string]$caminho) {
    $caminho.Substring($raizCompleta.Length).TrimStart('\').Replace('\', '/')
}

function Test-Esperado([string]$relativo) {
    $folha = $relativo.Split('/')[-1]
    foreach ($p in $todosEsperados) {
        $alvo = if ($p.Contains('/')) { $relativo } else { $folha }
        if ($alvo -like $p) { return $true }
    }
    return $false
}

function Test-LinhaEsperada([string]$linha) {
    foreach ($l in $LinhasEsperadas) {
        if ($linha.IndexOf($l, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Add-Ocorrencia([string]$relativo, [int]$linha, [string]$texto, [bool]$linhaEsperada = $false) {
    if ($linhaEsperada -or (Test-Esperado $relativo)) {
        $script:esperadasPorArquivo[$relativo] = 1 + [int]$script:esperadasPorArquivo[$relativo]
    } else {
        $script:inesperadas.Add([pscustomobject]@{ Caminho = $relativo; Linha = $linha; Texto = $texto })
    }
}

function Search-Arquivo([System.IO.FileInfo]$arquivo, [string]$relativo) {
    if ($arquivo.Length -eq 0) { return }
    if ($arquivo.Length -gt $limiteBytes) {
        Write-Warning "Ignorado (maior que 20 MB): $relativo"
        return
    }

    $bytes = [System.IO.File]::ReadAllBytes($arquivo.FullName)
    $utf16ComBom = $bytes.Length -ge 2 -and (
        ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))
    $amostra = [Math]::Min($bytes.Length, 8000)
    $binario = (-not $utf16ComBom) -and ([Array]::IndexOf($bytes, [byte]0, 0, $amostra) -ge 0)

    if ($binario) {
        # Procura como texto ANSI e como UTF-16 (ex.: recursos compilados)
        if ([regex]::IsMatch($latin1.GetString($bytes), $padrao, $opcoes) -or
            [regex]::IsMatch([System.Text.Encoding]::Unicode.GetString($bytes), $padrao, $opcoes)) {
            Add-Ocorrencia $relativo 0 "[binario] $relativo"
        }
        return
    }

    # Detecta BOM (UTF-8/UTF-16); sem BOM assume UTF-8
    $texto = [System.IO.File]::ReadAllText($arquivo.FullName)
    if (-not [regex]::IsMatch($texto, $padrao, $opcoes)) { return }

    $linhas = $texto -split "`r?`n"
    for ($n = 0; $n -lt $linhas.Length; $n++) {
        if ([regex]::IsMatch($linhas[$n], $padrao, $opcoes)) {
            $trecho = $linhas[$n].Trim()
            if ($trecho.Length -gt 160) { $trecho = $trecho.Substring(0, 157) + '...' }
            Add-Ocorrencia $relativo ($n + 1) ("[texto]   {0}:{1}: {2}" -f $relativo, ($n + 1), $trecho) (Test-LinhaEsperada $linhas[$n])
        }
    }
}

Write-Host ("Procurando '{0}' em {1}" -f $Nome, $raizCompleta)
Write-Host ''

# Percurso manual para nao entrar em pastas ignoradas nem em links/junctions
$pendentes = New-Object System.Collections.Generic.Stack[string]
$pendentes.Push($raizCompleta)
while ($pendentes.Count -gt 0) {
    $pasta = $pendentes.Pop()
    try {
        $itens = Get-ChildItem -LiteralPath $pasta -Force
    } catch {
        Write-Warning "Sem acesso: $pasta"
        continue
    }

    foreach ($item in $itens) {
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
        $relativo = Get-CaminhoRelativo $item.FullName

        if ($item.PSIsContainer) {
            if ($IgnorarPastas -contains $item.Name) { continue }
            if ([regex]::IsMatch($item.Name, $padrao, $opcoes)) { Add-Ocorrencia $relativo 0 "[nome]    $relativo/" }
            $pendentes.Push($item.FullName)
            continue
        }

        if ([regex]::IsMatch($item.Name, $padrao, $opcoes)) { Add-Ocorrencia $relativo 0 "[nome]    $relativo" }
        try {
            Search-Arquivo $item $relativo
        } catch {
            Write-Warning "Nao foi possivel ler: $relativo ($($_.Exception.Message))"
        }
    }
}

foreach ($o in ($inesperadas | Sort-Object Caminho, Linha)) { Write-Host $o.Texto }

if ($esperadasPorArquivo.Count -gt 0) {
    Write-Host ''
    Write-Host 'Esperadas (manter):'
    foreach ($c in ($esperadasPorArquivo.Keys | Sort-Object)) {
        Write-Host ("  {0} ({1})" -f $c, $esperadasPorArquivo[$c])
    }
}

Write-Host ''
$arquivos = @($inesperadas | Select-Object -ExpandProperty Caminho -Unique).Count
Write-Host ("Resumo: {0} ocorrencia(s) inesperada(s) em {1} arquivo(s)/pasta(s)." -f $inesperadas.Count, $arquivos)

if ($inesperadas.Count -gt 0) { exit 1 }
exit 0
