# Spotify Data Analysis — front

SPA em React + Vite + TypeScript + Tailwind + shadcn/ui que consome a API .NET do
SpotifyDataAnalysis. Build estático puro: não depende de ser servido pelo Host .NET e é publicável em
qualquer host estático. A ligação com a API é feita por URL absoluta (`VITE_API_BASE_URL`) e liberada
pela policy `SpotifyCorsPolicy` que já existe no Host.

## Stack

| Camada      | Escolha                                             |
| ----------- | --------------------------------------------------- |
| Build       | Vite 8                                              |
| Linguagem   | TypeScript 6 (`strict`, `noUncheckedIndexedAccess`) |
| UI          | React 19                                            |
| Estilo      | Tailwind CSS 4 (`@theme`, sem `tailwind.config`)    |
| Componentes | shadcn/ui sobre Radix — `button`, `badge`           |
| Rotas       | react-router-dom 7                                  |
| Ícones      | lucide-react                                        |
| Lint        | oxlint (`--max-warnings=0`)                         |

## Rodar a partir de clone limpo

```bash
cd frontend
npm install
npm run dev            # http://localhost:5173
```

Não é preciso configurar nada para o fluxo local: o `.env.development` versionado já aponta para
`http://localhost:5140`, que é a porta do perfil `http` do Host .NET.

Para apontar para outra API (deploy, túnel, outra porta), crie um `.env.local` — ele tem precedência
sobre o `.env.development` e não é versionado:

```bash
cp .env.example .env.local
# edite VITE_API_BASE_URL
```

| Variável              | Obrigatória | Default        | Papel                                                                                                                                                                    |
| --------------------- | ----------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `VITE_API_BASE_URL`   | não         | mesma origem   | Origem da API, sem barra final. Vazio ou omitido significa mesma origem (útil com proxy reverso). Valor presente e malformado exibe erro de configuração em cada tela.   |
| `VITE_API_TIMEOUT_MS` | não         | `20000`        | Corte de tempo por requisição. Hospedagem gratuita acorda devagar.                                                                                                       |

Variáveis `VITE_*` são embutidas no bundle em tempo de build. Trocar de API depois do build exige
rebuild — é o comportamento esperado de host estático, e o motivo de nada aqui depender de arquivo
escrito em runtime.

### Subir a API junto

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://localhost:5140'
$env:ConnectionStrings__SpotifyDb='Host=localhost;Port=5432;Database=spotify_data_analysis;Username=postgres;Password=admin'
dotnet run --project src/Host/SpotifyDataAnalysis.Api --no-launch-profile
```

`appsettings.Development.json` já traz `http://localhost:5173` em `Cors:AllowedOrigins`. Ao publicar o
front em outro domínio, acrescente a origem lá — sem isso o navegador bloqueia a resposta mesmo com a
API no ar, e o painel mostra o mesmo estado de "API não respondeu".

## Scripts

| Script              | O que faz                                                 |
| ------------------- | --------------------------------------------------------- |
| `npm run dev`       | Servidor de desenvolvimento na porta 5173 (`strictPort`). |
| `npm run build`     | `tsc -b` + `vite build`, saída em `dist/`.                |
| `npm run typecheck` | Só a checagem de tipos.                                   |
| `npm run lint`      | oxlint; falha em qualquer warning.                        |
| `npm run preview`   | Serve o `dist/` para conferir o build.                    |

## Como o front fala com a API

Toda saída HTTP passa por `src/api/http-client.ts`. Ele é o único lugar que conhece e interpreta os
três contratos publicados pela API:

- **`ApiResult<T>`** (`{ success, message, data }`) — envelope de sucesso. O cliente devolve `data` já
  desembrulhado; nenhuma tela acessa `.data.data`.
- **`PagedResult<T>`** — forma das listas paginadas do read-side, tipada em `src/api/contracts.ts` e
  pronta para as telas de catálogo e insights.
- **ProblemDetails (RFC 7807)** — contrato de erro do `ExceptionHandlingMiddleware`. Vira `ApiError`,
  com `kind` fechado (`notFound`, `validation`, `offline`, `timeout`, `server`, …), `traceId` e erros
  por campo.

Falha de transporte — API fora do ar, CORS negado, timeout — desemboca no mesmo `ApiError`, então
nenhuma tela precisa distinguir "resposta de erro" de "nenhuma resposta". O `ApiError` separa o que a
API disse (`apiTitle`, `detail`, `problemType`, `traceId`) do que o usuário lê (`headline`,
`nextStep`), para a evidência do RFC 7807 aparecer sem tradução inventada.

`useApiResource` reduz uma leitura a `loading | ready | failed` — não existe estado em que
`status === 'ready'` e o dado seja nulo. `ResourceBoundary` é o único ponto que mapeia esses estados
para os componentes compartilhados de `src/components/feedback/`.

## Estrutura

```
src/
  api/           contratos, cliente HTTP, ApiError, um módulo por área da API
  components/
    data/        leitura de números (MetricTile, CoverageBar)
    diagnostics/ sonda que demonstra o erro RFC 7807 contra a API real
    feedback/    LoadingState, EmptyState, ErrorState e o ResourceBoundary
    foundation/  primitivas visuais (Panel, TickRule)
    layout/      shell, rail de navegação, barra de conexão
    ui/          shadcn/ui — código vendorizado pela CLI
  hooks/         useApiResource
  lib/           formatação pt-BR e utilitário de classes
  pages/         uma tela por seção
  providers/     recursos compartilhados por contexto
  navigation.ts  fonte única das seções do shell
```

Para acrescentar um componente shadcn: `npx shadcn@latest add <nome>` — o `components.json` já está
configurado. Depois ajuste as classes para o vocabulário de [`DESIGN.md`](./DESIGN.md) (cantos retos,
rótulos em mono maiúsculo), porque o default do shadcn é arredondado e neutro demais para este
projeto.

O sistema visual — paleta, tipografia, densidade, componentes base e animação — está em
[`DESIGN.md`](./DESIGN.md). As telas seguintes reusam esse vocabulário em vez de criar o próprio.
