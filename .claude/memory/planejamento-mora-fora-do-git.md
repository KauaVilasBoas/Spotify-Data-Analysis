---
name: planejamento-mora-fora-do-git
description: PLANO.md, PENDENCIAS.md e o board do Trello são a fonte do backlog — e nada disso está no git, porque docs/ é gitignored de propósito
metadata:
  type: reference
---

O planejamento deste projeto **não está no repositório versionado**. Procurar por
ele no histórico do git ou num clone limpo não encontra nada — e isso é
deliberado, não um esquecimento.

**Onde está:**

- **`docs/PLANO.md`** — o plano do P.O: épicos E0–E6, cards candidatos, roadmap,
  modelo de dados, superfície da API, decisões numeradas (DP-n) com o racional de
  cada uma.
- **`docs/PENDENCIAS.md`** — checklist de "resolver no final", com o registro das
  integrações de branch já feitas.
- **Board do Trello "Spotify Data Analysis"** (`1nHOJ9P1`) — a fonte viva do
  backlog. Os `#nn` referenciados nos documentos são o `idShort` do card. Acesso
  via as ferramentas `mcp__trello__*`.

**Por que ficam fora do git:** `docs/` está no `.gitignore` (âncora `/docs/`)
porque planejamento não é peça de portfólio. O repositório publica o produto; o
caminho até ele fica local.

**O que isso significa na prática:** antes de assumir que uma decisão nunca foi
tomada, ou que um épico não foi refinado, **olhe esses três lugares**. Boa parte
do que parece "em aberto" olhando só o código já foi decidido e registrado ali.
