---
name: run-spotify-data-analysis
description: Sobe, dirige e fotografa o SpotifyDataAnalysis (API .NET 8 em 5140 + SPA React/Vite em 5173). Use quando pedirem para rodar, subir, iniciar, testar manualmente, tirar screenshot, inspecionar visualmente ou conferir no app real que uma mudanca funciona. Inclui smoke da API e driver de browser sem instalar dependencia.
---

Monolito modular .NET 8 (`src/Host/SpotifyDataAnalysis.Api`) mais SPA React/Vite (`frontend/`).
Dirigido por dois harnesses versionados aqui: `smoke-api.ps1` para o backend e
`driver.mjs` para a SPA. **Caminhos relativos a raiz do repositorio.**

Verificado em: Windows 11, PowerShell 5.1, .NET 8, Node v24.16.0, PostgreSQL 18,
Chrome em `C:\Program Files\Google\Chrome\Application\chrome.exe`.

## Pre-requisitos

Postgres local com o catalogo carregado, e a connection string em User Secrets:

```powershell
Test-NetConnection -ComputerName localhost -Port 5432 -InformationLevel Quiet
dotnet user-secrets list --project src/Host/SpotifyDataAnalysis.Api
```

Espere `True` e uma chave `ConnectionStrings:SpotifyDb`. O banco de referencia tem
**89.740 faixas e 0 imputadas**; o smoke imprime esses numeros.

O driver de browser nao instala nada: usa o Chrome (ou Edge) ja instalado e o
`WebSocket` nativo do Node 22+. Se o binario estiver em outro lugar, exporte
`CHROME_PATH`.

## Antes de subir: as duas portas mentem

```powershell
foreach ($p in 5140,5173) {
  $c = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($c) { $pr = Get-Process -Id $c.OwningProcess; "porta $p OCUPADA por PID $($c.OwningProcess) ($($pr.ProcessName)) desde $($pr.StartTime)" }
  else { "porta $p livre" }
}
```

- **5140 ocupada** costuma ser uma API antiga esquecida de outra sessao. Ela responde
  `/health` 200 alegremente e serve codigo velho. Confira `StartTime` antes de confiar.
  Derrube com `Stop-Process -Id <pid> -Force`.
- **5173 ocupada** pode ser **outro projeto seu**, e isso e silencioso e caro: o
  `vite.config.ts` tem `strictPort: true`, entao o nosso dev server **morre** em vez
  de trocar de porta, e voce acaba fotografando a aplicacao errada. Confirme lendo o
  `location.href` (o `driver.mjs` imprime isso na mensagem de falha).

## Subir

Dois processos, cada um em background:

```powershell
dotnet run --project src/Host/SpotifyDataAnalysis.Api --launch-profile http
npm --prefix frontend run dev
```

API em `http://localhost:5140` (Swagger em `/swagger`), SPA em `http://localhost:5173`.

**Nao troque a porta do frontend.** `appsettings.Development.json` traz
`Cors:AllowedOrigins = [ "http://localhost:5173" ]`, fixo. Em qualquer outra porta o
browser e bloqueado por CORS e a SPA mostra "The API did not respond", como se o
backend estivesse fora do ar. A API estara no ar, e a mensagem estara mentindo sobre a causa.

Nao ha proxy no Vite: `frontend/.env.development` ja aponta
`VITE_API_BASE_URL=http://localhost:5140`.

## Dirigir a API

```powershell
.\.claude\skills\run-spotify-data-analysis\smoke-api.ps1
```

Saida real de uma execucao verde:

```
== health ==
  OK   /health -> 200 Healthy
== warm-up do indice de similaridade ==
  OK   indice pronto em 0 s
== catalogo ==
  OK   faixas: 89740 | imputadas: 0 | generos: 113
  OK   tracks -> 89740 no total, primeiro: '49 Mercury Blues / The Brian Setzer Orchestra
== recomendacoes ==
  OK   content -> 10 de 10 itens em 29 ms (colapsadas: 3)
  OK   content: score = cosine + genre (0.9855)
  OK   blend -> 10 de 10 itens em 51 ms (colapsadas: 3)
  OK   blend: score (0.65) NAO e cosine+genre (0.9855), como esperado
SMOKE OK
```

Sai com codigo 1 na primeira asserçao que falhar. Parametros: `-BaseUrl`, `-Seed`, `-Limit`.

## Dirigir a SPA

```powershell
$d = ".\.claude\skills\run-spotify-data-analysis\driver.mjs"

# screenshot de uma rota
node $d shot "http://localhost:5173/" "$env:TEMP\overview.png"

# clicar num botao pelo texto e entao fotografar
node $d click "http://localhost:5173/recommendations?seed=70LcF31zb1H0PyJoS1Sx1r" "Blend" "$env:TEMP\blend.png"

# extrair o texto renderizado (rotulos e numeros) em vez de ler pixels
node $d text "http://localhost:5173/recommendations?seed=70LcF31zb1H0PyJoS1Sx1r" "Blend"
```

O modo `click` existe porque a estrategia do recomendador mora em `useState` e **nao**
na URL: sem clicar, so da para ver o caminho `content`. Passe `-` como texto do botao
para nao clicar em nada.

O driver espera **condicao do DOM**, nao tempo fixo. Quando estoura, ele imprime um
diagnostico com `location.href`, contagem de caracteres e de botoes, que e o que
revela app trocado por colisao de porta. Ajuste com `NAV_WAIT_MS` e `CLICK_WAIT_MS`
(padrao 8000 e 5000 ms; a primeira visita ao Vite frio pede 30000+).

Rotas: `/`, `/catalog`, `/insights`, `/model`, `/recommendations`.

## Sementes: a aleatoria nao serve

A matriz de co-ocorrencia cobre **10,8%** do catalogo. Uma semente sorteada faz
`strategy=blend` cair em content puro com aviso, e voce mede a coisa errada.

Semente boa, verificada: `70LcF31zb1H0PyJoS1Sx1r` ("Creep", Radiohead), 3.360 vizinhos.
Para achar outras:

```powershell
$line = (dotnet user-secrets list --project src/Host/SpotifyDataAnalysis.Api | Select-String "SpotifyDb").Line
$cs = $line.Substring($line.IndexOf('=') + 1).Trim()
$kv = @{}; $cs -split ';' | Where-Object { $_ -match '=' } | ForEach-Object { $p = $_ -split '=', 2; $kv[$p[0].Trim().ToLower()] = $p[1].Trim() }
$env:PGPASSWORD = $kv['password']
$q = "WITH pares AS (SELECT track_id_low AS id FROM prediction.track_cooccurrence UNION ALL SELECT track_id_high FROM prediction.track_cooccurrence), cnt AS (SELECT id, count(*) AS vizinhos FROM pares GROUP BY id) SELECT c.id, c.vizinhos, t.name FROM cnt c JOIN catalog.tracks t ON t.id = c.id ORDER BY c.vizinhos DESC LIMIT 3;"
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h $kv['host'] -p $kv['port'] -U $kv['username'] -d $kv['database'] -A -F' | ' -c $q
$env:PGPASSWORD = $null
```

## Gotchas

- **`/health` 200 nao significa indice pronto.** O indice de similaridade e montado em
  background no arranque; ate terminar, `/api/recommendations/**` devolve **503 com
  `Retry-After`**. O smoke espera esse 200 num laco; nao pule essa espera.
- **A forma da resposta muda por endpoint.** Envelope e sempre `{success, message, data}`,
  mas `/api/tracks` pagina em `data.items[]` com `trackId`/`primaryArtist`, enquanto
  `/api/recommendations/**` devolve `data.recommendations[]`. Nao existe `data.items`
  em recomendacoes, e nao existe `id`/`artist` em tracks.
- **`score` significa coisas diferentes por estrategia.** Em `content`,
  `score = cosineScore + genreBoost` e a soma fecha. Em `blend`, `score` e o score final
  do blender normalizado, e a soma **nao** fecha (0.65 contra 0.9855 na semente do
  exemplo). Isso e intencional, nao bug: o smoke afirma as duas coisas.
- **CORS travado em 5173** (ver acima). O sintoma aparece como falha de backend.
- **`strictPort: true`** faz o dev server morrer em vez de trocar de porta.
- **Porta fixa de CDP e armadilha.** No Windows `child.kill()` nao alcanca a arvore do
  Chrome, entao processos filhos sobrevivem segurando a porta de debug, e a execucao
  seguinte se conecta ao **Chrome zumbi da anterior**. Sintoma: screenshot quase em
  branco com DOM vazio. O `driver.mjs` sorteia porta livre por execucao e mata a arvore
  com `taskkill /T /F`.
- **Colunas do banco nao tem os nomes obvios.** `prediction.track_cooccurrence` guarda
  par simetrico em `track_id_low`/`track_id_high` (nao existe `seed_track_id`), e
  `catalog.tracks` nao tem `primary_artist`.
- **MSBuild responde em portugues** (`Aviso(s)`/`Erro(s)`), entao filtro por "Warning"
  nao casa nada. E build incremental sem mudanca reporta **0 aviso** de forma enganosa:
  use `--no-incremental` quando for comparar com o baseline de **88 avisos**.

## Testes (nao substituem rodar o app)

```powershell
dotnet build SpotifyDataAnalysis.sln -c Release --no-restore --no-incremental
dotnet test SpotifyDataAnalysis.sln -c Release --no-build --logger "console;verbosity=normal"
npm --prefix frontend run typecheck
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
```

Tres testes sao `[PostgresFact]` e ficam **ignorados** sem banco. Com
`ConnectionStrings__SpotifyDb` no ambiente eles rodam de verdade, e sao os unicos que
exercitam o gate de qualidade do recomendador contra o catalogo real.

## Encerrar

```powershell
foreach ($p in 5140,5173) {
  $c = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($c) { taskkill /PID $c.OwningProcess /T /F }
}
```

## Troubleshooting

| Sintoma | Causa | Conserto |
|---|---|---|
| SPA mostra "The API did not respond" mas `/health` responde 200 | frontend fora de 5173, bloqueado por CORS | volte para 5173, ou acrescente a origem em `appsettings.Development.json` |
| `npm run dev` sai com codigo 1 ou 255 sem erro claro | 5173 ocupada por outro app, `strictPort: true` | libere 5173, nao troque a porta |
| Screenshot de ~15 KB, DOM vazio, `NAO_ENCONTRADO` no clique | conectou num Chrome zumbi de execucao anterior | ja corrigido no `driver.mjs` (porta sorteada + `taskkill /T`); se reaparecer, mate `chrome.exe` com `sda-driver` na linha de comando |
| `driver.mjs` falha com `location.href` de outra aplicacao | colisao de porta | confira quem ocupa 5173 |
| `/api/recommendations/**` devolve 503 | warm-up do indice em andamento | espere o 200, e o comportamento correto |
| `blend` devolve aviso de colaborativo indisponivel | semente sem co-ocorrencia | use uma semente da consulta de sementes acima |
| `rmSync EPERM` no fim do driver | Windows ainda segura o perfil | inofensivo, ja e engolido; sobra um diretorio em `%TEMP%` |
