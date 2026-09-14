# Deploy da demo em free tier

Runbook do E6.2. Três recursos independentes: **API .NET 8** (container), **PostgreSQL gerenciado** e
**SPA estática**. A SPA NÃO é servida pelo Host .NET; ela vive num host estático próprio e fala com a API
por HTTP, com CORS como único mecanismo de ligação.

## Números medidos (E6.9, 13/09/2026)

Container `spotifydataanalysis-api:e5ab833` (HEAD, over-fetch adaptativo, E4.9) apontando para o
Postgres local com o dataset completo (89.740 faixas, 37.121 playlists, 1.481.511 pares de
co-ocorrência). CPU livre (sem throttle). Script: `deploy/measure-latency.ps1`.

**Metodologia:** 300 sementes determinísticas (semente textual `e4.4-proxies-v1`, query
`md5(semente || id)`, reprodutível via script), requisições sequenciais (1 cliente), 3 rodadas,
1.800 requisições totais por tier (300 sementes × 3 rodadas × 2 estratégias). RAM lida de
`docker stats --no-stream` após o warm-up e após a última rodada. `docker stats` devolve uso
**corrente**, não pico histórico: os valores abaixo são "uso após a rodada", não pico.

| Cenário | RAM pós-warmup | RAM pós-medição | OOM |
| --- | --- | --- | --- |
| 512 MB, CPU livre | 161,6 MiB | 310,9 MiB | não |
| 256 MB, CPU livre | 140,3 MiB | 148,3 MiB | não |

Disco do banco: **388 MB** no total, dos quais `prediction.track_cooccurrence` = 254 MB (65%).

**O que esta medição prova:** o índice de similaridade cabe em 256 MB e o app serve 1.800 requisições
sequenciais nesse tier sem OOM.

**O que esta medição NÃO prova:** latência e cold start. Veja as limitações conhecidas abaixo.

### Limitações conhecidas da metodologia atual

Os dados de latência do script (`p50`, `p95`, média por rodada) não são publicáveis por três razões:

1. **Aquecimento de uma única requisição.** O `Wait-Warmup` do script aguarda `/health` 200 seguido
   de ao menos uma recomendação 200 (`measure-latency.ps1`, linhas 245-256). Uma única requisição não
   é suficiente para o JIT do .NET chegar a regime. A rodada 1 é sistematicamente mais lenta que as
   seguintes nos dois tiers (content 512 MB: 104,0 ms / 65,7 ms / 83,1 ms; content 256 MB:
   83,0 ms / 55,6 ms / 52,2 ms), e o desvio entre rodadas (±19,2 ms e ±16,9 ms) supera qualquer
   diferença entre tiers, produzindo o resultado não-físico de 256 MB mais rápido que 512 MB.

2. **Cronômetro do warm-up a partir da referência errada.** O `Wait-Warmup` inicia o cronômetro
   antes de saber se o container está de pé (`measure-latency.ps1`, linha 224) e trunca para inteiro.
   O valor registrado de "1 s" é inconsistente com os ~9,5 s de boot que a tabela anterior registrava
   para a mesma imagem; um app .NET não sobe em 1 s, e o número mede a diferença entre o `docker run`
   e o primeiro `Invoke-WebRequest` bem-sucedido, não o cold start real.

3. **Sem limite de CPU (`--cpus` ausente).** O script sobe o container apenas com `-m` e
   `--memory-swap` (`measure-latency.ps1`, linhas 341-343), sem throttle de CPU. O gargalo real do
   free tier é CPU, e a linha de 0,1 vCPU da medição anterior mostrava p95 acima de 1.900 ms.
   Latência medida em CPU livre não representa o cenário de produção.

**Os números do E6.2 (17/08/2026) foram substituídos.** Aquela medição não tinha metodologia
registrada nem script; a comparação direta seria inválida porque o código e a metodologia mudaram ao
mesmo tempo.

O gargalo do free tier neste projeto é **CPU e disco**, não RAM: o working set cabe folgado em 256 MB.

## Requisitos do plano

- Host da API: container Linux, 256 MB de RAM ou mais, porta lida de `PORT` ou fixada em 8080.
- Postgres: **500 MB de storage é o mínimo absoluto** (banco atual = 388 MB, 78% de ocupação, sem folga
  para WAL e bloat). Com 1 GB ou mais o dataset entra inteiro sem recorte.
- Host estático: qualquer um com build de SPA e fallback de rota para `index.html`.

## Variáveis de ambiente da API

Nenhum segredo vai para o repositório. Tudo por variável de ambiente do host.

| Variável | Valor |
| --- | --- |
| `ConnectionStrings__SpotifyDb` | `Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<senha>;SSL Mode=Require;Trust Server Certificate=true` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://+:8080` (ou `http://+:$PORT` se o provedor injetar `PORT`) |
| `Cors__AllowedOrigins__0` | URL pública da SPA, sem barra final |
| `Jobs__OutboxDispatcher__Interval` | `00:00:30` |

`Cors:AllowedOrigins` vazio faz a política cair em `AllowAnyOrigin` sem credenciais. Preenchido, passa a
`WithOrigins(...).AllowCredentials()`. A origem precisa bater **exatamente** com o que o navegador manda:
`https://app.exemplo.com`, sem path e sem barra final.

## Variável de ambiente da SPA

| Variável | Valor |
| --- | --- |
| `VITE_API_BASE_URL` | URL pública da API, ex.: `https://spotify-api.exemplo.com` |

`VITE_*` é resolvida **em tempo de build**, não em runtime: mudar a URL da API exige um novo build da SPA.

### O par que só fecha um contra o outro

`VITE_API_BASE_URL` e `Cors__AllowedOrigins__0` são as duas pontas do mesmo cabo:

1. publique a API primeiro e anote a URL dela;
2. publique a SPA com `VITE_API_BASE_URL` = URL da API;
3. volte na API e ponha `Cors__AllowedOrigins__0` = URL da SPA;
4. redeploy da API para a variável valer.

Pular o passo 3 é o erro clássico: a SPA carrega, faz a chamada, e o navegador bloqueia a resposta com
erro de CORS. O sintoma no front é falha de rede genérica, não um 4xx, porque o navegador nem entrega a
resposta ao JavaScript.

## Carga dos dados no ambiente publicado

`dataset.csv` (20 MB) e o dataset Pichl (1,18 GB) **não são versionados**. O caminho reprodutível não
depende de reexecutar a ingestão no host publicado, que não tem os CSVs nem CPU para isso.

**Caminho recomendado: dump e restore.**

```
pg_dump --format=custom --no-owner --no-privileges \
  --host=localhost --username=postgres spotify_data_analysis > spotify.dump

pg_restore --no-owner --no-privileges --clean --if-exists \
  --host=<host-gerenciado> --username=<user> --dbname=<db> spotify.dump
```

O dump comprimido do banco atual gira em torno de 120 MB. As migrations do EF Core já vêm dentro do
dump (as tabelas `__ef_migrations_history` de cada schema), então o restore deixa o banco no mesmo
estado que a API espera.

**Conferência obrigatória depois do restore**, contra `GET /api/insights/summary`:
`totalTracks` 89.740 · `distinctArtists` 29.811 · `distinctAlbums` 57.637 · `distinctGenres` 113.
Divergência aqui é carga que falhou em silêncio.

**Se o plano de Postgres for de 500 MB**, o recorte medido que cabe sem tocar no catálogo é podar a
cauda da co-ocorrência:

| Recorte | Pares | Tabela | Banco |
| --- | --- | --- | --- |
| nenhum | 1.481.511 | 254 MB | 388 MB |
| `co_playlists >= 3` | 757.589 | ~130 MB | ~264 MB |
| top-50 vizinhos por faixa | ~500.000 | ~86 MB | ~220 MB |

O catálogo de 89.740 faixas fica intacto nos três casos.

## Build local do container

```
docker build -t spotifydataanalysis-api .
docker run --rm -m 512m --memory-swap 512m -p 8080:8080 \
  -e ConnectionStrings__SpotifyDb="Host=host.docker.internal;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=<senha>" \
  spotifydataanalysis-api
```

Imagem resultante: 349 MB.

## Cold start

Com spin-down ligado e 0,1 vCPU, a primeira recomendação depois da hibernação levou **83 s**
(51 s de boot + 32 s de montagem do índice de similaridade). O índice é montado em background no
arranque (commit 32262b6): durante a montagem o endpoint devolve 503 com `Retry-After: 5`; a
primeira chamada após o warm-up já encontra o serviço pronto. O tempo de warm-up em CPU livre não
foi re-medido com metodologia confiável (ver limitações acima).

Duas mitigações conhecidas, ambas fora do escopo do E6.2:

- ping periódico (a cada 10 min) para o serviço não hibernar;
- montar o índice em background no arranque, para que o boot já entregue o serviço quente.
