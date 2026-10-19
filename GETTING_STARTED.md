<h1 align="center">Getting started</h1>

<p align="center">
  <i>From a clean clone to the full app on your machine: configure, migrate, load the two public<br/>
  datasets, run the API and the SPA, train the model, and run every quality gate.</i>
</p>

<p align="center">
  <a href="README.md">README</a> &nbsp;·&nbsp;
  <a href="ENGINEERING.md">Engineering deep dive</a> &nbsp;·&nbsp;
  <a href="frontend/README.md">SPA notes (PT-BR)</a> &nbsp;·&nbsp;
  <a href="deploy/DEPLOY.md">Deploy runbook (PT-BR)</a>
</p>

---

## Contents

- [Prerequisites](#prerequisites)
- [1. Configure](#1-configure)
- [2. Migrate](#2-migrate)
- [3. Load the data](#3-load-the-data)
- [4. Run](#4-run)
- [5. Train and query](#5-train-and-query)
- [Or just Docker](#or-just-docker)
- [Tests and quality gates](#tests-and-quality-gates)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

- .NET 8 SDK
- PostgreSQL 15 or newer
- Node 22 or newer, for the SPA
- The EF Core CLI, for the migrations: `dotnet tool install --global dotnet-ef --version 8.*`
- Docker, only for the container path

## 1. Configure

Secrets live in User Secrets for development, or in environment variables, and never in the
repository.

```powershell
dotnet user-secrets set "ConnectionStrings:SpotifyDb" `
  "Host=localhost;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=<password>" `
  --project src/Host/SpotifyDataAnalysis.Api
```

Spotify credentials are **optional**. Only the live-ingestion path needs them, and the whole catalogue
seeds from CSV without an API key:

```powershell
dotnet user-secrets set "Spotify:ClientId"     "<id>"     --project src/Host/SpotifyDataAnalysis.Api
dotnet user-secrets set "Spotify:ClientSecret" "<secret>" --project src/Host/SpotifyDataAnalysis.Api
```

## 2. Migrate

The design-time factories read the connection string from the environment, so set it for the shell
first. Analytics has no schema of its own (it reads `catalog.*`), so there are only two migrations.

```powershell
$env:ConnectionStrings__SpotifyDb = "Host=localhost;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=<password>"
dotnet ef database update --project src/Modules/Catalog/SpotifyDataAnalysis.Modules.Catalog.Infrastructure
dotnet ef database update --project src/Modules/Prediction/SpotifyDataAnalysis.Modules.Prediction.Infrastructure
```

## 3. Load the data

Neither dataset is versioned here. Download them and point the CLI at the files.

| Dataset | Used for | Source |
|---|---|---|
| Spotify Tracks Dataset (maharshipandya) | catalogue, audio features, genre | [Kaggle](https://www.kaggle.com/datasets/maharshipandya/-spotify-tracks-dataset) |
| Spotify Playlists (Pichl et al., about 1.18 GB) | item-item co-occurrence | [Kaggle](https://www.kaggle.com/datasets/andrewmvd/spotify-playlists) |

```powershell
dotnet run --project src/Host/SpotifyDataAnalysis.Api -- seed-catalog     dataset.csv
dotnet run --project src/Host/SpotifyDataAnalysis.Api -- seed-references  dataset.csv
dotnet run --project src/Host/SpotifyDataAnalysis.Api -- seed-playlists   spotify-playlists/spotify_dataset.csv dataset.csv 50000
dotnet run --project src/Host/SpotifyDataAnalysis.Api -- build-cooccurrence
```

Each step prints its own census: rows read, rows matched, match rate, duplicates, keys discarded as
ambiguous. **Verify before moving on**, against `GET /api/insights/summary`:

```
totalTracks 89,740 · distinctArtists 29,811 · distinctAlbums 57,637 · distinctGenres 113
```

A divergence here is a load that failed in silence.

## 4. Run

```powershell
dotnet run --project src/Host/SpotifyDataAnalysis.Api --launch-profile http   # http://localhost:5140, Swagger at /swagger, health at /health
cd frontend; npm ci; npm run dev                                               # SPA on http://localhost:5173
```

`frontend/.env.development` is versioned and already points at the Host's `http` profile
(`http://localhost:5140`), and `appsettings.Development.json` already allows `http://localhost:5173`
in CORS. For any other target, copy `.env.example` to `.env.local` and set `VITE_API_BASE_URL`.
Remember that it is resolved **at build time**, so changing the API URL means rebuilding the SPA.

The similarity index is built in the background at startup. Until it is ready,
`/api/recommendations/**` answers **503 with `Retry-After`**, even though `/health` already returns 200.

## 5. Train and query

```powershell
curl -X POST http://localhost:5140/api/model/train     # returns the champion election table
curl http://localhost:5140/api/model/current           # version, metrics, feature importance
curl "http://localhost:5140/api/recommendations/track/<trackId>?limit=10&strategy=blend"
```

For `strategy=blend`, pick a seed that has co-occurrence: the matrix covers 9,664 of the 89,740
tracks, and a seed outside it falls back to content-based with a warning. `70LcF31zb1H0PyJoS1Sx1r`
("Creep", Radiohead) is a known good one.

## Or just Docker

The image carries only the API. It still needs a PostgreSQL that already holds the catalogue (steps 2
and 3, or the dump-and-restore path in [`deploy/DEPLOY.md`](deploy/DEPLOY.md)).

```powershell
docker build -t spotifydataanalysis-api .
docker run --rm -m 512m --memory-swap 512m -p 8080:8080 `
  -e ConnectionStrings__SpotifyDb="Host=host.docker.internal;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=<password>" `
  spotifydataanalysis-api
```

The container runs as `Production` on port 8080, so Swagger is off. Image size: 349 MB. Production
configuration (CORS origins, forwarded headers, the SPA's build-time API URL) is in
[`deploy/DEPLOY.md`](deploy/DEPLOY.md).

## Tests and quality gates

```powershell
dotnet test                                             # 807 tests
dotnet test tests/SpotifyDataAnalysis.ArchitectureTests  # 45 boundary rules, fast
```

The same gates CI runs, which are also the Definition of Done:

```powershell
dotnet build SpotifyDataAnalysis.sln -c Release --no-restore   # 0 errors; TreatWarningsAsErrors is on, so the build is the lint
dotnet test SpotifyDataAnalysis.sln -c Release --no-build --logger "console;verbosity=normal"
npm --prefix frontend run typecheck
npm --prefix frontend run lint                                 # oxlint --max-warnings=0
npm --prefix frontend test
npm --prefix frontend run build
```

Two tests are marked `[PostgresFact]` (the recommender quality gate and its proxy report) and one
`[CatalogPostgresFact]` (the imputation seeder against a live database). All three skip without a real
database. CI **prints their names and the reason** in the job summary, because a skipped test that
nobody can see is a lie told in green. With `ConnectionStrings__SpotifyDb` set in the environment they
run for real, and they are the only tests that exercise the recommender gate against the real
catalogue.

Measured on 2026-10-19 at commit `a10f401`: 807 tests, 804 passed, 3 skipped (the three above), 0
failed.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| The SPA says "The API did not respond", but `/health` returns 200 | the SPA is not on port 5173, so CORS blocks it | run it on 5173, or add the origin to `Cors:AllowedOrigins` in `appsettings.Development.json` |
| `npm run dev` exits with no clear error | port 5173 is taken; `vite.config.ts` sets `strictPort: true`, so Vite stops instead of moving | free port 5173 rather than changing it |
| `/api/recommendations/**` returns 503 | the similarity index is still warming up | wait for the 200; honour `Retry-After` |
| `strategy=blend` comes back with `collaborativeSignalUnavailable: true` and a warning | the seed has no co-occurrence, so the response fell back to content-based | use a seed inside the matrix (see step 5) |
| `?searchTerm=…` returns 400 | unknown query parameters are rejected with RFC 7807 | `/api/tracks` filters by `search` |
