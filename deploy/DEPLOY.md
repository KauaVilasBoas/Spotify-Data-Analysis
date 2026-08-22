# Deploy da demo em free tier

Runbook do E6.2. Três recursos independentes: **API .NET 8** (container), **PostgreSQL gerenciado** e
**SPA estática**. A SPA NÃO é servida pelo Host .NET; ela vive num host estático próprio e fala com a API
por HTTP, com CORS como único mecanismo de ligação.

## Números medidos (E6.2, 17/08/2026)

Container `spotifydataanalysis-api:e62` apontando para o Postgres com o dataset completo
(89.740 faixas, 37.121 playlists, 1.481.511 pares de co-ocorrência).

| Cenário | Pico de RAM | Boot até `/health` | 1ª recomendação | p95 content | p95 blend | OOM |
| --- | --- | --- | --- | --- | --- | --- |
| 512 MB, CPU livre | 310,1 MiB | 9,5 s | 6,9 s | 715 ms | 204 ms | não |
| 256 MB, CPU livre | 217,3 MiB | 11,1 s | 16,2 s | 402 ms | 166 ms | não |
| 512 MB, 0,1 vCPU | 142,9 MiB | 51,1 s | 32,4 s | 1.915 ms | 2.479 ms | não |
| 256 MB, 0,1 vCPU | 138,4 MiB | 31,9 s | 25,5 s | 2.614 ms | 2.337 ms | não |

Disco do banco: **388 MB** no total, dos quais `prediction.track_cooccurrence` = 254 MB (65%).

O gargalo do free tier neste projeto é **CPU e disco**, não RAM.

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
(51 s de boot + 32 s de montagem do índice de similaridade). O índice é montado sob demanda, na
primeira chamada de `/api/recommendations`, e custa 25 s nessa CPU.

Duas mitigações conhecidas, ambas fora do escopo do E6.2:

- ping periódico (a cada 10 min) para o serviço não hibernar;
- montar o índice em background no arranque, para que o boot já entregue o serviço quente.
