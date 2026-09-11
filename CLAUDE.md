# SpotifyDataAnalysis

> Memória de projeto do Claude Code. Mantenha este arquivo curto e de alto sinal:
> ele é carregado em toda sessão, principal e de subagente.

## Diretrizes de comportamento

1. **Pense antes de codar** — declare suposições explicitamente. Se existe mais de uma interpretação, apresente-as em vez de escolher calado. Diga quando existir um caminho mais simples. Se algo estiver genuinamente ambíguo, pare e pergunte.
2. **Simplicidade primeiro** — o mínimo de código que resolve o problema. Sem feature especulativa, sem abstração para uso único, sem configurabilidade que ninguém pediu, sem tratamento de erro para cenário impossível.
3. **Mudanças cirúrgicas** — toque só no que o pedido exige. Siga o estilo existente. Não refatore, reformate nem "melhore" código vizinho que não fazia parte do pedido.
4. **Execução orientada a objetivo** — transforme tarefa em meta verificável ("corrigir o bug" vira "escrever o teste que reproduz, depois fazer passar"). Para trabalho de vários passos, declare um plano curto com uma verificação por passo, e itere até cada passo estar verificado.
5. **Orquestrador, não implementador** — a sessão principal planeja, decide e coordena; ela não implementa. Implementação e análise delegáveis vão para um subagente especialista, despachado em paralelo quando os escopos não conflitam.

## Stack

C# · .NET 8 · monólito modular (DDD + CQRS) · PostgreSQL (EF Core no write-side, Dapper no read-side) · ML.NET · xUnit + ArchUnitNET · React 19 + Vite + TypeScript + Tailwind + ECharts · Docker · GitHub Actions.

## Comandos canônicos

Use exatamente estes — não chute.

**Backend (raiz do repositório):**

- **Install:** `dotnet restore SpotifyDataAnalysis.sln`
- **Lint:** não há linter separado — `TreatWarningsAsErrors` está ligado no `Directory.Build.props`, então **o build é o lint**.
- **Typecheck:** idem — a compilação é a checagem de tipo.
- **Test:** `dotnet test SpotifyDataAnalysis.sln -c Release --no-build --logger "console;verbosity=normal"`
- **Build:** `dotnet build SpotifyDataAnalysis.sln -c Release --no-restore`
- **Run:** `dotnet run --project src/Host/SpotifyDataAnalysis.Api --launch-profile http` → `http://localhost:5140` (Swagger em `/swagger`)

**Frontend (a partir de `frontend/`):**

- **Install:** `npm ci`
- **Lint:** `npm run lint` (`oxlint --max-warnings=0` — zero é o gate)
- **Typecheck:** `npm run typecheck`
- **Test:** não existe suíte de teste de frontend hoje. Se uma tarefa exigir, isso é uma decisão a tomar, não algo a improvisar.
- **Build:** `npm run build`
- **Dev:** `npm run dev` → `http://localhost:5173`

O banco local é `spotify_data_analysis` em `localhost:5432`. A connection string vem de `ConnectionStrings__SpotifyDb` (User Secrets ou variável de ambiente) — **nunca** de arquivo versionado.

## Tabela de roteamento de especialistas

Quando o trabalho é delegável, despache o especialista que casa com a tarefa, nunca um agente genérico. A frase "quando usar" **é** a regra de roteamento. O tier de modelo é por despacho: use a camada mais barata que ainda resolve.

| Agente | Quando usar | Tier |
|---|---|---|
| `dotnet-domain-specialist` | Domain/Application de um módulo: aggregate, value object, domain event, command + handler, invariante de negócio. | sonnet |
| `dapper-readside-specialist` | Read-side: Query/Handler/Result no mesmo `.cs`, paginação `ROW_NUMBER()` + `COUNT(*) OVER()`, dialeto PostgreSQL. | sonnet |
| `ef-persistence-specialist` | Write-side de persistência: `DbContext`, mapeamento snake_case, migration, índice, owned/`ToJson`, Outbox. | sonnet |
| `api-endpoint-specialist` | Host/Api: endpoint, `ProblemDetails`, validação de query param, Swagger, middleware, Composition Root. | sonnet |
| `mlnet-specialist` | Módulo Prediction: pipeline ML.NET, treino, métrica, cross-validation, importância de features, promoção de versão. | opus |
| `react-frontend-specialist` | Qualquer coisa sob `frontend/`: tela, componente, gráfico ECharts, Tailwind, estado de recurso. | sonnet |
| `test-engineer` | Teste xUnit test-first, caso de borda, caminho de erro, fixture determinística. | sonnet |
| `architecture-reviewer` | Fronteira de módulo, direção de dependência, pureza do SharedKernel, regra de ArchUnitNET. Somente leitura. | opus |
| `code-reviewer` | Revisão geral de diff: bug, tratamento de erro, cobertura faltando, escopo além do pedido. Somente leitura. | sonnet |
| `security-reviewer` | Segredo no fonte, injeção, autorização só no cliente, vazamento em log/resposta, entrada sem limite. Somente leitura. | sonnet |
| `devops-engineer` | CI GitHub Actions, `Dockerfile`, `.dockerignore`, `deploy/`, configuração de ambiente. | sonnet |
| `debugger` | Causa raiz de bug, crash ou teste instável — **antes** de qualquer correção ser proposta. | sonnet |
| `spotify-po` | Objetivo de negócio → backlog: descoberta, critérios de aceite, cards no Trello, prioridade. Não escreve código. | opus |

## Arquitetura — regras invioláveis

As 42 regras do `SpotifyDataAnalysis.ArchitectureTests` (ArchUnitNET) validam boa parte disto. **Nunca afrouxe uma regra existente para fazer um teste passar** — isso é regressão, não ajuste.

- **Monólito modular**, módulos por bounded context: `Catalog`, `Analytics`, `Prediction`. Cada um com `Domain · Application · Infrastructure · Contracts`.
- **Single-tenant / uso próprio.** Não existe isolamento por `companyId`/tenant. Não introduza filtro global de tenant sem decisão explícita.
- O `Domain` de um módulo **nunca** referencia `Domain`/`Application`/`Infrastructure` de outro. Módulos conversam **só via `*.Contracts`** (interface pública + DTO próprio); ID de outro domínio é guardado **por valor** (`Guid`), sem FK nem navegação cross-módulo.
- `Domain` não referencia EF Core, Dapper nem ASP.NET. `SharedKernel` é **puro**.
- `Host` referencia só o ponto de entrada dos módulos (`Application`/`IModule`) — nunca o `Domain` interno.
- **CQRS com mediator próprio** (no SharedKernel/Infrastructure): `IRequestHandler<TRequest,TResult>` com `HandleAsync`, `IMediator`, pipeline de `IPipelineBehavior` (Validation → Logging → Transaction). Cada Command tem o **seu** handler; nada genérico multiuso.
- **EventHandler é por domínio, não por evento**: um agregador por bounded context.
- **Eventos, estratégia híbrida.** Efeito colateral (sincronizar módulos, disparar job, chamada externa) → Outbox/eventual, e falha ali não derruba a operação principal. Invariante de negócio que exige atomicidade cross-domínio → handler síncrono na mesma transação, com rollback real — use com parcimônia.
- `DomainEvent` (interno, rico) ≠ `IntegrationEvent` (público, achatado, em `Contracts`). Traduza antes de publicar.
- A API do Spotify é **serviço externo**: adapter com porta no Application/Contracts e implementação na Infrastructure. Nunca chamada do Domain. Ingestão idempotente e resiliente a rate limit.

Antes de criar algo novo, **procure o que já existe** — `SharedKernel` e `Infrastructure` compartilhada já entregam `Entity`, `AggregateRoot`, `ValueObject`, `Guard`, `IClock`, `PagedQuery/PagedResult`, mediator, behaviors, Outbox e `BaseDataAccess`.

## Convenções

- **Commits atômicos e frequentes**, em **Conventional Commits**, mensagem em português. Commite a cada milestone que compila; nunca acumule dezenas de mudanças.
- **NUNCA adicione o trailer `Co-Authored-By`** — nem do Claude, nem de agente. Commits saem só como o autor do `git config`.
- Faça commit via `git commit -F <arquivo>` ou `-m` de uma linha. **Evite here-string do PowerShell** para mensagem — vaza um `@` na primeira linha. Confira com `git log -1 --format=%s`.
- Versionamento **SemVer**, derivado dos tipos de Conventional Commit (`fix:` → patch, `feat:` → minor, `BREAKING CHANGE` → major).
- Trabalhe em branch de tarefa (`feature/...`). **Não faça `push` nem abra PR** sem pedido explícito.
- **Definition of Done do backend:** `dotnet build` com **0 erro / 0 warning** (warnings-as-errors ligado) **e** `dotnet test` verde, incluindo os ArchitectureTests. Rode de verdade e **reporte a saída real** — nunca "deveria passar".
- **Definition of Done do frontend:** `npm run typecheck`, `npm run lint` e `npm run build`, os três verdes, com saída reportada.
- Teste que precisa de Postgres real usa `[PostgresFact]` — é pulado no CI de propósito, e o skip aparece nomeado no summary. Verde nunca pode significar "não rodou".
- Segredos vão para User Secrets ou variável de ambiente. Nunca no repositório.
- Não invente escopo. O que sair do pedido, proponha como próximo passo.
