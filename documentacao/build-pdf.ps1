<#
.SYNOPSIS
  Gera o PDF tecnico a partir do HTML, usando o Chrome ja instalado.

.DESCRIPTION
  Nao instala nada. Usa `--print-to-pdf` do Chrome headless.
  PRECISA de internet na hora da geracao: os diagramas Mermaid vem de CDN.
  O `--virtual-time-budget` da tempo para o Mermaid desenhar antes da impressao;
  sem ele o PDF sai com os blocos de diagrama vazios.
#>
param(
  [string]$Fonte = "$PSScriptRoot\arquitetura-tecnica.html",
  [string]$Saida = "$PSScriptRoot\arquitetura-tecnica.pdf",
  [int]$OrcamentoMs = 25000
)

$ErrorActionPreference = 'Stop'

$candidatos = @(
  $env:CHROME_PATH,
  'C:\Program Files\Google\Chrome\Application\chrome.exe',
  'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe',
  'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $candidatos) { throw 'Nenhum Chrome ou Edge encontrado. Defina CHROME_PATH.' }
$chrome = $candidatos[0]

if (-not (Test-Path $Fonte)) { throw "HTML de origem nao encontrado: $Fonte" }

# Sem internet o Mermaid nao carrega e os diagramas saem em branco. Avisa cedo.
try {
  Invoke-WebRequest 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs' `
    -Method Head -TimeoutSec 10 -UseBasicParsing | Out-Null
} catch {
  Write-Warning 'CDN do Mermaid inacessivel: os diagramas vao sair VAZIOS no PDF.'
}

if (Test-Path $Saida) { Remove-Item $Saida -Force }

$perfil = Join-Path $env:TEMP ("pdf-build-" + [guid]::NewGuid().ToString('N'))
$uri = ([uri]"file:///$($Fonte -replace '\\','/')").AbsoluteUri

Write-Host "Chrome : $chrome"
Write-Host "Fonte  : $Fonte"
Write-Host "Saida  : $Saida"

# O Chrome escreve avisos benignos no stderr (device_event_log, display_layout).
# No PowerShell 5.1, com ErrorActionPreference='Stop', isso vira NativeCommandError
# e aborta o script. Baixa a guarda so nesta chamada.
$anterior = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
  & $chrome --headless=new --disable-gpu --no-sandbox `
    --user-data-dir="$perfil" `
    --virtual-time-budget=$OrcamentoMs `
    --no-pdf-header-footer `
    --print-to-pdf="$Saida" `
    $uri | Out-Null
} finally {
  $ErrorActionPreference = $anterior
}

Start-Sleep -Milliseconds 500
try { Remove-Item $perfil -Recurse -Force -ErrorAction SilentlyContinue } catch { }

if (-not (Test-Path $Saida)) { throw 'O Chrome nao produziu o PDF.' }

$kb = [math]::Round((Get-Item $Saida).Length / 1KB, 1)
Write-Host "PDF gerado: $Saida ($kb KB)"

# Um PDF com diagramas renderizados passa bem dos 100 KB. Bem menos que isso
# quase sempre significa Mermaid que nao desenhou.
if ($kb -lt 80) { Write-Warning "PDF suspeito de estar sem diagramas ($kb KB)." }
