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

# Duas dependencias de rede, e falhar em qualquer uma degrada o PDF em silencio:
# sem Mermaid os diagramas saem vazios, sem Google Fonts a tipografia cai para
# serif generica e o documento perde a identidade inteira.
$recursos = @{
  'Mermaid'      = 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs'
  'Google Fonts' = 'https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,400&display=swap'
}
foreach ($nome in $recursos.Keys) {
  try {
    Invoke-WebRequest $recursos[$nome] -Method Head -TimeoutSec 10 -UseBasicParsing | Out-Null
  } catch {
    Write-Warning "$nome inacessivel: o PDF vai sair degradado."
  }
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

# Com diagramas em SVG e tres familias de fonte embutidas, o PDF passa de 800 KB.
# Bem menos que isso quase sempre significa Mermaid que nao desenhou ou fonte que
# nao carregou, e os dois casos saem sem erro nenhum.
if ($kb -lt 400) { Write-Warning "PDF suspeito: $kb KB. Confira diagramas e fontes." }

# Prova de que as fontes customizadas entraram: um PDF com fallback generico nao
# embute FontFile nenhum.
$bytes = [IO.File]::ReadAllBytes($Saida)
$texto = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
$embutidas = ([regex]::Matches($texto, '/FontFile')).Count
$paginas = ([regex]::Matches($texto, '/Type\s*/Page[^s]')).Count
Write-Host "paginas: $paginas | fontes embutidas: $embutidas"
if ($embutidas -lt 5) { Write-Warning 'Poucas fontes embutidas: a tipografia provavelmente caiu para fallback.' }
