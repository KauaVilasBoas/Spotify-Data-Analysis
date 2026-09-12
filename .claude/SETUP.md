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
| 9 | Quality gates configurados, divisão decidida | ✅ |
| 10 | Ao menos uma regra nova em warn→error | ✅ |
| 11 | Sistema de memória leve configurado | ✅ |
| 12 | (Opcional) Vault Obsidian + MCP | ⬜ adiado |
| 13 | (Opcional) Graphify | ✅ |
| 14 | (Opcional) agent-browser | ✅ |

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

### Etapa 4 — faxina das permissões

O `settings.local.json` tinha **298 entradas** e crescia sozinho: cada comando
aprovado numa sessão vira uma linha permanente, e ninguém poda. Durante esta
própria sessão ele subiu de 290 para 298.

Resultado: **31 locais + 53 versionadas**, com três destinos.

| Destino | O quê |
|---|---|
| `settings.json` (versionado) | Comandos canônicos do projeto: `dotnet`, `npm`/`npx`, e o MCP do Trello. São a definição operacional do projeto e valem para qualquer clone. |
| `settings.local.json` (máquina) | O que depende desta máquina: caminho do `psql.exe`, Docker, `gh`, portas locais, `git` amplo. |
| Removido | Lixo de sessão morta. |

O que saiu, e por quê:

- **Vazamento de escopo** — `Read(//c/Projetos/SISLAB/**)` e
  `Read(//c/Projetos/Lumen/**)` davam leitura a **outros projetos** a partir da
  sessão deste repositório. Esses dois são a razão de a faxina não ser cosmética.
- Caminhos de `tool-results` de sessões que não existem mais.
- `sed -i` de correções pontuais já aplicadas, `kill 349`, `echo "exit=$?"` e
  variantes, `rm` de arquivos temporários específicos.
- Mensagens de commit inteiras coladas como permissão de `printf`.
- Polling de GitHub Actions com SHA fixo de branch já mesclada.

Backup do arquivo original antes de sobrescrever (ele é gitignored — não há
histórico para recuperar):
`%TEMP%\settings.local.json.bak-2026-09-11`.

Commit: `18b8e75`.

### Etapa 5 — Parte 5: quality gates

Adaptada, não copiada. O toolkit pressupõe ESLint + Biome em JS/TS; aqui a
divisão em dois linters sem sobreposição vira **Roslyn analyzers no backend** e
**oxlint no frontend** — dois analisadores, domínios disjuntos, zero regra
duplicada.

**Medido antes de configurar** (o prompt `08` é explícito: instalar e medir, não
consertar):

| Medição | Resultado |
|---|---|
| Avisos de analisador na solução | **694 distintos** |
| Concentração | **604 (87%) são `CA1707`** — todos em `tests/`, zero em `src/` |
| Backlog real | **90 avisos em 13 regras** |
| Arquivos > 350 linhas (backend) | **4** |
| Arquivos > 350 linhas (frontend) | **0** |
| Média de linhas por arquivo | backend 87 · frontend 76 |

**`CA1707` foi desligada em `tests/`** — decisão de escopo, não dívida. Nome de
teste `Metodo_Deve_X_Quando_Y` é idiomático em xUnit e é o que o
`test-engineer` exige. "Corrigir" destruiria legibilidade para satisfazer uma
regra escrita para API pública. Em `src/` ela continua valendo — e lá já está em
zero, ou seja, **bloqueia**.

**O tier de migração warn→error** existia como problema real aqui: com
`TreatWarningsAsErrors=true`, qualquer regra nova nasceria bloqueando. O
equivalente .NET do tier "warn" do toolkit é `WarningsNotAsErrors` no
`Directory.Build.props` — a regra fica ligada e visível, sem travar o build,
enquanto a contagem for maior que zero.

> **Como promover uma regra:** zere a contagem dela, remova o ID de
> `WarningsNotAsErrors`, e ela passa a bloquear para sempre. A contagem de cada
> uma está registrada como comentário ao lado da lista, com data — é o que torna
> a dívida visível em vez de virar "depois a gente aperta isso".

**Teto de 350 linhas:** ativado no frontend (`eslint/max-lines` no oxlint), onde
a contagem já era zero — nasce bloqueando de forma legítima, que é exatamente o
critério de promoção do toolkit. Verificado que a regra realmente dispara
(baixando o teto para 50 temporariamente): nome de regra errado no oxlint é
ignorado em silêncio, e um gate silencioso é pior que gate nenhum. No backend
não existe analisador equivalente — os 4 arquivos estão registrados abaixo como
backlog medido.

**Verificação, com saída real:**

- `dotnet build -c Release`: **0 erros, 90 avisos**, `CA1707` zerado.
- `dotnet test -c Release`: **721 aprovados, 2 ignorados, 0 falhas** (723 total).
  Os 2 ignorados são os `[PostgresFact]`, nomeados no log.
- `npm run lint` e `npm run typecheck`: verdes.

Commit: `72f9ddf`.

### Etapa 6 — Parte 7: extras opcionais

**Graphify** — a CLI já estava instalada (0.9.56, via `uv`) e a skill já estava
no diretório global; faltava só construir o grafo deste repositório.

> **Desvio do doc:** o Playbook manda `graphify claude install`. Esse comando
> **não existe** na 0.9.56 — a sintaxe atual é `graphify install --platform claude`,
> e ele só copia a skill, não reescreve o `CLAUDE.md` como o doc sugere. O próprio
> toolkit avisa: "esse ecossistema muda em semanas, não em anos".

Grafo: **4.548 nós, 9.872 arestas, 232 comunidades**, de 504 arquivos. Extração
por AST, local, sem API key. Reconstruir com `graphify update .`.
`graphify-out/` é gitignored — artefato derivado, não fonte.

Consultas que já valeram o custo:

- `graphify god-nodes` — os hubs reais são `SharedKernel.Messaging` (78 arestas),
  `DomainException` (67), `Track` (52), `PagedResult` (41). Confirma que o
  acoplamento se concentra onde deveria: no SharedKernel.
- `graphify affected "Track" --depth 2` — tudo que depende de `Track` fica
  **dentro de Catalog e seus testes**. Zero vazamento cross-módulo, verificado por
  um caminho independente do ArchUnitNET.

**agent-browser** — instalado (0.37.1) com o próprio Chrome for Testing
(153.0.8010.36, isolado do Chrome do usuário). Testado contra o frontend real.

O `snapshot` (árvore de acessibilidade com refs, não screenshot) funcionou e
rendeu dois achados sem que ninguém procurasse:

1. O estado de erro da SPA está correto — "NO RESPONSE" com detalhe acionável e
   botão de retry quando a API não está no ar.
2. **Possível bug de acessibilidade:** o link de navegação sai como
   `link "Modelsoon"` — o rótulo "Model" e o badge "soon" colam sem separação.
   Leitor de tela lê "Modelsoon". **Não corrigido** (fora de escopo), registrado
   como backlog.

**Obsidian** — não instalado, e **não adotado por ora**. Decisão registrada
abaixo.

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

1. **Backlog medido, deliberadamente não corrigido** (medir e consertar são
   trabalhos separados):
   - 90 avisos de analisador em 13 regras, cada um no tier de migração.
     `CA1001` (tipo com campo `IDisposable` que não implementa `IDisposable`,
     3 ocorrências) é a de maior valor: é classe de bug, não estilo.
   - 4 arquivos de backend acima de 350 linhas:
     `GetTrackRecommendationsQueryHandlerTests.cs` (612),
     `GetTrackRecommendationsQuery.cs` (476),
     `SpotifyApiClientTests.cs` (394),
     `ImportKaggleAudioFeaturesTests.cs` (384).
     O prompt `09-file-size-refactor` é o segundo tempo disso — corta por
     responsabilidade, nunca por contagem de linha.
   - Link de navegação lido como `"Modelsoon"` pela árvore de acessibilidade
     (achado do agent-browser).
2. **Parte 4 — fluxo completo ponta a ponta.** Brainstorm → plano → ondas
   paralelas → revisão multi-agente → commit. Só fecha sobre um card real; é o
   último item, por construção.
