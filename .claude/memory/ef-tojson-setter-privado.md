---
name: ef-tojson-setter-privado
description: value object get-only mapeado com ToJson grava {} no jsonb silenciosamente, e nenhum teste in-memory pega isso
metadata:
  type: architecture
---

A regra operacional ("VO mapeado por EF precisa de setter privado") está em
`.claude/agents/ef-persistence-specialist.md` e
`.claude/agents/dotnet-domain-specialist.md`, que é onde ela é acionável. O que
mora aqui é a **evidência** — o motivo de a regra existir e de não se poder
confiar na suíte verde.

**O bug.** No módulo Catalog, o owned type `AudioFeatures` era mapeado como JSON
via `OwnsOne(t => t.AudioFeatures, o => o.ToJson("audio_features"))`. As 15
propriedades eram só-leitura (`public double Danceability { get; }`, setadas no
construtor). O EF Core, por convenção, **só mapeia propriedade com setter** ao
serializar owned type como JSON. Resultado: **toda faixa persistia
`audio_features = {}`** — as features sumiam sem erro nenhum. O snapshot
confirmava: o owned type tinha só a shadow key `TrackId`, zero das 15
propriedades.

**Por que passou despercebido do E1 até o E3.** Todos os testes do E1 são
in-memory, com fakes de repositório — nenhum provider EF real. A serialização
nunca tocou um banco até o catálogo ser populado de verdade (E1.10). Pior: os
smoke tests do E2/E3 usavam jsonb **escrito à mão**, com as chaves PascalCase
corretas — então validaram a LEITURA e deram a impressão de cobertura, enquanto a
ESCRITA seguia quebrada.

**Fix:** setters privados (`{ get; private set; }`). O EF passa a mapear por
convenção e o VO continua imutável de fora. A migration só sincroniza o snapshot;
não há DDL, o jsonb não muda.

**O corolário, que é o que generaliza:** validar **persistência** contra Postgres
real — não só leitura — antes de confiar em qualquer mapeamento novo. Ver
[[read-side-count-over-bigint]] para a outra metade dessa lição.

Nem todo owned type tinha o bug: `ReleaseDate`, mapeado por table-split com
`Property()` explícito, estava correto. Só o `ToJson` por convenção quebrava.
