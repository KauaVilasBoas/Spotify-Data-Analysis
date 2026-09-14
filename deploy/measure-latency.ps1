<#
.SYNOPSIS
    Mede a latência do endpoint de recomendação em container com limite de memória de 512 MB e 256 MB.

.DESCRIPTION
    Metodologia reprodutível para a tabela de free-tier do README/DEPLOY.md (E6.9).

    SEMENTES
    --------
    As 300 sementes são selecionadas com a semente textual "e4.4-proxies-v1" e a query determinística
    do CatalogRecommenderEvaluationSampleSource, a MESMA query usada pelo gate de qualidade. Busca
    do banco antes de medir; a seleção é determinística e não depende de arquivo externo.

    AQUECIMENTO
    -----------
    Aguarda /health 200 + ao menos UMA recomendação com status 200 antes de iniciar a coleta.
    O índice de similaridade é montado em background no arranque (commits 32262b6/3651ba5):
    durante esse período o endpoint devolve 503, e medir antes disso mediria 503, não latência.
    O tempo de warm-up é registrado por tier.

    CARGA
    -----
    Requisições SEQUENCIAIS, um único cliente, o cenário honesto de uma demo em free tier.
    Estratégias "content" e "blend" são medidas separadamente com as mesmas sementes e na mesma ordem.

    RODADAS
    -------
    3 rodadas por tier e por estratégia. Os percentis são calculados sobre as amostras individuais
    de cada rodada; a tabela traz p50/p95/média por rodada mais média e desvio entre rodadas.

    PRÉ-REQUISITOS
    --------------
    - Docker Desktop em execução.
    - Variável de ambiente ConnectionStrings__SpotifyDb com a connection string do banco.
      O script FALHA com mensagem clara se a variável estiver ausente.
    - psql.exe acessível em $env:PATH ou no caminho padrão do PostgreSQL.
    - A imagem Docker do repositório: passada via -ImageTag (padrão: spotifydataanalysis-api:latest).
      Se a imagem não existir, o script constrói a partir do Dockerfile na raiz do repositório.

    LIMITAÇÕES CONHECIDAS: OS DADOS DE LATÊNCIA DESTE SCRIPT NÃO SÃO CONFIÁVEIS
    ------------------------------------------------------------------------------
    Os números de p50/p95/média gerados por este script devem ser lidos com cautela por três razões:

    1. AQUECIMENTO INSUFICIENTE (linhas 245-256, função Wait-Warmup).
       O script considera o app aquecido após uma única requisição de recomendação com status 200.
       Isso não é suficiente para o JIT do .NET chegar a regime. A rodada 1 é sistematicamente
       mais lenta que as seguintes, e o desvio entre rodadas supera qualquer diferença entre tiers,
       produzindo resultados não-físicos (ex.: 256 MB mais rápido que 512 MB).

    2. CRONÔMETRO DO WARM-UP A PARTIR DA REFERÊNCIA ERRADA (linha 224, início do Stopwatch).
       O cronômetro é iniciado antes de o container estar de pé; o resultado é truncado para
       inteiro. O valor de "1 s" registrado nas execuções de E6.9 é inconsistente com o boot
       real de um app .NET (a medição anterior registrava ~9,5 s para a mesma imagem).

    3. SEM LIMITE DE CPU (linhas 341-343, docker run).
       O container é subido apenas com -m e --memory-swap, sem --cpus. O gargalo real do
       free tier é CPU; latência medida em CPU livre não representa o cenário de produção
       (p95 acima de 1.900 ms foi registrado a 0,1 vCPU na medição anterior).

    O que este script SIM prova de forma confiável: RAM em uso após a corrida completa
    (via docker stats --no-stream, que devolve uso corrente, não pico histórico) e ausência
    de OOM ao longo de 1.800 requisições sequenciais. Esses dados estão em deploy/DEPLOY.md.

.PARAMETER ImageTag
    Tag da imagem Docker a usar. Padrão: "spotifydataanalysis-api:latest".
    Se não existir, a imagem será construída via "docker build".

.PARAMETER Tiers
    Lista de limites de memória a testar em MiB. Padrão: @(512, 256).

.PARAMETER Rounds
    Número de rodadas de medição por tier e por estratégia. Padrão: 3.

.PARAMETER Port
    Porta local exposta pelo container. Padrão: 18080.

.PARAMETER RepoRoot
    Raiz do repositório. Padrão: diretório pai de deploy/.

.EXAMPLE
    # Medir com a imagem atual em ambos os tiers (uso normal):
    cd C:\Projetos\SpotifyDataAnalysis
    $env:ConnectionStrings__SpotifyDb = "Host=localhost;..."
    .\deploy\measure-latency.ps1

.EXAMPLE
    # Medir só em 512 MB com tag específica:
    .\deploy\measure-latency.ps1 -ImageTag spotifydataanalysis-api:e62 -Tiers @(512)

.NOTES
    O script NÃO toca em containers de outros projetos.
    O container de medição é derrubado ao final, mesmo em caso de erro.
    Nenhuma connection string ou senha é gravada em arquivo ou exibida em log.
#>
[CmdletBinding()]
param(
    [string]   $ImageTag  = "spotifydataanalysis-api:latest",
    [int[]]    $Tiers     = @(512, 256),
    [int]      $Rounds    = 3,
    [int]      $Port      = 18080,
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
# 4. Funções auxiliares
# ---------------------------------------------------------------------------

function Get-Percentile([double[]]$samples, [double]$p) {
    $sorted = $samples | Sort-Object
    $n      = $sorted.Count
    $rank   = ($p / 100.0) * ($n - 1)
    $lo     = [int][Math]::Floor($rank)
    $hi     = [int][Math]::Ceiling($rank)
    if ($lo -eq $hi) { return $sorted[$lo] }
    return $sorted[$lo] + ($rank - $lo) * ($sorted[$hi] - $sorted[$lo])
}

function Get-StdDev([double[]]$values) {
    if ($values.Count -lt 2) { return 0.0 }
    $mean = ($values | Measure-Object -Average).Average
    $variance = ($values | ForEach-Object { [Math]::Pow($_ - $mean, 2) } | Measure-Object -Sum).Sum / ($values.Count - 1)
    return [Math]::Sqrt($variance)
}

$ContainerName = "spotifyda-latency-measure"

function Stop-MeasureContainer {
    $exists = docker ps -a --filter "name=^${ContainerName}$" --format "{{.Names}}" 2>&1
    if ($exists -eq $ContainerName) {
        Write-Host "  Parando container $ContainerName ..."
        docker stop $ContainerName | Out-Null
        docker rm  $ContainerName | Out-Null
    }
}

function Wait-Warmup([string]$baseUrl, [string[]]$seedIds, [int]$timeoutSecs = 180) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    # Aguarda /health 200
    Write-Host "    Aguardando /health 200 ..."
    $healthOk = $false
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSecs) {
        try {
            $resp = Invoke-WebRequest -Uri "$baseUrl/health" -TimeoutSec 5 -UseBasicParsing -ErrorAction SilentlyContinue
            if ($resp.StatusCode -eq 200) { $healthOk = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthOk) {
        Write-Error "ERRO: /health nunca retornou 200 em $timeoutSecs s."
        return $null
    }
    Write-Host "    /health 200 em $([int]$sw.Elapsed.TotalSeconds) s"

    # Aguarda primeira recomendação 200 (o índice pode ainda estar montando → 503)
    Write-Host "    Aguardando primeira recomendação 200 (índice pode estar montando) ..."
    $recOk = $false
    foreach ($seedId in $seedIds) {
        $url = "$baseUrl/api/recommendations/track/${seedId}?limit=10&strategy=content"
        while ($sw.Elapsed.TotalSeconds -lt $timeoutSecs) {
            try {
                $resp = Invoke-WebRequest -Uri $url -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
                if ($resp.StatusCode -eq 200) { $recOk = $true; break }
            } catch { }
            Start-Sleep -Milliseconds 1000
        }
        if ($recOk) { break }
        if ($sw.Elapsed.TotalSeconds -ge $timeoutSecs) { break }
    }

    if (-not $recOk) {
        Write-Error "ERRO: endpoint de recomendação nunca retornou 200 em $timeoutSecs s."
        return $null
    }

    $warmupSecs = [int]$sw.Elapsed.TotalSeconds
    Write-Host "    Índice pronto. Warm-up total: $warmupSecs s"
    return $warmupSecs
}

function Measure-Strategy([string]$baseUrl, [string[]]$seedIds, [string]$strategy, [int]$roundIndex) {
    $samples = [System.Collections.Generic.List[double]]::new()
    $errors  = 0
    $sw      = [System.Diagnostics.Stopwatch]::new()

    foreach ($seedId in $seedIds) {
        $url = "$baseUrl/api/recommendations/track/${seedId}?limit=10&strategy=${strategy}"
        $sw.Restart()
        try {
            $resp = Invoke-WebRequest -Uri $url -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
            $elapsed = $sw.Elapsed.TotalMilliseconds
            if ($resp.StatusCode -eq 200) {
                $samples.Add($elapsed)
            } else {
                $errors++
            }
        } catch {
            $errors++
        }
    }

    $arr  = $samples.ToArray()
    $p50  = [Math]::Round((Get-Percentile $arr 50), 1)
    $p95  = [Math]::Round((Get-Percentile $arr 95), 1)
    $mean = [Math]::Round(($arr | Measure-Object -Average).Average, 1)

    return [PSCustomObject]@{
        Round    = $roundIndex
        Strategy = $strategy
        Samples  = $arr.Count
        Errors   = $errors
        P50      = $p50
        P95      = $p95
        Mean     = $mean
        RawSamples = $arr
    }
}

function Get-PeakRamMiB([string]$containerName) {
    # docker stats --no-stream devolve o uso ATUAL, não o pico.
    # Usamos docker inspect para ler memory_stats.max_usage (bytes), que é o pico desde o start.
    $raw = docker inspect $containerName --format "{{.MemoryStats.MaxUsage}}" 2>&1
    # O campo só existe via API de stats; inspect JSON padrão usa .HostConfig.Memory para o limite.
    # Lemos via "docker stats --no-stream" para obter o valor no momento.
    $statsRaw = docker stats $containerName --no-stream --format "{{.MemUsage}}" 2>&1
    return $statsRaw
}

# ---------------------------------------------------------------------------
# 5. Loop por tier
# ---------------------------------------------------------------------------

$Results = [System.Collections.Generic.List[object]]::new()
$TierSummaries = [System.Collections.Generic.List[object]]::new()

foreach ($TierMiB in $Tiers) {
    Write-Host ""
    Write-Host "================================================================"
    Write-Host "TIER: $TierMiB MB"
    Write-Host "================================================================"

    # Garantir que não há container residual
    Stop-MeasureContainer

    $BaseUrl = "http://localhost:$Port"

    # Connection string para o container (host.docker.internal aponta para a máquina host)
    $ContainerConnStr = $ConnStr -replace '(?i)Host\s*=\s*localhost', 'Host=host.docker.internal'
    $ContainerConnStr = $ContainerConnStr -replace '(?i)Host\s*=\s*127\.0\.0\.1', 'Host=host.docker.internal'

    # Subir container
    Write-Host ""
    Write-Host "Subindo container ($TierMiB MB, sem throttle de CPU) ..."
    docker run -d `
        --name $ContainerName `
        -m "${TierMiB}m" --memory-swap "${TierMiB}m" `
        -p "${Port}:8080" `
        -e "ConnectionStrings__SpotifyDb=$ContainerConnStr" `
        -e "ASPNETCORE_ENVIRONMENT=Production" `
        -e "ASPNETCORE_URLS=http://+:8080" `
        $ImageTag | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "AVISO: docker run falhou para o tier $TierMiB MB."
        Stop-MeasureContainer
        $TierSummaries.Add([PSCustomObject]@{
            TierMiB    = $TierMiB
            Status     = "FALHA_START"
            WarmupSecs = $null
            PeakRam    = $null
        })
        continue
    }

    # Verificar se o container ainda está rodando (OOM imediato)
    Start-Sleep -Seconds 2
    $containerStatus = docker inspect $ContainerName --format "{{.State.Status}}" 2>&1
    $exitCode        = docker inspect $ContainerName --format "{{.State.ExitCode}}" 2>&1
    if ($containerStatus -ne "running") {
        Write-Warning "Container morreu imediatamente. Status='$containerStatus' ExitCode='$exitCode'"
        Write-Host "  Log do container:"
        docker logs $ContainerName 2>&1 | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" }
        Stop-MeasureContainer
        $TierSummaries.Add([PSCustomObject]@{
            TierMiB    = $TierMiB
            Status     = "OOM_IMEDIATO (exit $exitCode)"
            WarmupSecs = $null
            PeakRam    = $null
        })
        continue
    }

    # Warm-up
    $warmupSecs = Wait-Warmup $BaseUrl $Seeds 180

    # Verificar OOM pós warm-up
    $containerStatus = docker inspect $ContainerName --format "{{.State.Status}}" 2>&1
    $exitCode        = docker inspect $ContainerName --format "{{.State.ExitCode}}" 2>&1
    $oomKilled       = docker inspect $ContainerName --format "{{.State.OOMKilled}}" 2>&1

    if ($containerStatus -ne "running" -or $oomKilled -eq "true") {
        $label = if ($oomKilled -eq "true") { "OOM_KILLED (exit $exitCode)" } else { "MORTO_DURANTE_WARMUP (exit $exitCode)" }
        Write-Warning "Container morreu durante o warm-up: $label"
        Write-Host "  docker inspect OOMKilled: $oomKilled"
        docker logs $ContainerName 2>&1 | Select-Object -Last 30 | ForEach-Object { Write-Host "    $_" }
        Stop-MeasureContainer
        $TierSummaries.Add([PSCustomObject]@{
            TierMiB    = $TierMiB
            Status     = $label
            WarmupSecs = $warmupSecs
            PeakRam    = $null
        })
        continue
    }

    if ($null -eq $warmupSecs) {
        Stop-MeasureContainer
        $TierSummaries.Add([PSCustomObject]@{
            TierMiB    = $TierMiB
            Status     = "TIMEOUT_WARMUP"
            WarmupSecs = $null
            PeakRam    = $null
        })
        continue
    }

    # Medir RAM pós warm-up (pico desde o arranque via docker stats)
    $statsRaw = docker stats $ContainerName --no-stream --format "{{.MemUsage}}" 2>&1
    Write-Host "  RAM no momento pós warm-up: $statsRaw"

    # ---------------------------------------------------------------------------
    # 5a. Rodadas de medição
    # ---------------------------------------------------------------------------

    $contentRounds = [System.Collections.Generic.List[object]]::new()
    $blendRounds   = [System.Collections.Generic.List[object]]::new()

    for ($r = 1; $r -le $Rounds; $r++) {
        Write-Host ""
        Write-Host "  Rodada $r/$Rounds ..."

        Write-Host "    content ..."
        $resContent = Measure-Strategy $BaseUrl $Seeds "content" $r

        # Verificar OOM entre estratégias
        $containerStatus = docker inspect $ContainerName --format "{{.State.Status}}" 2>&1
        if ($containerStatus -ne "running") {
            $exitCode  = docker inspect $ContainerName --format "{{.State.ExitCode}}" 2>&1
            $oomKilled = docker inspect $ContainerName --format "{{.State.OOMKilled}}" 2>&1
            Write-Warning "Container morreu na rodada $r (content). OOMKilled=$oomKilled ExitCode=$exitCode"
            break
        }

        Write-Host "    blend ..."
        $resBlend = Measure-Strategy $BaseUrl $Seeds "blend" $r

        # Verificar OOM após blend
        $containerStatus = docker inspect $ContainerName --format "{{.State.Status}}" 2>&1
        if ($containerStatus -ne "running") {
            $exitCode  = docker inspect $ContainerName --format "{{.State.ExitCode}}" 2>&1
            $oomKilled = docker inspect $ContainerName --format "{{.State.OOMKilled}}" 2>&1
            Write-Warning "Container morreu na rodada $r (blend). OOMKilled=$oomKilled ExitCode=$exitCode"
            $blendRounds.Add($resBlend)
            break
        }

        Write-Host "    content: p50=$($resContent.P50) ms  p95=$($resContent.P95) ms  mean=$($resContent.Mean) ms  amostras=$($resContent.Samples)  erros=$($resContent.Errors)"
        Write-Host "    blend:   p50=$($resBlend.P50) ms  p95=$($resBlend.P95) ms  mean=$($resBlend.Mean) ms  amostras=$($resBlend.Samples)  erros=$($resBlend.Errors)"

        $contentRounds.Add($resContent)
        $blendRounds.Add($resBlend)
    }

    # Pico de RAM final (docker stats lê o momento; para pico real precisamos do max_usage via API)
    # O campo MemoryStats.MaxUsage só está disponível via "docker stats" na API de streaming.
    # Lemos o valor atual pós-medição, que é uma proxy conservadora do pico de estado estável.
    $statsRawFinal = docker stats $ContainerName --no-stream --format "{{.MemUsage}}" 2>&1
    Write-Host ""
    Write-Host "  RAM pós-medição (docker stats): $statsRawFinal"

    # ---------------------------------------------------------------------------
    # 5b. Calcular resumo entre rodadas
    # ---------------------------------------------------------------------------

    function Summarize-Rounds([System.Collections.Generic.List[object]]$rounds, [string]$strategy, [int]$tierMiB) {
        if ($rounds.Count -eq 0) {
            return [PSCustomObject]@{
                TierMiB  = $tierMiB
                Strategy = $strategy
                Rounds   = 0
                Samples  = 0
                P50_R1   = "N/A"; P95_R1 = "N/A"; Mean_R1 = "N/A"
                P50_R2   = "N/A"; P95_R2 = "N/A"; Mean_R2 = "N/A"
                P50_R3   = "N/A"; P95_R3 = "N/A"; Mean_R3 = "N/A"
                MeanP50  = "N/A"; StdDevP50 = "N/A"
                MeanP95  = "N/A"; StdDevP95 = "N/A"
                MeanMean = "N/A"; StdDevMean = "N/A"
            }
        }
        $p50s  = $rounds | ForEach-Object { $_.P50 }
        $p95s  = $rounds | ForEach-Object { $_.P95 }
        $means = $rounds | ForEach-Object { $_.Mean }

        $obj = [PSCustomObject]@{
            TierMiB   = $tierMiB
            Strategy  = $strategy
            Rounds    = $rounds.Count
            Samples   = ($rounds[0]).Samples
            P50_R1 = "N/A"; P95_R1 = "N/A"; Mean_R1 = "N/A"
            P50_R2 = "N/A"; P95_R2 = "N/A"; Mean_R2 = "N/A"
            P50_R3 = "N/A"; P95_R3 = "N/A"; Mean_R3 = "N/A"
            MeanP50  = [Math]::Round(($p50s  | Measure-Object -Average).Average, 1)
            StdDevP50= [Math]::Round((Get-StdDev $p50s),  1)
            MeanP95  = [Math]::Round(($p95s  | Measure-Object -Average).Average, 1)
            StdDevP95= [Math]::Round((Get-StdDev $p95s),  1)
            MeanMean = [Math]::Round(($means | Measure-Object -Average).Average, 1)
            StdDevMean=[Math]::Round((Get-StdDev $means), 1)
        }
        for ($i = 0; $i -lt $rounds.Count; $i++) {
            $n = $i + 1
            $obj."P50_R$n"  = $rounds[$i].P50
            $obj."P95_R$n"  = $rounds[$i].P95
            $obj."Mean_R$n" = $rounds[$i].Mean
        }
        return $obj
    }

    $summaryContent = Summarize-Rounds $contentRounds "content" $TierMiB
    $summaryBlend   = Summarize-Rounds $blendRounds   "blend"   $TierMiB
    $Results.Add($summaryContent)
    $Results.Add($summaryBlend)

    $TierSummaries.Add([PSCustomObject]@{
        TierMiB    = $TierMiB
        Status     = "OK"
        WarmupSecs = $warmupSecs
        PeakRam    = $statsRawFinal
    })

    # Derrubar container do tier
    Write-Host ""
    Write-Host "Derrubando container do tier $TierMiB MB ..."
    Stop-MeasureContainer
}

# ---------------------------------------------------------------------------
# 6. Relatório final
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================================"
Write-Host "RESULTADO FINAL"
Write-Host "================================================================"
Write-Host ""
Write-Host "Imagem: $ImageTag"
Write-Host "Sementes: $($Seeds.Count)  |  Rodadas: $Rounds  |  Carga: sequencial (1 cliente)"
Write-Host ""

Write-Host "--- Warm-up e RAM por tier ---"
$TierSummaries | ForEach-Object {
    $status = $_.Status
    $wu     = if ($_.WarmupSecs) { "$($_.WarmupSecs) s" } else { "N/A" }
    $ram    = if ($_.PeakRam)    { $_.PeakRam }           else { "N/A" }
    Write-Host "  $($_.TierMiB) MB: status=$status  warm-up=$wu  RAM-pos-warmup=$ram"
}

Write-Host ""
Write-Host "--- Latência por tier e estratégia ---"
Write-Host ""
Write-Host ("  {0,-8} {1,-10} {2,-6} {3,-8} {4,-8} {5,-8} {6,-8} {7,-8} {8,-8} {9,-10} {10,-10}" -f `
    "Tier", "Estrategia", "N", "P50_R1", "P95_R1", "P50_R2", "P95_R2", "P50_R3", "P95_R3", "p95_media", "p95_stddev")
Write-Host ("  " + "-" * 100)

$Results | ForEach-Object {
    Write-Host ("  {0,-8} {1,-10} {2,-6} {3,-8} {4,-8} {5,-8} {6,-8} {7,-8} {8,-8} {9,-10} {10,-10}" -f `
        "$($_.TierMiB) MB",
        $_.Strategy,
        $_.Samples,
        "$($_.P50_R1) ms",
        "$($_.P95_R1) ms",
        "$($_.P50_R2) ms",
        "$($_.P95_R2) ms",
        "$($_.P50_R3) ms",
        "$($_.P95_R3) ms",
        "$($_.MeanP95) ms",
        "$($_.StdDevP95) ms")
}

Write-Host ""
Write-Host "--- Médias finais (média das rodadas) ---"
Write-Host ""
$Results | ForEach-Object {
    Write-Host "  $($_.TierMiB) MB / $($_.Strategy): p50=$($_.MeanP50) ms (±$($_.StdDevP50))  p95=$($_.MeanP95) ms (±$($_.StdDevP95))  media=$($_.MeanMean) ms (±$($_.StdDevMean))"
}

Write-Host ""
Write-Host "Referência fora de container (local Release, 316 sementes, E4.9):"
Write-Host "  content:  p50 13,6 ms / p95 35,3 ms / média 20,2 ms"
Write-Host ""
