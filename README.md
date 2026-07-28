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
src/Jobs/SpotifyDataAnalysis.Jobs           worker in-process (processamento do Outbox)
tests/SpotifyDataAnalysis.ArchitectureTests testes de arquitetura (ArchUnitNET)
tests/SpotifyDataAnalysis.SharedKernel.Tests testes unitários do SharedKernel
```

## Rodando

```bash
dotnet build
dotnet test
dotnet run --project src/Host/SpotifyDataAnalysis.Api   # Swagger em /swagger, health em /health
```

Connection string PostgreSQL: `ConnectionStrings:SpotifyDb` (User Secrets / env `ConnectionStrings__SpotifyDb`).

## Fluxo de trabalho (P.O → Trello → Dev)

- **`@spotify-po`** — refina specs em cards de Trello (objetivo, escopo, critérios de aceite, riscos, decisões pendentes).
- **`@spotify-dev`** — implementa os cards respeitando a arquitetura; **Definition of Done**: `dotnet build` 0/0 e `dotnet test` verde (incluindo ArchitectureTests).

> **Estado atual:** fundação compartilhada pronta, sem módulos de domínio — aguardando as especificações do projeto.
