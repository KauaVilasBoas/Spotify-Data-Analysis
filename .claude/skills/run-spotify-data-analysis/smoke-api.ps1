<#
.SYNOPSIS
  Smoke da API: espera o warm-up do indice e exercita os endpoints que importam.

.DESCRIPTION
  Sai com codigo 1 na primeira asserçao que falhar. Nao e teste de unidade:
  bate no processo real, contra o Postgres real.

.PARAMETER BaseUrl
  Origem da API. Padrao http://localhost:5140

.PARAMETER Seed
  Faixa-semente. O padrao ("Creep", Radiohead) foi escolhido porque tem 3.360
  vizinhos na matriz de co-ocorrencia. Semente aleatoria NAO serve: a matriz
  cobre so 10,8% do catalogo, e o blend cai em content com aviso.
#>
param(
  [string]$BaseUrl = 'http://localhost:5140',
  [string]$Seed = '70LcF31zb1H0PyJoS1Sx1r',
  [int]$Limit = 10
)

$ErrorActionPreference = 'Stop'
$falhas = 0

function Ok($msg)    { Write-Host "  OK   $msg" }
function Falha($msg) { Write-Host "  FALHA $msg"; $script:falhas++ }

# --- 1. health -------------------------------------------------------------
Write-Host "`n== health =="
try {
  $h = Invoke-WebRequest "$BaseUrl/health" -TimeoutSec 10 -UseBasicParsing
  if ($h.StatusCode -eq 200) { Ok "/health -> 200 $($h.Content)" } else { Falha "/health -> $($h.StatusCode)" }
} catch { Falha "/health inacessivel: $($_.Exception.Message)"; exit 1 }

# --- 2. warm-up do indice --------------------------------------------------
# O indice de similaridade e montado em background no arranque. Ate ficar
# pronto o endpoint devolve 503 com Retry-After. /health 200 NAO significa
# indice pronto.
Write-Host "`n== warm-up do indice de similaridade =="
$sw = [Diagnostics.Stopwatch]::StartNew()
$pronto = $false
while ($sw.Elapsed.TotalSeconds -lt 180 -and -not $pronto) {
  try {
    $r = Invoke-WebRequest "$BaseUrl/api/recommendations/track/$Seed`?limit=1" -TimeoutSec 30 -UseBasicParsing
    if ($r.StatusCode -eq 200) { $pronto = $true }
  } catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -eq 503) { Start-Sleep -Seconds 2 } else { Falha "status inesperado $code durante o warm-up"; break }
  }
}
if ($pronto) { Ok "indice pronto em $([int]$sw.Elapsed.TotalSeconds) s" } else { Falha 'indice nao ficou pronto em 180 s'; exit 1 }

# --- 3. catalogo -----------------------------------------------------------
Write-Host "`n== catalogo =="
$s = (Invoke-RestMethod "$BaseUrl/api/insights/summary" -TimeoutSec 30).data
if ($s.totalTracks -gt 0) { Ok "faixas: $($s.totalTracks) | imputadas: $($s.tracksWithImputedFeatures) | generos: $($s.distinctGenres)" }
else { Falha 'summary sem faixas: o banco esta vazio ou a migration nao rodou' }

$t = (Invoke-RestMethod "$BaseUrl/api/tracks?page=1&pageSize=3" -TimeoutSec 30).data
# Envelope: { success, message, data }. Aqui data e paginado: items/totalCount.
# Campos do item sao trackId/name/primaryArtist, NAO id/artist.
if ($t.items[0].trackId) { Ok "tracks -> $($t.totalCount) no total, primeiro: $($t.items[0].name) / $($t.items[0].primaryArtist)" }
else { Falha 'forma inesperada em /api/tracks (esperado data.items[].trackId)' }

# --- 4. recomendacoes: content vs blend ------------------------------------
# Aqui mora a diferenca que o rotulo do frontend precisa respeitar:
#   content -> score = cosineScore + genreBoost  (a soma fecha)
#   blend   -> score = score final do blender    (a soma NAO fecha)
Write-Host "`n== recomendacoes =="
foreach ($estrategia in 'content', 'blend') {
  $sw2 = [Diagnostics.Stopwatch]::StartNew()
  $d = (Invoke-RestMethod "$BaseUrl/api/recommendations/track/$Seed`?limit=$Limit&strategy=$estrategia&genreMode=boost&dedupe=true" -TimeoutSec 60).data
  $ms = [int]$sw2.Elapsed.TotalMilliseconds
  # Recomendacoes vem em data.recommendations, NAO em data.items.
  $n = $d.recommendations.Count
  if ($n -eq $Limit) { Ok "$estrategia -> $n de $Limit itens em $ms ms (colapsadas: $($d.totalDuplicatesCollapsed))" }
  else { Falha "$estrategia -> $n de $Limit itens (o over-fetch adaptativo do E4.9 garante o limite cheio)" }

  $primeiro = $d.recommendations[0]
  $soma = $primeiro.cosineScore + $primeiro.genreBoost
  $fecha = [math]::Abs($soma - $primeiro.score) -lt 1e-9
  if ($estrategia -eq 'content') {
    if ($fecha) { Ok "content: score = cosine + genre ($([math]::Round($primeiro.score,4)))" }
    else { Falha "content deveria fechar a soma, mas score=$($primeiro.score) e soma=$soma" }
  } else {
    if (-not $fecha) { Ok "blend: score ($([math]::Round($primeiro.score,4))) NAO e cosine+genre ($([math]::Round($soma,4))), como esperado" }
    else { Falha 'blend fechou a soma: o blender voltou a ordenar pelo score de content?' }
  }
}

Write-Host ""
if ($falhas -eq 0) { Write-Host 'SMOKE OK'; exit 0 } else { Write-Host "SMOKE FALHOU: $falhas asserçao(oes)"; exit 1 }
