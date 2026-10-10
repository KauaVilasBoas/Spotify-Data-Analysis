<#
.SYNOPSIS
    Mede latência (p50/p95/p99) e cold start do endpoint de recomendação em container, nas quatro
    combinações {512 MB, 256 MB} x {--cpus 0.1, sem throttle}, e valida a própria medição.

.DESCRIPTION
    Metodologia reprodutível para a seção de free-tier do README/DEPLOY.md (E6.9, refeita no E6.12).
    UM comando, sem passo manual: ver .EXAMPLE.

    FERRAMENTA DE CARGA
    -------------------
    bombardier 1.2.6, imagem pública alpine/bombardier:1.2.6 (digest sha256:7be639b2...55ab), executada
    em um container descartável (docker run --rm) NA MESMA REDE DOCKER da API, falando com a API pelo
    nome do container. Os percentis (p50/p95/p99) vêm da ferramenta (-l -o json), nenhum é calculado em
    PowerShell. Nada é instalado nem versionado: só Docker é necessário. O cliente deixou de ser o
    Invoke-WebRequest (confundidor do E6.9: PowerShell + rede somados ao número da aplicação).

    CARGA
    -----
    1 conexão (-c 1), requisições sequenciais, o cenário honesto de uma demo em free tier. Uma única
    semente (a primeira da seleção determinística): o custo do endpoint é uma varredura completa do
    índice, e o código da API não tem cache de resposta, então a semente não muda o trabalho feito.
    Só a estratégia "content" é medida (a de menor custo; "blend" continua fora desta medição).

    AQUECIMENTO (DP-1)
    ------------------
    Antes de cada combinação, janelas de 20 requisições são descartadas (fora da amostra) até que o
    desvio-padrão da janela caia abaixo de 10% da mediana (p50) da janela. PISO de 50 e TETO de 200
    descartes: como a janela é de 20, o mínimo efetivo é 60 e o máximo 200. Se o teto for atingido sem
    estabilizar, a combinação segue medida mas é marcada WarmupCapped=true na saída. O número de
    descartes de cada combinação sai na saída estruturada. Média/desvio/mediana da janela vêm do bombardier.

    COLD START
    ----------
    O cronômetro (Stopwatch, ms, sem truncar) inicia imediatamente ANTES do "docker run". Dois números:
      (a) ColdStartHealthMs   : docker run até /health responder 200;
      (b) ColdStartFirstRecMs : docker run até a primeira recomendação 200.
    O índice é montado em background no arranque (commits 32262b6/3651ba5), então (b) inclui a
    espera dos 503. O polling roda no host (HttpClient, 25 ms), pela porta publicada.

    CPU
    ---
    --cpus 0.1 é aplicado de verdade e provado: o script grava HostConfig.NanoCpus e o delta de
    nr_throttled / throttled_usec do cpu.stat do cgroup do container durante as rodadas medidas.
    A combinação "sem throttle" roda sem --cpus (todas as CPUs da VM do Docker).

    RODADAS (DP-2)
    --------------
    5 rodadas por combinação, N requisições por rodada (-RequestsPerRound), sempre no mesmo container
    (o aquecimento vale para todas). Reporta p50/p95/p99 por rodada, mediana e amplitude (max-min).

    SANIDADE BLOQUEANTE
    -------------------
    Três checagens sobre os próprios dados; se QUALQUER uma falhar a medição é INVÁLIDA, o script
    imprime os números crus, sai com código 2 e nada deve ir para o README. Valem para p50, p95 e p99:
      S1  em cada CPU, a mediana de 256 MB não pode ser MENOR que a de 512 MB;
      S2  em cada CPU, a amplitude entre rodadas (a maior dos dois tiers) tem de ser MENOR que a
          diferença entre as medianas dos tiers;
      S3  a rodada 1 não pode ser sistematicamente a mais lenta. Falha se, em qualquer combinação,
          o p50 cai estritamente a cada rodada, OU o p50 da rodada 1 supera o maior das rodadas 2..5
          por mais que a amplitude das rodadas 2..5, OU a rodada 1 é a mais lenta nas quatro combinações.
    Uma diferença real entre 512 e 256 MB menor que o ruído torna a S2 insatisfazível por natureza;
    se for o caso, o resultado é reportado como tal, não forçado.

    PRÉ-REQUISITOS
    --------------
    - Docker Desktop em execução.
    - Variável de ambiente ConnectionStrings__SpotifyDb com a connection string do banco.
      O script FALHA com mensagem clara se a variável estiver ausente.
    - psql.exe acessível em $env:PATH ou no caminho padrão do PostgreSQL (o banco só é LIDO).
    - A imagem Docker do repositório: por padrão spotifydataanalysis-api:<commit curto do HEAD>.
      Se não existir, o script constrói a partir do Dockerfile na raiz do repositório.

    LIMITAÇÕES CONHECIDAS (após o E6.12)
    ------------------------------------
    Deixaram de ser limitação: aquecimento de uma requisição só; cronômetro do cold start iniciado
    depois do docker run e truncado; ausência de limite de CPU; cliente Invoke-WebRequest.
    Continuam sendo:
    1. Uma semente só, estratégia "content" só; "blend" (que consulta a co-ocorrência no Postgres) não é medida.
    2. Cliente e API dividem a mesma máquina; o Postgres também é o da máquina, fora de container e de
       qualquer limite de CPU/RAM. Latência de banco de um Postgres gerenciado remoto não está incluída.
    3. --cpus 0.1 é cota CFS (throttling em fatias de 100 ms), não uma vCPU lenta: o comportamento sob
       um provedor de free tier pode diferir do de um host local.
    4. O aquecimento mede estabilidade da latência por janelas de 20; não prova que o JIT chegou ao
       tier final, só que a latência parou de se mover o suficiente para o critério da DP-1.
    5. A RAM lida é o uso corrente no fim da corrida (docker stats --no-stream), não o pico histórico.
    6. O cold start depende do cache de páginas do Postgres e do disco do momento.

.PARAMETER ImageTag
    Tag da imagem Docker a usar. Padrão: "spotifydataanalysis-api:<commit curto do HEAD>".
    Se não existir, a imagem será construída via "docker build".

.PARAMETER Tiers
    Limites de memória a testar, em MiB. Padrão: @(512, 256).

.PARAMETER CpuLimits
    Limites de CPU (valor de --cpus). 0 significa sem throttle. Padrão: @(0.1, 0).

.PARAMETER Rounds
    Rodadas de medição por combinação. Padrão: 5 (DP-2).

.PARAMETER RequestsPerRound
    Requisições medidas por rodada. Padrão: 200.

.PARAMETER Port
    Porta local publicada pelo container (usada só no polling do cold start). Padrão: 18080.

.PARAMETER LoadImage
    Imagem do gerador de carga. Padrão: alpine/bombardier:1.2.6.

.PARAMETER OutFile
    Arquivo JSON com a saída estruturada. Padrão: measure-latency-<commit>.json em $env:TEMP
    (fora do repositório). Não contém connection string.

.PARAMETER RepoRoot
    Raiz do repositório. Padrão: diretório pai de deploy/.

.EXAMPLE
    # Medição completa (as 4 combinações, 5 rodadas), um comando:
    cd C:\Projetos\SpotifyDataAnalysis
    $line = dotnet user-secrets list --project src/Host/SpotifyDataAnalysis.Api | Where-Object { $_ -like 'ConnectionStrings:SpotifyDb*' }
    $env:ConnectionStrings__SpotifyDb = ($line -split ' = ',2)[1]
    .\deploy\measure-latency.ps1

.EXAMPLE
    # Só uma combinação, para depurar o script:
    .\deploy\measure-latency.ps1 -Tiers @(512) -CpuLimits @(0) -Rounds 2

.NOTES
    O script NÃO toca em containers de outros projetos: só cria e remove, pelo nome exato, o container
    "spotifyda-latency-measure" e a rede "spotifyda-latency-net". Ambos são derrubados ao final,
    mesmo em caso de erro. Nenhuma connection string ou senha é gravada em arquivo ou exibida em log.
#>
[CmdletBinding()]
param(
    [string]   $ImageTag  = "",
    [int[]]    $Tiers     = @(512, 256),
    [double[]] $CpuLimits = @(0.1, 0),
    [int]      $Rounds    = 5,
    [int]      $RequestsPerRound = 200,
    [int]      $Port      = 18080,
    [string]   $LoadImage = "alpine/bombardier:1.2.6",
    [string]   $OutFile   = "",
    [string]   $RepoRoot  = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# 1. Pré-condições
# ---------------------------------------------------------------------------

$ConnStr = $env:ConnectionStrings__SpotifyDb
if (-not $ConnStr) {
    Write-Error @"
ERRO: variável de ambiente 'ConnectionStrings__SpotifyDb' não definida.
Defina-a antes de executar:
  `$env:ConnectionStrings__SpotifyDb = 'Host=...;Port=5432;Database=...;Username=...;Password=...'
"@
    exit 1
}

# Localizar psql
$PsqlExe = $null
$candidates = @(
    "psql",
    "C:\Program Files\PostgreSQL\18\bin\psql.exe",
    "C:\Program Files\PostgreSQL\17\bin\psql.exe",
    "C:\Program Files\PostgreSQL\16\bin\psql.exe",
    "C:\Program Files\PostgreSQL\15\bin\psql.exe"
)
foreach ($c in $candidates) {
    if (Get-Command $c -ErrorAction SilentlyContinue) {
        $PsqlExe = $c
        break
    }
}
if (-not $PsqlExe) {
    Write-Error "ERRO: psql.exe não encontrado. Instale o PostgreSQL ou adicione o bin/ ao PATH."
    exit 1
}

# Verificar Docker
docker version | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERRO: Docker não está acessível. Inicie o Docker Desktop."
    exit 1
}

# ---------------------------------------------------------------------------
# 2. Garantir imagem
# ---------------------------------------------------------------------------

$Commit   = (git -C $RepoRoot rev-parse --short HEAD | Out-String).Trim()
$SrcClean = -not (git -C $RepoRoot status --porcelain -- src Directory.Build.props Dockerfile | Out-String).Trim()
$DockerVm = (docker info --format "NCPU={{.NCPU}} MemTotal={{.MemTotal}} cgroup=v{{.CgroupVersion}}" | Out-String).Trim()
if (-not $ImageTag) { $ImageTag = "spotifydataanalysis-api:$Commit" }
if (-not $SrcClean) { Write-Warning "src/, Directory.Build.props ou Dockerfile têm mudanças não commitadas: o commit medido NÃO descreve a imagem." }

$imageExists = docker images $ImageTag --format "{{.Repository}}:{{.Tag}}" 2>&1
if (-not $imageExists -or $imageExists -notmatch [regex]::Escape($ImageTag)) {
    Write-Host ""
    Write-Host "Imagem '$ImageTag' não encontrada. Construindo a partir de $RepoRoot ..."
    docker build -t $ImageTag $RepoRoot
    if ($LASTEXITCODE -ne 0) { Write-Error "ERRO: docker build falhou."; exit 1 }
    Write-Host "Build concluído."
}
else {
    Write-Host "Usando imagem existente: $ImageTag"
}

# ---------------------------------------------------------------------------
# 3. Buscar sementes determinísticas do banco
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Buscando 300 sementes determinísticas do banco (seed='e4.4-proxies-v1')..."

$SeedSql = @"
SELECT t.id
FROM catalog.tracks AS t
WHERE
      (t.audio_features ->> 'Danceability')     IS NOT NULL
  AND (t.audio_features ->> 'Energy')           IS NOT NULL
  AND (t.audio_features ->> 'Valence')          IS NOT NULL
  AND (t.audio_features ->> 'Tempo')            IS NOT NULL
  AND (t.audio_features ->> 'Acousticness')     IS NOT NULL
  AND (t.audio_features ->> 'Instrumentalness') IS NOT NULL
  AND (t.audio_features ->> 'Liveness')         IS NOT NULL
  AND (t.audio_features ->> 'Speechiness')      IS NOT NULL
  AND (t.audio_features ->> 'Loudness')         IS NOT NULL
ORDER BY md5('e4.4-proxies-v1' || t.id)
LIMIT 300;
"@

# Extrair parâmetros de conexão da connection string (formato Key=Value separado por ;)
function Parse-ConnStr([string]$cs) {
    $map = @{}
    $cs -split ';' | ForEach-Object {
        $kv = $_ -split '=', 2
        if ($kv.Count -eq 2) { $map[$kv[0].Trim()] = $kv[1].Trim() }
    }
    return $map
}

$connMap  = Parse-ConnStr $ConnStr
$dbHost   = $connMap["Host"]
$dbPort   = if ($connMap["Port"]) { $connMap["Port"] } else { "5432" }
$dbName   = $connMap["Database"]
$dbUser   = $connMap["Username"]
$dbPass   = $connMap["Password"]

$env:PGPASSWORD = $dbPass
$rawIds = & $PsqlExe -h $dbHost -p $dbPort -U $dbUser -d $dbName -t -A -c $SeedSql 2>&1
$Seeds  = $rawIds | Where-Object { $_ -match '^[A-Za-z0-9]{15,30}$' }

if ($Seeds.Count -lt 10) {
    Write-Error "ERRO: menos de 10 sementes retornadas do banco ($($Seeds.Count)). Verifique a connection string e o banco."
    exit 1
}
Write-Host "  $($Seeds.Count) sementes carregadas."

# ---------------------------------------------------------------------------
# 4. Banco (somente leitura) e funções auxiliares
# ---------------------------------------------------------------------------

$TrackCount = (& $PsqlExe -h $dbHost -p $dbPort -U $dbUser -d $dbName -t -A -c "SELECT count(*) FROM catalog.tracks;" | Out-String).Trim()
Write-Host "  catalog.tracks no banco: $TrackCount faixas"

$SeedId  = $Seeds[0]
$RecPath = "/api/recommendations/track/${SeedId}?limit=10&strategy=content"

$ContainerName = "spotifyda-latency-measure"
$NetworkName   = "spotifyda-latency-net"
$InternalUrl   = "http://${ContainerName}:8080"
$HostUrl       = "http://localhost:$Port"
$Inv           = [System.Globalization.CultureInfo]::InvariantCulture

Add-Type -AssemblyName System.Net.Http
$Http = [System.Net.Http.HttpClient]::new()
$Http.Timeout = [TimeSpan]::FromSeconds(5)

function Get-StatusCode([string]$url) {
    try {
        $r = $Http.GetAsync($url).GetAwaiter().GetResult()
        $c = [int]$r.StatusCode
        $r.Dispose()
        return $c
    } catch { return 0 }
}

function Stop-MeasureContainer {
    $exists = docker ps -a --filter "name=^${ContainerName}$" --format "{{.Names}}"
    if ($exists -eq $ContainerName) {
        Write-Host "  Parando container $ContainerName ..."
        docker stop $ContainerName | Out-Null
        docker rm  $ContainerName | Out-Null
    }
}

function Remove-MeasureNetwork {
    $exists = docker network ls --filter "name=^${NetworkName}$" --format "{{.Name}}"
    if ($exists -eq $NetworkName) { docker network rm $NetworkName | Out-Null }
}

function Get-Median([double[]]$v) {
    $s = $v | Sort-Object
    $n = $s.Count
    if ($n % 2 -eq 1) { return [double]$s[($n - 1) / 2] }
    return ([double]$s[$n / 2 - 1] + [double]$s[$n / 2]) / 2.0
}

function Get-Amplitude([double[]]$v) {
    $m = $v | Measure-Object -Minimum -Maximum
    return [double]($m.Maximum - $m.Minimum)
}

# Roda o bombardier (container descartável, mesma rede da API). Os percentis vêm da ferramenta.
# O JSON do bombardier reporta latência em microssegundos.
function Invoke-Load([int]$requests) {
    $out = docker run --rm --network $NetworkName $LoadImage -c 1 -t 60s -l -p r -o json -n $requests "$InternalUrl$RecPath"
    if ($LASTEXITCODE -ne 0) { throw "bombardier falhou (exit $LASTEXITCODE): $out" }
    $res = ($out -join "`n" | ConvertFrom-Json).result
    $p = $res.latency.percentiles
    return [PSCustomObject]@{
        Requests = $requests
        Ok       = [int]$res.req2xx
        Errors   = $requests - [int]$res.req2xx
        P50      = [Math]::Round([double]$p.'50' / 1000.0, 2)
        P95      = [Math]::Round([double]$p.'95' / 1000.0, 2)
        P99      = [Math]::Round([double]$p.'99' / 1000.0, 2)
        MeanMs   = [Math]::Round([double]$res.latency.mean / 1000.0, 2)
        StdDevMs = [Math]::Round([double]$res.latency.stddev / 1000.0, 2)
        MaxMs    = [Math]::Round([double]$res.latency.max / 1000.0, 2)
    }
}

function Get-CpuStat {
    $h = @{}
    $raw = docker exec $ContainerName cat /sys/fs/cgroup/cpu.stat
    foreach ($line in $raw) {
        $kv = "$line" -split '\s+'
        if ($kv.Count -eq 2) { $h[$kv[0]] = [long]$kv[1] }
    }
    return $h
}

# ---------------------------------------------------------------------------
# 5. Uma combinação (RAM x CPU): cold start, aquecimento, rodadas
# ---------------------------------------------------------------------------

$ContainerConnStr = $ConnStr -replace '(?i)Host\s*=\s*localhost', 'Host=host.docker.internal'
$ContainerConnStr = $ContainerConnStr -replace '(?i)Host\s*=\s*127\.0\.0\.1', 'Host=host.docker.internal'

function Measure-Combination([int]$tierMiB, [double]$cpus) {
    $label = if ($cpus -gt 0) { "$tierMiB MB / --cpus $($cpus.ToString($Inv))" } else { "$tierMiB MB / sem throttle" }
    Write-Host ""
    Write-Host "================================================================"
    Write-Host "COMBINACAO: $label"
    Write-Host "================================================================"

    Stop-MeasureContainer

    $runArgs = @('run', '-d', '--name', $ContainerName, '--network', $NetworkName,
        '-m', "${tierMiB}m", '--memory-swap', "${tierMiB}m", '-p', "${Port}:8080",
        '-e', "ConnectionStrings__SpotifyDb=$ContainerConnStr",
        '-e', 'ASPNETCORE_ENVIRONMENT=Production', '-e', 'ASPNETCORE_URLS=http://+:8080')
    if ($cpus -gt 0) { $runArgs += @('--cpus', $cpus.ToString($Inv)) }
    $runArgs += $ImageTag

    $combo = [ordered]@{
        Tier = $tierMiB; Cpus = $cpus; Label = $label; Status = "OK"
        ColdStartHealthMs = $null; ColdStartFirstRecMs = $null
        WarmupDiscards = 0; WarmupCapped = $false; WarmupWindows = @()
        Rounds = @(); Throttle = $null; RamAfterRun = $null
    }

    # Cold start: o cronômetro inicia ANTES do docker run e não é truncado.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    docker @runArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { $combo.Status = "FALHA_START"; Stop-MeasureContainer; return [PSCustomObject]$combo }

    $deadline = 300000
    while ($sw.Elapsed.TotalMilliseconds -lt $deadline) {
        if ((Get-StatusCode "$HostUrl/health") -eq 200) { $combo.ColdStartHealthMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1); break }
        Start-Sleep -Milliseconds 25
    }
    if ($null -eq $combo.ColdStartHealthMs) {
        $combo.Status = "TIMEOUT_HEALTH"
        docker logs $ContainerName | Select-Object -Last 15 | ForEach-Object { Write-Host "    $_" }
        Stop-MeasureContainer; return [PSCustomObject]$combo
    }
    while ($sw.Elapsed.TotalMilliseconds -lt $deadline) {
        if ((Get-StatusCode "$HostUrl$RecPath") -eq 200) { $combo.ColdStartFirstRecMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1); break }
        Start-Sleep -Milliseconds 25
    }
    if ($null -eq $combo.ColdStartFirstRecMs) { $combo.Status = "TIMEOUT_RECOMENDACAO"; Stop-MeasureContainer; return [PSCustomObject]$combo }
    Write-Host "  Cold start: /health em $($combo.ColdStartHealthMs) ms; primeira recomendacao 200 em $($combo.ColdStartFirstRecMs) ms"

    # Aquecimento por critério estatístico (DP-1): janelas de 20 descartadas até desvio < 10% da mediana
    # da janela, piso 50 e teto 200 descartes.
    $windows = [System.Collections.Generic.List[object]]::new()
    $discards = 0
    while ($true) {
        $w = Invoke-Load 20
        $discards += 20
        $cv = if ($w.Ok -eq 20 -and $w.P50 -gt 0) { $w.StdDevMs / $w.P50 } else { [double]::PositiveInfinity }
        $windows.Add([PSCustomObject]@{ Discards = $discards; P50 = $w.P50; StdDevMs = $w.StdDevMs; StdDevOverP50 = [Math]::Round($cv, 3); Errors = $w.Errors })
        Write-Host "    aquecimento $discards descartes: p50=$($w.P50) ms stddev=$($w.StdDevMs) ms (stddev/p50=$([Math]::Round($cv, 3)))"
        if ($discards -ge 50 -and $cv -lt 0.10) { break }
        if ($discards -ge 200) { $combo.WarmupCapped = $true; break }
    }
    $combo.WarmupDiscards = $discards
    $combo.WarmupWindows  = $windows.ToArray()

    $status = docker inspect $ContainerName --format "{{.State.Status}}"
    if ($status -ne "running") { $combo.Status = "MORTO_NO_AQUECIMENTO (OOMKilled=$(docker inspect $ContainerName --format '{{.State.OOMKilled}}'))"; Stop-MeasureContainer; return [PSCustomObject]$combo }

    # Rodadas medidas, com o delta do cpu.stat como evidência de throttle.
    $cpuBefore = Get-CpuStat
    for ($r = 1; $r -le $Rounds; $r++) {
        $res = Invoke-Load $RequestsPerRound
        $res | Add-Member -NotePropertyName Round -NotePropertyValue $r
        $combo.Rounds += $res
        Write-Host "    rodada ${r}/${Rounds}: p50=$($res.P50) ms  p95=$($res.P95) ms  p99=$($res.P99) ms  ok=$($res.Ok)/$($res.Requests)"
        $status = docker inspect $ContainerName --format "{{.State.Status}}"
        if ($status -ne "running") { $combo.Status = "MORTO_NA_RODADA_$r (OOMKilled=$(docker inspect $ContainerName --format '{{.State.OOMKilled}}'))"; break }
    }
    $cpuAfter = Get-CpuStat

    $combo.Throttle = [PSCustomObject]@{
        NanoCpus          = [long](docker inspect $ContainerName --format "{{.HostConfig.NanoCpus}}")
        MemoryBytes       = [long](docker inspect $ContainerName --format "{{.HostConfig.Memory}}")
        NrPeriodsDelta    = $cpuAfter['nr_periods']      - $cpuBefore['nr_periods']
        NrThrottledDelta  = $cpuAfter['nr_throttled']    - $cpuBefore['nr_throttled']
        ThrottledUsecDelta= $cpuAfter['throttled_usec']  - $cpuBefore['throttled_usec']
        UsageUsecDelta    = $cpuAfter['usage_usec']      - $cpuBefore['usage_usec']
    }
    $combo.RamAfterRun = docker stats $ContainerName --no-stream --format "{{.MemUsage}}"
    Write-Host "  Throttle: NanoCpus=$($combo.Throttle.NanoCpus) nr_throttled+=$($combo.Throttle.NrThrottledDelta)/$($combo.Throttle.NrPeriodsDelta) periodos throttled_usec+=$($combo.Throttle.ThrottledUsecDelta)"
    Write-Host "  RAM apos a corrida: $($combo.RamAfterRun)"

    Stop-MeasureContainer
    return [PSCustomObject]$combo
}

# ---------------------------------------------------------------------------
# 6. Sanidade bloqueante
# ---------------------------------------------------------------------------

$Metrics = @('P50', 'P95', 'P99')

function Get-Series($combo, [string]$metric) { return [double[]]@($combo.Rounds | ForEach-Object { $_.$metric }) }

function Test-Sanity($combos) {
    $checks = [System.Collections.Generic.List[object]]::new()
    $lowTier  = ($Tiers | Measure-Object -Minimum).Minimum
    $highTier = ($Tiers | Measure-Object -Maximum).Maximum
    $complete = @($combos | Where-Object { $_.Status -eq "OK" -and $_.Rounds.Count -eq $Rounds })

    foreach ($cpus in $CpuLimits) {
        $lo = $complete | Where-Object { $_.Tier -eq $lowTier  -and $_.Cpus -eq $cpus }
        $hi = $complete | Where-Object { $_.Tier -eq $highTier -and $_.Cpus -eq $cpus }
        foreach ($m in $Metrics) {
            if ($null -eq $lo -or $null -eq $hi -or $lowTier -eq $highTier) {
                $checks.Add([PSCustomObject]@{ Check = "S1"; Cpus = $cpus; Metric = $m; Pass = $false; Detail = "par de tiers incompleto, nao avaliavel" })
                $checks.Add([PSCustomObject]@{ Check = "S2"; Cpus = $cpus; Metric = $m; Pass = $false; Detail = "par de tiers incompleto, nao avaliavel" })
                continue
            }
            $sLo = Get-Series $lo $m; $sHi = Get-Series $hi $m
            $medLo = Get-Median $sLo; $medHi = Get-Median $sHi
            $diff  = $medLo - $medHi
            $amp   = [Math]::Max((Get-Amplitude $sLo), (Get-Amplitude $sHi))
            $checks.Add([PSCustomObject]@{ Check = "S1"; Cpus = $cpus; Metric = $m; Pass = ($medLo -ge $medHi)
                Detail = "mediana $lowTier MB = $([Math]::Round($medLo,2)) ms, mediana $highTier MB = $([Math]::Round($medHi,2)) ms" })
            $checks.Add([PSCustomObject]@{ Check = "S2"; Cpus = $cpus; Metric = $m; Pass = ($amp -lt $diff)
                Detail = "amplitude entre rodadas (maior dos tiers) = $([Math]::Round($amp,2)) ms; diferenca entre tiers = $([Math]::Round($diff,2)) ms" })
        }
    }

    foreach ($m in $Metrics) {
        $firstSlowest = 0
        foreach ($c in $complete) {
            $s = Get-Series $c $m
            $strictFall = $true
            for ($i = 1; $i -lt $s.Count; $i++) { if (-not ($s[$i] -lt $s[$i - 1])) { $strictFall = $false } }
            $rest = [double[]]$s[1..($s.Count - 1)]
            $r1Outlier = ($s[0] - ($rest | Measure-Object -Maximum).Maximum) -gt (Get-Amplitude $rest)
            $r1Max = ($s[0] -eq ($s | Measure-Object -Maximum).Maximum)
            if ($r1Max) { $firstSlowest++ }
            $checks.Add([PSCustomObject]@{ Check = "S3"; Cpus = $c.Cpus; Metric = $m; Pass = (-not $strictFall -and -not $r1Outlier)
                Detail = "$($c.Tier) MB: rodadas=[$(($s | ForEach-Object { $_ }) -join ', ')] queda_estrita=$strictFall r1_acima_do_regime=$r1Outlier" })
        }
        $checks.Add([PSCustomObject]@{ Check = "S3"; Cpus = "todas"; Metric = $m; Pass = (-not ($complete.Count -gt 0 -and $firstSlowest -eq $complete.Count))
            Detail = "rodada 1 foi a mais lenta em $firstSlowest de $($complete.Count) combinacoes" })
    }
    if ($complete.Count -ne ($Tiers.Count * $CpuLimits.Count)) {
        $checks.Add([PSCustomObject]@{ Check = "COBERTURA"; Cpus = "todas"; Metric = "-"; Pass = $false; Detail = "$($complete.Count) de $($Tiers.Count * $CpuLimits.Count) combinacoes completas" })
    }
    return $checks.ToArray()
}

# ---------------------------------------------------------------------------
# 7. Execução
# ---------------------------------------------------------------------------

$RunStart = Get-Date
$Combos = [System.Collections.Generic.List[object]]::new()
try {
    Remove-MeasureNetwork
    docker network create $NetworkName | Out-Null
    foreach ($cpus in $CpuLimits) {
        foreach ($tier in $Tiers) {
            $Combos.Add((Measure-Combination $tier $cpus))
        }
    }
}
finally {
    Stop-MeasureContainer
    Remove-MeasureNetwork
}
$ElapsedMin = [Math]::Round(((Get-Date) - $RunStart).TotalMinutes, 1)

$Checks = Test-Sanity $Combos.ToArray()
$Valid  = -not (@($Checks | Where-Object { -not $_.Pass }).Count)

# ---------------------------------------------------------------------------
# 8. Relatório e saída estruturada
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================================"
Write-Host "RESULTADO FINAL"
Write-Host "================================================================"
Write-Host "Commit: $Commit (src limpo: $SrcClean)  Imagem: $ImageTag  Faixas no banco: $TrackCount"
Write-Host "Carga: $LoadImage, 1 conexao, $RequestsPerRound req/rodada, $Rounds rodadas, semente $SeedId"
Write-Host "Docker VM: $DockerVm"
Write-Host "Duracao total: $ElapsedMin min"
Write-Host ""
Write-Host "--- Cold start e aquecimento ---"
foreach ($c in $Combos) {
    Write-Host ("  {0,-26} status={1}  health={2} ms  1a-recomendacao={3} ms  descartes={4}{5}" -f $c.Label, $c.Status, $c.ColdStartHealthMs, $c.ColdStartFirstRecMs, $c.WarmupDiscards, $(if ($c.WarmupCapped) { " (TETO ATINGIDO)" } else { "" }))
}
Write-Host ""
Write-Host "--- Latencia por rodada (ms), mediana e amplitude entre rodadas ---"
foreach ($c in $Combos) {
    if ($c.Rounds.Count -eq 0) { continue }
    Write-Host "  $($c.Label)"
    foreach ($rd in $c.Rounds) { Write-Host ("    R{0}: p50={1,8} p95={2,8} p99={3,8}  ok={4}/{5}" -f $rd.Round, $rd.P50, $rd.P95, $rd.P99, $rd.Ok, $rd.Requests) }
    $line = foreach ($m in $Metrics) { $s = Get-Series $c $m; "{0} mediana={1} amplitude={2}" -f $m, [Math]::Round((Get-Median $s), 2), [Math]::Round((Get-Amplitude $s), 2) }
    Write-Host "    $($line -join ' | ')"
    Write-Host "    throttle: NanoCpus=$($c.Throttle.NanoCpus) nr_throttled+=$($c.Throttle.NrThrottledDelta)/$($c.Throttle.NrPeriodsDelta) throttled_usec+=$($c.Throttle.ThrottledUsecDelta)  RAM=$($c.RamAfterRun)"
}
Write-Host ""
Write-Host "--- Sanidade bloqueante ---"
foreach ($k in $Checks) {
    Write-Host ("  [{0}] {1} cpus={2} {3}: {4}" -f $(if ($k.Pass) { "OK  " } else { "FALHA" }), $k.Check, $k.Cpus, $k.Metric, $k.Detail)
}

if ([string]::IsNullOrEmpty($OutFile)) { $OutFile = Join-Path $env:TEMP "measure-latency-$Commit.json" }
[PSCustomObject]@{
    Commit = $Commit; SrcClean = $SrcClean; Image = $ImageTag; TrackCount = $TrackCount; LoadImage = $LoadImage
    LoadImageDigestNote = "sha256:7be639b288fce624fee54e2ef95c35685f90fc9ed1399812cf856b4b7c6c55ab"
    SeedId = $SeedId; RequestsPerRound = $RequestsPerRound; Rounds = $Rounds; DockerVm = $DockerVm
    ElapsedMinutes = $ElapsedMin; Valid = $Valid; Combinations = $Combos.ToArray(); Sanity = $Checks
} | ConvertTo-Json -Depth 8 | Set-Content -Path $OutFile -Encoding UTF8
Write-Host ""
Write-Host "Saida estruturada: $OutFile"

if (-not $Valid) {
    Write-Host ""
    Write-Host "MEDICAO INVALIDA: ao menos uma sanidade falhou. NAO publique estes numeros." -ForegroundColor Red
    exit 2
}
Write-Host ""
Write-Host "MEDICAO VALIDA: as tres sanidades passaram em todas as combinacoes." -ForegroundColor Green
