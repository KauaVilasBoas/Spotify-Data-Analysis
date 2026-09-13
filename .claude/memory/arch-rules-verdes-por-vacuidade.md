---
name: arch-rules-verdes-por-vacuidade
description: 4 das 42 regras do ArchUnitNET avaliavam zero tipos por vários épicos — o que segurava a pureza do SharedKernel era o .csproj, não o teste
metadata:
  type: feedback
---

As armadilhas de API (`ResideInAssembly` exige `ClrAssembly`; `ResideInNamespace`
é correspondência exata) estão em `.claude/agents/architecture-reviewer.md`. O que
mora aqui é **por quanto tempo passou e o que realmente segurava o invariante**.

**O caso.** Quatro dos 42 testes de arquitetura selecionavam **zero tipos**.
`ResideInAssembly` recebia o nome simples do assembly, e o ArchUnitNET 0.13.3 casa
por `FullName` — filtro vazio. Com `WithoutRequiringPositiveResults()` por cima, as
quatro regras passavam por vacuidade, verdes, sem nunca ter avaliado nada.

**O que de fato segurava a pureza do SharedKernel** era o `.csproj` sem
referências. O teste não contribuía com nada. Isso só apareceu porque a fatia do
E4.8/E1.13/E6.9 acrescentou o primeiro tipo novo ao SharedKernel em vários épicos
— ou seja, o falso-verde sobreviveu justamente porque ninguém exercitou a regra.

**Havia uma quinta armadilha no mesmo padrão:** `ResideInNamespace("Microsoft.AspNetCore")`
nunca detectaria nada nem com o subject correto, porque nenhum tipo do ASP.NET mora
no namespace raiz. Corrigida no `SharedKernelPurityTests` com
`ResideInNamespaceMatching`; **as três regras equivalentes de Catalog, Analytics e
Prediction continuam com o exato** (`*ModuleIsolationTests.cs`) e seguem incapazes
de detectar violação.

**O que isso significa na prática:** contagem de regras não é cobertura. Regra de
arquitetura nova só conta como prova depois de um par vermelho/verde com violação
injetada de propósito — foi assim que a correção foi validada.

Mesma família de [[read-side-count-over-bigint]] e [[ef-tojson-setter-privado]]:
suíte verde que não executa o que afirma proteger.
