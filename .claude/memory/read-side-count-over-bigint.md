---
name: read-side-count-over-bigint
description: teste de contrato do read-side afirma só o TEXTO do SQL — 18 testes verdes e o endpoint dava 500 em 100% das chamadas
metadata:
  type: feedback
---

A regra operacional (`COUNT(*) OVER()` → `long`; smoke test com Postgres real
antes do Done) está em `.claude/agents/dapper-readside-specialist.md`. O que mora
aqui é **quanto isso custou** e por que a suíte verde não é evidência.

**O caso.** No E2.2, o endpoint `GET /api/insights/popularity/top` dava **500 em
100% das chamadas** com 18 testes verdes na suíte. Causa: `COUNT(*) OVER()` do
PostgreSQL devolve `bigint` (Int64), e o record materializado pelo Dapper tipava
o total como `int`. Sem construtor compatível, `InvalidOperationException` em
runtime. A query de `summary` funcionava só por coincidência — o result dela já
usava `long`.

**Por que os testes não pegaram.** Os testes de contrato do read-side afirmam
apenas o **texto** do SQL (via `internal const Sql` + `InternalsVisibleTo`). Eles
não executam e não materializam. Erro de tipo, de materialização e de dialeto
passam verdes e só aparecem contra um banco vivo.

**O que isso significa na prática:** para qualquer card do read-side, "os testes
passaram" não é evidência de nada. A evidência é a chamada real ao endpoint,
contra Postgres real, com a saída reportada.

Receita do smoke test: Docker `postgres:16` → `dotnet ef database update` do
Catalog (a design-time factory lê `ConnectionStrings__SpotifyDb`) → semear linhas
via SQL, com o jsonb de `audio_features`/`artists` em chaves **PascalCase**
(`Genre`, `IsImputed`, `Name`) → subir o Host com a env var → bater no endpoint.

Vale para toda query paginada nova que copiar a CTE
`ROW_NUMBER() + COUNT(*) OVER()`. Ver [[ef-tojson-setter-privado]] para a versão
write-side do mesmo problema: teste in-memory não prova persistência.
