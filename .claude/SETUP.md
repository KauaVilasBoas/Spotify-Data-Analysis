# Adoção do vibe-coding-toolkit — registro de progresso

> Fonte: [soumatheusgomes/vibe-coding-toolkit](https://github.com/soumatheusgomes/vibe-coding-toolkit),
> seguindo `docs/02-playbook-onboarding.md`.
>
> Este arquivo é o **único** registro de acompanhamento da adoção — atualizado por
> quem orquestra, ao fim de cada etapa. Não é changelog (isso é o `git log`); é o
> estado do setup e o porquê de cada desvio do template.

Iniciado em **2026-09-11**, na branch `feature/vibe-coding-toolkit-setup`.

---

## Checklist final do Playbook

| # | Item | Estado |
|---|---|---|
| 1 | Claude Code instalado e autenticado | ✅ |
| 2 | Superpowers instalado | ✅ |
| 3 | Ponytail instalado | ✅ |
| 4 | Caveman instalado | ✅ |
| 5 | `CLAUDE.md` preenchido — stack, comandos canônicos, tabela de agentes | ✅ |
| 6 | `.claude/settings.json` + hooks reais | ✅ |
| 7 | Regra de ondas paralelas copiada e referenciada | ✅ |
| 8 | Fluxo completo ponta a ponta rodado ao menos uma vez | ⬜ |
| 9 | Quality gates configurados, divisão decidida | ⬜ |
| 10 | Ao menos uma regra nova em warn→error | ⬜ |
| 11 | Sistema de memória leve configurado | ✅ |
| 12 | (Opcional) Vault Obsidian + MCP | ⬜ |
| 13 | (Opcional) Graphify | ⬜ |
| 14 | (Opcional) agent-browser | ⬜ |

**Verificação pendente de humano** — os checkpoints "Veja funcionando" do Playbook
exigem uma sessão interativa, não são automatizáveis a partir daqui:

- [ ] Superpowers: pedido ambíguo dispara a skill `brainstorming` antes de qualquer código.
- [ ] Ponytail: pedido que convida over-engineering volta com a escada de decisão.
- [ ] Caveman: `/caveman-stats` mostra economia acumulada — e `Est. net` não está negativo.

---

## Etapas concluídas

### Etapa 1 — Parte 2: os três pilares

Instalados por comando `/plugin` na sessão. `enabledPlugins` foi gravado no
`settings.json` **do projeto**, não no do usuário — mantido assim de propósito:
os pilares viajam junto do repositório.

Commit: `108b865`.

### Etapa 2 — Parte 3: configuração do projeto

| Commit | O quê |
|---|---|
| `1767a48` | `.gitignore`: `.claude/` sai do ignore total |
| `7d215b8` | `CLAUDE.md` + 12 especialistas; `spotify-dev` aposentado |
| `f491def` | Regra de ondas paralelas + referência no `CLAUDE.md` |
| `834b1c7` | `settings.json` + hook de SessionStart |

### Etapa 3 — Parte 6: memória

Camada 1 montada em `.claude/memory/` via o prompt `06-memory-bootstrap`
(`MEMORY_DIR=.claude/memory`, `CONFIG_FILE=CLAUDE.md`, `LINE_CAP=130`).
Carregada por `@import` no `CLAUDE.md`.

As 6 entradas que viviam em `~/.claude/projects/` foram passadas pelo teste
estreito e viraram 7 — encolhendo, não crescendo. A memória global agora é um
ponteiro de uma linha.

**Camada 2 não existe** e não foi inventada: o projeto não tem vault nem wiki.
Ao bater o teto de 130 linhas, a sessão deve parar e pedir decisão.

Commit: `ad01451`.

---

## Decisões tomadas — e onde desviamos do template

| Decisão | Escolha | Racional |
|---|---|---|
| Trailer `Co-Authored-By` | **Nunca usar** | Regra pré-existente do projeto, confirmada. Commits saem só como o autor do `git config`. |
| `spotify-dev` monolítico | **Aposentado** | Um agente que pode tocar qualquer arquivo nunca tem escopo disjunto de outro — bloqueia ondas paralelas na estrutura. |
| Elenco de especialistas | **12 + `spotify-po`** | Derivado dos domínios reais do repo, não copiado do roster genérico do toolkit. |
| `.claude/` no `.gitignore` | **Versionado seletivamente** | O setup precisa sobreviver a clone limpo — o mesmo gate que o E6.1 defende. Só `settings.local.json` e estado de sessão ficam fora. |
| Idioma do `CLAUDE.md` | **Português**, estrutura 1:1 com o template | Instrução interna de trabalho. O `README.md` do portfólio segue em inglês. |
| Hook `example-command-proxy` | **Não escrito** | Era placeholder. O Playbook permite remover hook não usado; escrevemos um que resolve problema real daqui. |
| Bloco `env` do template | **Removido** | Chave de API inventada em arquivo versionado é ruído, não configuração. |
| Memória: global vs repo | **Repo é canônico** | Versionada, sobrevive a troca de máquina. A global virou ponteiro para evitar duas fontes da verdade. |
| Quality gates ESLint/Biome | **Adaptado, não copiado** | O toolkit pressupõe JS/TS. O backend é .NET (sem ESLint) e o frontend usa oxlint. Copiar `templates/eslint/` seria seguir a letra e trair a ideia. |

---

## Pendente

1. **Parte 5 — quality gates.** Backend: não existe `.editorconfig`; as regras de
   estilo não são medidas hoje. O `TreatWarningsAsErrors` já é o *fim* da
   migração warn→error, não o começo dela. Frontend: oxlint já em
   `--max-warnings=0`; falta escrever como uma regra nova entra.
2. **Faxina do `.claude/settings.local.json`** — 290 entradas, com lixo de
   sessões mortas (caminhos de `tool-results`, `kill 349`, `sed` pontual). O que
   sobreviver e for genérico sobe para o `settings.json` versionado.
3. **Parte 4 — fluxo completo ponta a ponta.** Brainstorm → plano → ondas
   paralelas → revisão multi-agente → commit. Só fecha sobre um card real; é o
   último item, por construção.
