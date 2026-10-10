<#
.SYNOPSIS
    Mede latência (média/p50/p95/p99) e cold start do endpoint de recomendação em container, nas quatro
    combinações {512 MB, 256 MB} x {--cpus 0.1, sem throttle}, e valida a própria medição.

.DESCRIPTION
    Metodologia reprodutível para a seção de free-tier do README/DEPLOY.md (E6.9, refeita no E6.12,
    segunda fase: validade pelo eixo de CPU, com sementes amostradas). UM comando, sem passo manual:
    ver .EXAMPLE.

    FERRAMENTA DE CARGA
    -------------------
    k6, imagem oficial grafana/k6 fixada por digest (v2.3.0, digest
    sha256:e66db15b860113878fa74670e31f5e274830b7b6e42c8bff28b2f2d86a257603 no valor padrão de
    -LoadImage; o JSON grava o digest REAL lido do Docker e a versão lida de "k6 version" para a imagem
    usada, que mudam se alguém passar -LoadImage). Roda em container descartável (docker run --rm) NA
    MESMA REDE DOCKER da API, falando com ela pelo nome do container; o script do k6,
    deploy/measure-latency.k6.js (texto, versionado), é montado somente leitura. Média, p50, p95 e p99
    vêm do k6 (summaryTrendStats declarado no .js; o resumo sai em JSON, numa linha "K6SUMMARY" do
    stdout), nenhum é calculado em PowerShell. A duração é a http_req_duration do k6 (envio, espera e
    recebimento; a conexão keep-alive é reaproveitada, então o handshake não entra). Nada é instalado:
    só Docker é necessário.

    CARGA
    -----
    1 usuário virtual (executor per-vu-iterations, vus=1), requisições sequenciais, o cenário honesto de
    uma demo em free tier. As sementes são as 300 da seleção determinística (seed 'e4.4-proxies-v1',
    ORDER BY md5, a mesma ordem sempre). Elas chegam ao k6 pela variável de ambiente SEEDS (ids
    separados por vírgula) e a iteração i usa SEEDS[i]: a ordem é fixa. Cada rodada medida é UMA
    passada sequencial pelas 300 (-SeedCount). Antes de medir, um container de pré-voo (512 MB, sem
    throttle, fora da medição) confirma que as 300 sementes respondem 200; qualquer 404/422/outro
    código aborta o script com a semente e o código, não entra como erro silencioso.
    Só a estratégia "content" é medida; "blend" (que consulta a co-ocorrência no Postgres) está fora
    desta medição.

    AQUECIMENTO
    -----------
    Antes das rodadas de cada combinação, janelas de 20 requisições são descartadas (fora da amostra).
    Cada janela repete as MESMAS 20 primeiras sementes na mesma ordem, de modo que a diferença entre
    janelas é deriva de regime e não custo diferente de semente. Critério de estabilização: a MÉDIA
    (do k6) de duas janelas consecutivas difere menos de 10% (|m_k - m_k-1| / m_k-1 < 0,10). PISO de
    50 e TETO de 200 descartes: como a janela é de 20, o mínimo efetivo é 60 e o máximo 200. Janela
    com resposta diferente de 200 não conta. Se o teto for atingido sem estabilizar, a combinação segue
    medida mas é marcada WarmupCapped=true e a linha final do veredito avisa. O número de descartes e as
    médias de cada janela saem na saída estruturada. O critério anterior (desvio da janela abaixo de
    10% da mediana) nunca convergia sob cota de CFS, porque a latência é bimodal dentro da janela; a
    média de janelas não depende disso. Ele é um critério de estabilidade da média, não prova de que o
    JIT chegou ao tier final. As 20 sementes de aquecimento são as mesmas das 20 primeiras posições de
    cada rodada medida, o que pode favorecê-las levemente (cache do Postgres).

    COLD START
    ----------
    O cronômetro (Stopwatch, ms, sem truncar) inicia imediatamente ANTES do "docker run". Dois números,
    medidos 3 vezes por combinação (3 "docker run" novos, o container anterior é parado e removido):
      (a) tempo até /health responder 200;
      (b) tempo até a primeira recomendação 200.
    O índice é montado em background no arranque (commits 32262b6/3651ba5), então (b) inclui a
    espera dos 503. O polling roda no host (HttpClient, 25 ms), pela porta publicada. O JSON traz as
    três medições; o console traz mediana e faixa (min a max). A carga (aquecimento e 5 rodadas) roda
    no container do TERCEIRO arranque. Os arranques 2 e 3 acontecem com o cache de páginas do Postgres
    e do disco já quentes pelo anterior: a faixa mostra isso, não o esconde.

    CPU
    ---
    --cpus 0.1 é aplicado de verdade e provado: o script grava HostConfig.NanoCpus e o delta de
    nr_throttled / throttled_usec do cpu.stat do cgroup do container durante as rodadas medidas.
    A combinação "sem throttle" roda sem --cpus (todas as CPUs da VM do Docker).

    RODADAS (DP-2)
    --------------
    5 rodadas por combinação (-Rounds), cada uma UMA passada pelas 300 sementes, sempre no mesmo
    container (o aquecimento vale para todas). Por rodada: média, p50, p95, p99. Entre rodadas:
    mediana e amplitude (max-min) de cada uma dessas quatro estatísticas.

    VALIDADE BLOQUEANTE
    -------------------
    Se QUALQUER uma falhar a medição é INVÁLIDA, o script imprime os números crus, sai com código 2 e
    nada deve ir para o README. Elas substituem as antigas S1/S2 (que exigiam 256 MB mais lento que
    512 MB, efeito que não existe: a aplicação usa cerca de 160 MiB e cabe nos dois tiers) e o eixo
    medido passa a ser o da CPU. V1 e V2 usam a MÉDIA, de propósito: sob cota de CFS os percentis são
    bimodais por natureza, então p50/p95/p99 são reportados com a amplitude ao lado, não gateados.
      THROTTLE  com Cpus > 0, HostConfig.NanoCpus tem de ser o pedido e nr_throttled do cpu.stat tem
          de ter crescido durante as rodadas; com Cpus = 0, NanoCpus tem de ser 0.
      V1  direção: em cada tier de RAM, a mediana (entre rodadas) da média a 0,1 CPU é MAIOR que a
          sem throttle.
      V2  sinal acima do ruído: em cada tier de RAM, a diferença entre as medianas da média das duas
          configurações de CPU é MAIOR que a maior amplitude entre rodadas das duas.
      V3  cobertura: as quatro combinações completas (3 cold starts e todas as rodadas) e zero resposta
          diferente de 200 nas rodadas medidas, cada rodada com todas as requisições.

    REPORTADO, NÃO BLOQUEANTE
    -------------------------
      Neutralidade de RAM  para cada configuração de CPU, a diferença entre as medianas da média de
          256 e 512 MB com a maior amplitude ao lado, e "dentro do ruído" (|diferença| <= amplitude)
          ou "fora do ruído".
      Rodada 1 mais lenta  aviso quando, em uma combinação, a média da rodada 1 é a maior e supera a
          maior das rodadas 2..N por mais que a amplitude das rodadas 2..N.

    PRÉ-REQUISITOS
    --------------
    - Docker Desktop em execução, com cgroup v2 APENAS (o script aborta em cgroup v1).
    - Variável de ambiente ConnectionStrings__SpotifyDb com a connection string do banco.
      O script FALHA com mensagem clara se a variável estiver ausente.
    - psql.exe acessível em $env:PATH ou no caminho padrão do PostgreSQL (o banco só é LIDO).
    - A imagem Docker do repositório: por padrão spotifydataanalysis-api:<commit curto do HEAD>.
      Se não existir, o script constrói a partir do Dockerfile na raiz do repositório.

    LIMITAÇÕES CONHECIDAS (após a segunda fase do E6.12)
    ----------------------------------------------------
    Deixaram de ser limitação: aquecimento de uma requisição só; cronômetro do cold start iniciado
    depois do docker run e truncado; ausência de limite de CPU; cliente Invoke-WebRequest; semente
    única; cold start de amostra única; critério de validade sobre o eixo de RAM.
    Continuam sendo:
    1. Só a estratégia "content"; "blend" (que consulta a co-ocorrência no Postgres) não é medida.
    2. Cliente e API dividem a mesma máquina; o Postgres também é o da máquina, fora de container e de
       qualquer limite de CPU/RAM. Latência de banco de um Postgres gerenciado remoto não está incluída.
    3. --cpus 0.1 é cota CFS (throttling em fatias de 100 ms), não uma vCPU lenta: o comportamento sob
       um provedor de free tier pode diferir do de um host local. Sob essa cota os percentis são
       bimodais; por isso a validade é sobre a média.
    4. O aquecimento mede estabilidade da média entre janelas de 20 sementes repetidas; não prova que o
       JIT chegou ao tier final.
    5. A RAM lida é o uso corrente no fim da corrida (docker stats --no-stream), não o pico histórico.
    6. O cold start depende do cache de páginas do Postgres e do disco do momento (ver COLD START).
    7. A ordem das combinações é fixa (512 antes de 256 dentro de cada CPU, CPU limitada antes da
       livre): deriva lenta do host pode aparecer como diferença entre combinações.
    8. A connection string vai para o container por -e na linha de comando do docker run, visível em
       docker inspect e na lista de processos do host. O script anterior já fazia assim; aceitável
       para máquina de desenvolvimento. (As sementes e a URL passadas ao k6 não são sensíveis.)
    9. A reescrita de Host=localhost para host.docker.internal não tem âncora e pressupõe Docker Desktop.
    10. Os 300 ids vão ao k6 pela linha de comando do docker run (cerca de 7 kB), dentro do limite do
        Windows; um número muito maior de sementes exigiria arquivo montado.

.PARAMETER ImageTag
    Tag da imagem Docker a usar. Padrão: "spotifydataanalysis-api:<commit curto do HEAD>".
    Se não existir, a imagem será construída via "docker build".

.PARAMETER Tiers
    Limites de memória a testar, em MiB. Padrão: @(512, 256).

.PARAMETER CpuLimits
    Limites de CPU (valor de --cpus). 0 significa sem throttle. Padrão: @(0.1, 0).

.PARAMETER Rounds
    Rodadas de medição por combinação. Padrão: 5 (DP-2).

.PARAMETER SeedCount
    Quantas sementes da seleção entram (e, portanto, requisições por rodada: uma passada). Padrão: 300.
    Valores menores servem só para ensaiar o encanamento; a medição publicável usa 300.

.PARAMETER Port
    Porta local publicada pelo container (usada só no polling do cold start). Padrão: 18080.

.PARAMETER LoadImage
    Imagem do gerador de carga. Padrão: grafana/k6@sha256:e66db15b...7603 (v2.3.0, digest completo no
    valor padrão do parâmetro). O JSON grava o digest real lido do Docker para a imagem usada.

.PARAMETER OutFile
    Arquivo JSON com a saída estruturada. Padrão: measure-latency-<commit>.json na pasta temporária
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
    .\deploy\measure-latency.ps1 -Tiers @(512) -CpuLimits @(0) -Rounds 2 -SeedCount 20

.NOTES
    COMO RODAR: sem redirecionar fluxos. Se a imagem ainda não existe, o script a constrói com "docker
    build", que escreve o progresso em stderr; no Windows PowerShell 5.1, redirecionar com *> ou 2>&1
    transforma esse stderr em erro (NativeCommandError) e, com $ErrorActionPreference = "Stop", derruba
    o script no build. Rodar direto (.\deploy\measure-latency.ps1) constrói e segue. Para guardar a saída
    em arquivo use Start-Transcript, ou construa a imagem antes (docker build -t spotifydataanalysis-api:<commit> .)
    e então redirecione à vontade, pois o script passa a usar a imagem existente.

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
    [int]      $SeedCount = 300,
    [int]      $Port      = 18080,
    [string]   $LoadImage = "grafana/k6@sha256:e66db15b860113878fa74670e31f5e274830b7b6e42c8bff28b2f2d86a257603",
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
$CgroupVersion = (docker info --format "{{.CgroupVersion}}" | Out-String).Trim()
if ($CgroupVersion -ne "2") {
    Write-Error "ERRO: este script exige cgroup v2 (a prova de throttle le /sys/fs/cgroup/cpu.stat); o Docker reporta cgroup v'$CgroupVersion'."
    exit 1
}
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
Write-Host "Buscando a seleção determinística de sementes do banco (seed='e4.4-proxies-v1'); as $SeedCount primeiras entram na carga..."

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

if ($Seeds.Count -lt $SeedCount -or $SeedCount -lt 20) {
    Write-Error "ERRO: o banco devolveu $($Seeds.Count) sementes e a medição pede $SeedCount (mínimo 20, o tamanho da janela de aquecimento). Verifique a connection string e o banco."
    exit 1
}
$Seeds = @($Seeds | Select-Object -First $SeedCount)
Write-Host "  $($Seeds.Count) sementes carregadas."

# ---------------------------------------------------------------------------
# 4. Banco (somente leitura) e funções auxiliares
# ---------------------------------------------------------------------------

$TrackCount = (& $PsqlExe -h $dbHost -p $dbPort -U $dbUser -d $dbName -t -A -c "SELECT count(*) FROM catalog.tracks;" | Out-String).Trim()
Write-Host "  catalog.tracks no banco: $TrackCount faixas"

$SeedId  = $Seeds[0]
$RecPath = "/api/recommendations/track/${SeedId}?limit=10&strategy=content"
$SeedList = $Seeds -join ','
$WarmWindow = 20

$ContainerName = "spotifyda-latency-measure"
$LoadName      = "spotifyda-latency-load"
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

function Remove-LoadContainer {
    $exists = docker ps -a --filter "name=^${LoadName}$" --format "{{.Names}}"
    if ($exists -eq $LoadName) { docker rm -f $LoadName | Out-Null }
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

# Roda o k6 (container descartável, mesma rede da API, 1 VU) sobre as primeiras $requests sementes,
# em ordem. Média e percentis vêm do k6 (linha K6SUMMARY do stdout), em ms.
function Invoke-Load([int]$requests) {
    $out = docker run --rm --name $LoadName --network $NetworkName -v "${PSScriptRoot}:/scripts:ro" `
        -e "BASE_URL=$InternalUrl" -e "SEEDS=$SeedList" -e "ITERATIONS=$requests" `
        $LoadImage run --quiet --no-color /scripts/measure-latency.k6.js 2>$null
    if ($LASTEXITCODE -ne 0) { throw "k6 falhou (exit $LASTEXITCODE): $out" }
    $line = @($out | Where-Object { "$_" -like 'K6SUMMARY *' })
    if ($line.Count -ne 1) { throw "k6 nao emitiu exatamente uma linha K6SUMMARY (saida: $($out -join ' | '))" }
    $res = ("$($line[0])".Substring(10)) | ConvertFrom-Json
    return [PSCustomObject]@{
        Requests = $requests
        Done     = [int]$res.requests
        Non200   = [int]$res.non200
        Mean     = [Math]::Round([double]$res.avg, 2)
        P50      = [Math]::Round([double]$res.p50, 2)
        P95      = [Math]::Round([double]$res.p95, 2)
        P99      = [Math]::Round([double]$res.p99, 2)
        MaxMs    = [Math]::Round([double]$res.max, 2)
    }
}

function Get-CpuStat {
    $h = @{}
    $raw = docker exec $ContainerName cat /sys/fs/cgroup/cpu.stat
    if ($LASTEXITCODE -ne 0) { throw "Nao foi possivel ler /sys/fs/cgroup/cpu.stat no container (exit $LASTEXITCODE): a prova de throttle e impossivel." }
    foreach ($line in $raw) {
        $kv = "$line" -split '\s+'
        if ($kv.Count -eq 2) { $h[$kv[0]] = [long]$kv[1] }
    }
    foreach ($k in 'nr_periods', 'nr_throttled', 'throttled_usec', 'usage_usec') {
        if (-not $h.ContainsKey($k)) { throw "cpu.stat sem a chave '$k' (saida: $($raw -join ' | ')): a prova de throttle e impossivel." }
    }
    return $h
}

# ---------------------------------------------------------------------------
# 5. Uma combinação (RAM x CPU): cold start, aquecimento, rodadas
# ---------------------------------------------------------------------------

$ContainerConnStr = $ConnStr -replace '(?i)Host\s*=\s*localhost', 'Host=host.docker.internal'
$ContainerConnStr = $ContainerConnStr -replace '(?i)Host\s*=\s*127\.0\.0\.1', 'Host=host.docker.internal'

# Sobe o container e mede o cold start: o cronômetro inicia ANTES do docker run e não é truncado.
# Devolve Status "OK" ou o motivo da falha (o container já foi removido nesse caso).
function Start-Measured([int]$tierMiB, [double]$cpus) {
    Stop-MeasureContainer

    $runArgs = @('run', '-d', '--name', $ContainerName, '--network', $NetworkName,
        '-m', "${tierMiB}m", '--memory-swap', "${tierMiB}m", '-p', "${Port}:8080",
        '-e', "ConnectionStrings__SpotifyDb=$ContainerConnStr",
        '-e', 'ASPNETCORE_ENVIRONMENT=Production', '-e', 'ASPNETCORE_URLS=http://+:8080')
    if ($cpus -gt 0) { $runArgs += @('--cpus', $cpus.ToString($Inv)) }
    $runArgs += $ImageTag

    $r = [ordered]@{ Status = "OK"; HealthMs = $null; FirstRecMs = $null }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    docker @runArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { $r.Status = "FALHA_START"; Stop-MeasureContainer; return [PSCustomObject]$r }

    $deadline = 300000
    while ($sw.Elapsed.TotalMilliseconds -lt $deadline) {
        if ((Get-StatusCode "$HostUrl/health") -eq 200) { $r.HealthMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1); break }
        Start-Sleep -Milliseconds 25
    }
    if ($null -eq $r.HealthMs) {
        $r.Status = "TIMEOUT_HEALTH"
        docker logs $ContainerName | Select-Object -Last 15 | ForEach-Object { Write-Host "    $_" }
        Stop-MeasureContainer; return [PSCustomObject]$r
    }
    while ($sw.Elapsed.TotalMilliseconds -lt $deadline) {
        $code = Get-StatusCode "$HostUrl$RecPath"
        if ($code -eq 200) { $r.FirstRecMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1); break }
        if ($code -in 404, 422) { throw "A semente $SeedId devolveu HTTP $code no endpoint de recomendacao: a medicao seria de erro, nao de latencia." }
        Start-Sleep -Milliseconds 25
    }
    if ($null -eq $r.FirstRecMs) { $r.Status = "TIMEOUT_RECOMENDACAO"; Stop-MeasureContainer }
    return [PSCustomObject]$r
}

# Pré-voo: todas as sementes têm de responder 200 antes de qualquer medição. Container descartável
# (512 MB, sem throttle), fora das medições; a falha derruba o script com a semente e o código.
function Test-Seeds {
    Write-Host ""
    Write-Host "Pre-voo: confirmando que as $($Seeds.Count) sementes respondem 200 ..."
    $s = Start-Measured 512 0
    if ($s.Status -ne "OK") { throw "Pre-voo: o container nao ficou pronto ($($s.Status))." }
    $bad = @()
    foreach ($id in $Seeds) {
        $code = Get-StatusCode "$HostUrl/api/recommendations/track/${id}?limit=10&strategy=content"
        if ($code -ne 200) { $bad += "$id=HTTP $code" }
    }
    Stop-MeasureContainer
    if ($bad.Count -gt 0) { throw "Pre-voo: $($bad.Count) de $($Seeds.Count) sementes nao responderam 200: $($bad -join '; ')" }
    Write-Host "  as $($Seeds.Count) sementes responderam 200."
}

function Measure-Combination([int]$tierMiB, [double]$cpus) {
    $label = if ($cpus -gt 0) { "$tierMiB MB / --cpus $($cpus.ToString($Inv))" } else { "$tierMiB MB / sem throttle" }
    Write-Host ""
    Write-Host "================================================================"
    Write-Host "COMBINACAO: $label"
    Write-Host "================================================================"

    $combo = [ordered]@{
        Tier = $tierMiB; Cpus = $cpus; Label = $label; Status = "OK"
        ColdStarts = @()
        WarmupDiscards = 0; WarmupCapped = $false; WarmupWindows = @()
        Rounds = @(); Throttle = $null; RamAfterRun = $null
    }

    # Cold start medido 3 vezes (3 docker run novos). A carga roda no container do terceiro arranque.
    for ($i = 1; $i -le 3; $i++) {
        $s = Start-Measured $tierMiB $cpus
        if ($s.Status -ne "OK") { $combo.Status = "$($s.Status)_NO_ARRANQUE_$i"; return [PSCustomObject]$combo }
        $combo.ColdStarts += [PSCustomObject]@{ Run = $i; HealthMs = $s.HealthMs; FirstRecMs = $s.FirstRecMs }
        Write-Host "  Cold start $i/3: /health em $($s.HealthMs) ms; primeira recomendacao 200 em $($s.FirstRecMs) ms"
        if ($i -lt 3) { Stop-MeasureContainer }
    }

    # Aquecimento: janelas de 20 requisições (as mesmas 20 primeiras sementes) descartadas até a média de
    # duas janelas consecutivas diferir menos de 10%; piso de 50 e teto de 200 descartes.
    $windows = [System.Collections.Generic.List[object]]::new()
    $discards = 0
    $prev = $null
    while ($true) {
        $w = Invoke-Load $WarmWindow
        $discards += $WarmWindow
        $valid = ($w.Non200 -eq 0 -and $w.Done -eq $WarmWindow -and $w.Mean -gt 0)
        $delta = if ($valid -and $null -ne $prev) { [Math]::Abs($w.Mean - $prev) / $prev } else { [double]::PositiveInfinity }
        $windows.Add([PSCustomObject]@{ Discards = $discards; Mean = $w.Mean; P50 = $w.P50; Non200 = $w.Non200; DeltaFromPrev = $(if ([double]::IsInfinity($delta)) { $null } else { [Math]::Round($delta, 3) }) })
        Write-Host "    aquecimento $discards descartes: media=$($w.Mean) ms p50=$($w.P50) ms (variacao da media vs janela anterior: $(if ([double]::IsInfinity($delta)) { 'n/d' } else { [Math]::Round($delta, 3) }))"
        $prev = if ($valid) { $w.Mean } else { $null }
        if ($discards -ge 50 -and $delta -lt 0.10) { break }
        if ($discards -ge 200) { $combo.WarmupCapped = $true; break }
    }
    $combo.WarmupDiscards = $discards
    $combo.WarmupWindows  = $windows.ToArray()

    $status = docker inspect $ContainerName --format "{{.State.Status}}"
    if ($status -ne "running") { $combo.Status = "MORTO_NO_AQUECIMENTO (OOMKilled=$(docker inspect $ContainerName --format '{{.State.OOMKilled}}'))"; Stop-MeasureContainer; return [PSCustomObject]$combo }

    # Rodadas medidas, com o delta do cpu.stat como evidência de throttle.
    $cpuBefore = Get-CpuStat
    for ($r = 1; $r -le $Rounds; $r++) {
        $res = Invoke-Load $SeedCount
        $res | Add-Member -NotePropertyName Round -NotePropertyValue $r
        $combo.Rounds += $res
        Write-Host "    rodada ${r}/${Rounds}: media=$($res.Mean) ms  p50=$($res.P50) ms  p95=$($res.P95) ms  p99=$($res.P99) ms  nao200=$($res.Non200)  feitas=$($res.Done)/$($res.Requests)"
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

$Metrics = @('Mean', 'P50', 'P95', 'P99')

function Get-Series($combo, [string]$metric) { return [double[]]@($combo.Rounds | ForEach-Object { $_.$metric }) }

function Get-ColdSeries($combo, [string]$field) { return [double[]]@($combo.ColdStarts | ForEach-Object { $_.$field }) }

function Test-Validity($combos) {
    $checks = [System.Collections.Generic.List[object]]::new()
    # Uma combinação é completa com 3 cold starts e todas as rodadas; V3 cobra o resto (nao200, requisições).
    $complete = @($combos | Where-Object { $_.Status -eq "OK" -and $_.ColdStarts.Count -eq 3 -and $_.Rounds.Count -eq $Rounds })
    $thrCpu  = @($CpuLimits | Where-Object { $_ -gt 0 })
    $freeCpu = @($CpuLimits | Where-Object { $_ -eq 0 })

    foreach ($tier in $Tiers) {
        $thr  = if ($thrCpu.Count -eq 1)  { $complete | Where-Object { $_.Tier -eq $tier -and $_.Cpus -eq $thrCpu[0] } }  else { $null }
        $free = if ($freeCpu.Count -eq 1) { $complete | Where-Object { $_.Tier -eq $tier -and $_.Cpus -eq $freeCpu[0] } } else { $null }
        if ($null -eq $thr -or $null -eq $free) {
            foreach ($k in 'V1', 'V2') { $checks.Add([PSCustomObject]@{ Check = $k; Cpus = "-"; Metric = "Mean"; Pass = $false; Detail = "$tier MB: par {com throttle, sem throttle} incompleto, nao avaliavel" }) }
            continue
        }
        $sThr = Get-Series $thr 'Mean'; $sFree = Get-Series $free 'Mean'
        $medThr = Get-Median $sThr; $medFree = Get-Median $sFree
        $diff = $medThr - $medFree
        $ampThr = Get-Amplitude $sThr; $ampFree = Get-Amplitude $sFree
        $amp = [Math]::Max($ampThr, $ampFree)
        $checks.Add([PSCustomObject]@{ Check = "V1"; Cpus = "-"; Metric = "Mean"; Pass = ($medThr -gt $medFree)
            Detail = "$tier MB: mediana da media com --cpus $($thrCpu[0].ToString($Inv)) = $([Math]::Round($medThr,2)) ms; sem throttle = $([Math]::Round($medFree,2)) ms" })
        $checks.Add([PSCustomObject]@{ Check = "V2"; Cpus = "-"; Metric = "Mean"; Pass = ($diff -gt $amp)
            Detail = "$tier MB: diferenca entre medianas da media = $([Math]::Round($diff,2)) ms; maior amplitude entre rodadas = $([Math]::Round($amp,2)) ms (throttle $([Math]::Round($ampThr,2)), livre $([Math]::Round($ampFree,2)))" })
    }

    $expected = $Tiers.Count * $CpuLimits.Count
    $non200 = 0; $short = 0
    foreach ($c in $complete) { foreach ($rd in $c.Rounds) { $non200 += $rd.Non200; if ($rd.Done -ne $rd.Requests) { $short++ } } }
    $v3ok = ($complete.Count -eq $expected) -and ($non200 -eq 0) -and ($short -eq 0)
    $checks.Add([PSCustomObject]@{ Check = "V3"; Cpus = "todas"; Metric = "-"; Pass = $v3ok
        Detail = "$($complete.Count) de $expected combinacoes completas (3 cold starts e $Rounds rodadas); respostas diferentes de 200 nas rodadas = $non200; rodadas com requisicoes faltando = $short" })

    foreach ($c in $complete) {
        if ($c.Cpus -gt 0) {
            $wanted = [long][Math]::Round($c.Cpus * 1e9)
            $pass = ($c.Throttle.NanoCpus -eq $wanted) -and ($c.Throttle.NrThrottledDelta -gt 0)
            $detail = "$($c.Tier) MB: NanoCpus=$($c.Throttle.NanoCpus) (pedido $wanted), nr_throttled+=$($c.Throttle.NrThrottledDelta) de $($c.Throttle.NrPeriodsDelta) periodos"
        } else {
            $pass = ($c.Throttle.NanoCpus -eq 0)
            $detail = "$($c.Tier) MB: NanoCpus=$($c.Throttle.NanoCpus) (esperado 0, sem throttle)"
        }
        $checks.Add([PSCustomObject]@{ Check = "THROTTLE"; Cpus = $c.Cpus; Metric = "-"; Pass = $pass; Detail = $detail })
    }
    return $checks.ToArray()
}

# Indicadores reportados, nao bloqueantes: neutralidade de RAM (por configuração de CPU) e rodada 1 lenta.
function Get-Indicators($combos) {
    $out = [System.Collections.Generic.List[object]]::new()
    $complete = @($combos | Where-Object { $_.Status -eq "OK" -and $_.Rounds.Count -eq $Rounds })
    $lowTier  = ($Tiers | Measure-Object -Minimum).Minimum
    $highTier = ($Tiers | Measure-Object -Maximum).Maximum
    foreach ($cpus in $CpuLimits) {
        $lo = $complete | Where-Object { $_.Tier -eq $lowTier  -and $_.Cpus -eq $cpus }
        $hi = $complete | Where-Object { $_.Tier -eq $highTier -and $_.Cpus -eq $cpus }
        if ($null -eq $lo -or $null -eq $hi -or $lowTier -eq $highTier) { continue }
        $sLo = Get-Series $lo 'Mean'; $sHi = Get-Series $hi 'Mean'
        $diff = (Get-Median $sLo) - (Get-Median $sHi)
        $amp  = [Math]::Max((Get-Amplitude $sLo), (Get-Amplitude $sHi))
        $out.Add([PSCustomObject]@{ Kind = "RAM"; Cpus = $cpus; InNoise = ([Math]::Abs($diff) -le $amp)
            Detail = "cpus=${cpus}: mediana da media $lowTier MB menos $highTier MB = $([Math]::Round($diff,2)) ms; maior amplitude = $([Math]::Round($amp,2)) ms: $(if ([Math]::Abs($diff) -le $amp) { 'dentro do ruido' } else { 'fora do ruido' })" })
    }
    foreach ($c in $complete) {
        if ($Rounds -lt 3) { continue }
        $s = Get-Series $c 'Mean'
        $rest = [double[]]$s[1..($s.Count - 1)]
        $excess = $s[0] - ($rest | Measure-Object -Maximum).Maximum
        $slow = $excess -gt (Get-Amplitude $rest)
        $out.Add([PSCustomObject]@{ Kind = "RODADA1"; Cpus = $c.Cpus; Slow = $slow
            Detail = "$($c.Label): media da rodada 1 = $($s[0]) ms; maior das demais = $([Math]::Round(($rest | Measure-Object -Maximum).Maximum,2)) ms; amplitude das demais = $([Math]::Round((Get-Amplitude $rest),2)) ms: $(if ($slow) { 'AVISO, rodada 1 mais lenta' } else { 'sem aviso' })" })
    }
    return $out.ToArray()
}

# ---------------------------------------------------------------------------
# 7. Execução
# ---------------------------------------------------------------------------

docker pull $LoadImage | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "ERRO: nao foi possivel obter a imagem de carga '$LoadImage'."; exit 1 }
$LoadImageDigest = (docker image inspect $LoadImage --format '{{index .RepoDigests 0}}' | Out-String).Trim()
$LoadToolVersion = (docker run --rm $LoadImage version | Out-String).Trim()

Write-Host ""
Write-Host "Containers em execucao antes da medicao (qualquer um que consuma CPU contamina a medicao):"
$Running = @(docker ps --format "  {{.Names}}  {{.Image}}  {{.Status}}")
if ($Running.Count -eq 0) { Write-Host "  (nenhum)" } else { $Running | ForEach-Object { Write-Host $_ } }

$RunStart = Get-Date
$Combos = [System.Collections.Generic.List[object]]::new()
try {
    Stop-MeasureContainer
    Remove-LoadContainer
    Remove-MeasureNetwork
    docker network create $NetworkName | Out-Null
    Test-Seeds
    foreach ($cpus in $CpuLimits) {
        foreach ($tier in $Tiers) {
            $Combos.Add((Measure-Combination $tier $cpus))
        }
    }
}
finally {
    Stop-MeasureContainer
    Remove-LoadContainer
    Remove-MeasureNetwork
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
$ElapsedMin = [Math]::Round(((Get-Date) - $RunStart).TotalMinutes, 1)

$Checks = Test-Validity $Combos.ToArray()
$Valid  = -not (@($Checks | Where-Object { -not $_.Pass }).Count)
$Indicators = Get-Indicators $Combos.ToArray()

# ---------------------------------------------------------------------------
# 8. Relatório e saída estruturada
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "================================================================"
Write-Host "RESULTADO FINAL"
Write-Host "================================================================"
Write-Host "Commit: $Commit (src limpo: $SrcClean)  Imagem: $ImageTag  Faixas no banco: $TrackCount"
Write-Host "Carga: $LoadToolVersion ($LoadImageDigest), 1 VU, $SeedCount sementes por rodada (uma passada, mesma ordem), $Rounds rodadas"
Write-Host "Docker VM: $DockerVm"
Write-Host "Duracao total: $ElapsedMin min"
Write-Host ""
Write-Host "--- Cold start (mediana e faixa de 3 arranques) e aquecimento ---"
foreach ($c in $Combos) {
    if ($c.ColdStarts.Count -eq 0) { Write-Host ("  {0,-26} status={1}" -f $c.Label, $c.Status); continue }
    $h = Get-ColdSeries $c 'HealthMs'; $f = Get-ColdSeries $c 'FirstRecMs'
    Write-Host ("  {0,-26} status={1}  /health: mediana={2} ms faixa={3}-{4}  1a-recomendacao: mediana={5} ms faixa={6}-{7}  descartes={8}{9}" -f $c.Label, $c.Status,
        (Get-Median $h), ($h | Measure-Object -Minimum).Minimum, ($h | Measure-Object -Maximum).Maximum,
        (Get-Median $f), ($f | Measure-Object -Minimum).Minimum, ($f | Measure-Object -Maximum).Maximum,
        $c.WarmupDiscards, $(if ($c.WarmupCapped) { " (TETO ATINGIDO)" } else { "" }))
}
Write-Host ""
Write-Host "--- Latencia por rodada (ms), mediana e amplitude entre rodadas ---"
foreach ($c in $Combos) {
    if ($c.Rounds.Count -eq 0) { continue }
    Write-Host "  $($c.Label)"
    foreach ($rd in $c.Rounds) { Write-Host ("    R{0}: media={1,8} p50={2,8} p95={3,8} p99={4,8}  nao200={5}  feitas={6}/{7}" -f $rd.Round, $rd.Mean, $rd.P50, $rd.P95, $rd.P99, $rd.Non200, $rd.Done, $rd.Requests) }
    $line = foreach ($m in $Metrics) { $s = Get-Series $c $m; "{0} mediana={1} amplitude={2}" -f $m, [Math]::Round((Get-Median $s), 2), [Math]::Round((Get-Amplitude $s), 2) }
    Write-Host "    $($line -join ' | ')"
    Write-Host "    throttle: NanoCpus=$($c.Throttle.NanoCpus) nr_throttled+=$($c.Throttle.NrThrottledDelta)/$($c.Throttle.NrPeriodsDelta) throttled_usec+=$($c.Throttle.ThrottledUsecDelta)  RAM=$($c.RamAfterRun)"
}
Write-Host ""
Write-Host "--- Validade bloqueante ---"
foreach ($k in $Checks) {
    Write-Host ("  [{0}] {1} cpus={2} {3}: {4}" -f $(if ($k.Pass) { "OK  " } else { "FALHA" }), $k.Check, $k.Cpus, $k.Metric, $k.Detail)
}
Write-Host ""
Write-Host "--- Indicadores nao bloqueantes ---"
foreach ($i in $Indicators) { Write-Host "  [$($i.Kind)] $($i.Detail)" }

if ([string]::IsNullOrEmpty($OutFile)) { $OutFile = Join-Path ([IO.Path]::GetTempPath()) "measure-latency-$Commit.json" }
[PSCustomObject]@{
    Commit = $Commit; SrcClean = $SrcClean; Image = $ImageTag; TrackCount = $TrackCount; LoadImage = $LoadImage
    LoadImageDigest = $LoadImageDigest; LoadToolVersion = $LoadToolVersion
    SeedCount = $SeedCount; Seeds = $Seeds; Rounds = $Rounds; DockerVm = $DockerVm
    ElapsedMinutes = $ElapsedMin; Valid = $Valid; Combinations = $Combos.ToArray(); Validity = $Checks; Indicators = $Indicators
} | ConvertTo-Json -Depth 8 | Set-Content -Path $OutFile -Encoding UTF8
Write-Host ""
Write-Host "Saida estruturada: $OutFile"

$Capped = @($Combos | Where-Object { $_.WarmupCapped } | ForEach-Object { $_.Label })
if ($Capped.Count -gt 0) {
    Write-Host ""
    Write-Host "AVISO: aquecimento bateu no teto de 200 descartes SEM estabilizar em: $($Capped -join '; '). Os numeros dessas combinacoes nao sao de regime estavel." -ForegroundColor Yellow
}

if (-not $Valid) {
    Write-Host ""
    Write-Host "MEDICAO INVALIDA: ao menos uma validade (THROTTLE, V1, V2, V3) falhou. NAO publique estes numeros." -ForegroundColor Red
    exit 2
}
Write-Host ""
Write-Host "MEDICAO VALIDA: THROTTLE, V1, V2 e V3 passaram em todas as combinacoes$(if ($Capped.Count -gt 0) { ' (COM AVISO DE AQUECIMENTO ACIMA)' })." -ForegroundColor Green
