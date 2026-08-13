# SpotifyDataAnalysis

Monólito modular em **.NET 8** — DDD + CQRS + Clean Architecture, mesma arquitetura de referência do SISLAB, porém **single-tenant / uso próprio** (sem isolamento por tenant na fundação).

## Arquitetura

- **Monólito modular**: cada bounded context é um módulo isolado (`Domain · Application · Infrastructure · Contracts`).
- **CQRS com mediator próprio** (`IRequestHandler<TRequest,TResult>` / `IMediator`), pipeline de behaviors: **Validation → Logging → Transaction**.
- **Write-side EF Core + PostgreSQL** (snake_case, `DbContext` por módulo herdando `SpotifyDbContextBase`); **read-side Dapper** (`BaseDataAccess`, paginação `ROW_NUMBER() + COUNT(*) OVER()`).
- **Eventos híbridos**: domain event transacional (mesma transação) **ou** Outbox/eventual (`OutboxMessage` → `OutboxDispatcher` → `IEventBus`).
- **Isolamento de módulos** validado por **ArchUnitNET** (`tests/SpotifyDataAnalysis.ArchitectureTests`).
- **Host** (`SpotifyDataAnalysis.Api`) é o composition root; **Jobs** (`SpotifyDataAnalysis.Jobs`) roda in-process.

## Estrutura da solução

```
src/Host/SpotifyDataAnalysis.Api            Web API · composition root · middleware
src/Modules/<Contexto>/                     Domain · Application · Infrastructure · Contracts  (a criar com as specs)
src/Shared/SpotifyDataAnalysis.SharedKernel Entity/AggregateRoot/ValueObject, mediator (IRequest/IMediator),
                                            ICommand/IQuery, IEventBus, exceptions, Guard, IClock, PagedQuery/PagedResult
src/Shared/SpotifyDataAnalysis.Infrastructure Mediator, behaviors, Outbox, EfUnitOfWork, SpotifyDbContextBase (snake_case),
                                            BaseDataAccess (Dapper), IModule/ModuleLoader, DI
src/Jobs/SpotifyDataAnalysis.Jobs           worker in-process (Outbox + coleta agendada de playlists)
tests/SpotifyDataAnalysis.ArchitectureTests testes de arquitetura (ArchUnitNET)
tests/SpotifyDataAnalysis.SharedKernel.Tests testes unitários do SharedKernel
tests/SpotifyDataAnalysis.Modules.Catalog.Tests testes do módulo Catalog (domínio + ingestão)
tests/SpotifyDataAnalysis.Jobs.Tests        testes dos jobs agendados
```

## Rodando

```bash
dotnet build
dotnet test
dotnet run --project src/Host/SpotifyDataAnalysis.Api   # Swagger em /swagger, health em /health
```

## Configuração de credenciais (User Secrets / env — nunca no repositório)

Segredos ficam em **User Secrets** (dev) ou **variáveis de ambiente** — nunca versionados (o `.gitignore` cobre isso).

**PostgreSQL** (write-side EF / read-side Dapper):

```bash
dotnet user-secrets set "ConnectionStrings:SpotifyDb" "Host=localhost;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=SUA_SENHA" --project src/Host/SpotifyDataAnalysis.Api
```
(ou via env `ConnectionStrings__SpotifyDb`)

**Credenciais do Spotify** (fluxo Client Credentials — módulo Catalog):

1. Crie um app em <https://developer.spotify.com/dashboard> e copie o **Client ID** e o **Client Secret**.
2. Configure-os em User Secrets:

```bash
dotnet user-secrets set "Spotify:ClientId" "SEU_CLIENT_ID" --project src/Host/SpotifyDataAnalysis.Api
dotnet user-secrets set "Spotify:ClientSecret" "SEU_CLIENT_SECRET" --project src/Host/SpotifyDataAnalysis.Api
```
(ou via env `Spotify__ClientId` / `Spotify__ClientSecret`)

As URLs (`Spotify:BaseUrl`, `Spotify:AuthUrl`) já têm defaults públicos em `appsettings.json`; só as credenciais são segredo.

## Ingestão (E1)

**Coleta a partir de playlists-semente.** O `IngestPlaylistCommand` percorre a playlist na API do Spotify e
registra faixas, artistas e álbuns no catálogo. É **idempotente**: rodar de novo atualiza o que já existe
(popularidade varia com o tempo) em vez de duplicar.

Para agendar a coleta, habilite o job e informe as playlists-semente (desligado por default — ele chama a
API externa):

```jsonc
// appsettings.Development.json (ou env Jobs__PlaylistIngestion__Enabled=true)
"Jobs": {
  "PlaylistIngestion": {
    "Enabled": true,
    "Interval": "06:00:00",
    "SeedPlaylistIds": [ "37i9dQZF1DXcBWIGoYBM5M" ]
  }
}
```

**Audio-features (dataset Kaggle).** A API do Spotify não expõe mais os atributos de áudio (nov/2024), então
eles vêm do dataset *"Spotify Tracks Dataset"* (maharshipandya) via `ImportKaggleAudioFeaturesCommand`.
O CSV **não é versionado** — baixe-o de
<https://www.kaggle.com/datasets/maharshipandya/-spotify-tracks-dataset> e aponte o caminho no command.

O importador casa cada linha com o catálogo em duas estratégias encadeadas — `track_id` (exato) e, como
fallback, "artista + título" normalizados — e devolve a **métrica de qualidade** da importação: taxa de
match, fração resolvida pelo fallback, duplicatas e taxa de imputação.

**Faltantes.** Atributos ausentes são preenchidos pela **mediana do gênero** (com mediana global de
retaguarda) e a faixa fica marcada com `IsImputed` — nada é preenchido em silêncio.

**Playlists para co-ocorrência (dataset Pichl, E4.5).** O recomendador colaborativo (E4.6) precisa de sinal de
co-ocorrência item-item, que o *"Spotify Tracks Dataset"* não tem (é uma linha por faixa, sem playlists). Esse
sinal vem do dataset *"Spotify Playlists"* (Pichl et al.) — <https://www.kaggle.com/datasets/andrewmvd/spotify-playlists>
(~1,2 GB, colunas `user_id, artistname, trackname, playlistname`). O CSV **não é versionado** — baixe-o
manualmente e aponte o caminho no seeder.

O seeder lê o Pichl em streaming, agrupa por `user_id`+`playlistname`, e casa cada faixa ao catálogo por chave
`"artista|título"` normalizada (`TrackMatchKey`) reconstruída do `dataset.csv` (o único lugar com o nome do
artista). Popula `catalog.playlists` via `DbContext` cru em lotes — **sem Outbox** (é carga de dados, não
ingestão de negócio). É idempotente: cada playlist recebe um id determinístico de `user_id`+`playlistname`, e
reexecutar pula as já semeadas.

```bash
# argumentos: [csvPichl] [datasetCsv] [maxPlaylists]  (todos opcionais; teto default 50000 playlists novas/execução)
dotnet run --project src/Host/SpotifyDataAnalysis.Api -- seed-playlists spotify-playlists/spotify_dataset.csv dataset.csv 50000
```

O comando devolve o **censo** que alimenta os gates do E4.6: taxa de casamento (linhas do Pichl que casaram) e,
via consulta a `catalog.playlists`, a densidade de co-ocorrência (pares de faixas-do-catálogo em ≥2 playlists).

## Migrations

```bash
$env:ConnectionStrings__SpotifyDb = "Host=localhost;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=SUA_SENHA"
dotnet ef database update --project src/Modules/Catalog/SpotifyDataAnalysis.Modules.Catalog.Infrastructure
```

## Fluxo de trabalho (P.O → Trello → Dev)

- **`@spotify-po`** — refina specs em cards de Trello (objetivo, escopo, critérios de aceite, riscos, decisões pendentes).
- **`@spotify-dev`** — implementa os cards respeitando a arquitetura; **Definition of Done**: `dotnet build` 0/0 e `dotnet test` verde (incluindo ArchitectureTests).

> **Estado atual:** fundação pronta + épicos **E0 (Integração Spotify)** e **E1 (Ingestão & Catálogo)** concluídos — módulo **Catalog** com cliente da Web API (auth Client Credentials com cache/refresh, resiliência retry/429, paginação), agregados Track/AudioFeatures/Artist/Album/Playlist persistidos, ingestão idempotente com Outbox (`TrackIngested`, `PlaylistIngested`), importador Kaggle com fallback de match e imputação de faltantes, e job de coleta agendada. Próximo: **E2 — Analytics / EDA**.
