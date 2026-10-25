# Regras da memória do projeto

> Carregado em toda sessão via `CLAUDE.md`. Se estas regras não forem lidas
> automaticamente, a memória depende de alguém lembrar de abrir o arquivo — e aí
> não é memória automática.

## Estrutura

- **`MEMORY.md`** — o índice, uma linha por memória, apontando para o arquivo de tópico.
- **Um arquivo por memória**, nesta pasta, com frontmatter:

```markdown
---
name: slug-em-kebab-case
description: resumo de uma linha — é o que decide relevância numa sessão futura
metadata:
  type: feedback | architecture | business-rule | reference
---
```

`type` tem **exatamente quatro valores**. Não invente um quinto — se uma entrada não
cabe em nenhum, isso é sinal de que ela talvez não devesse virar memória.

- **`feedback`** — um erro que precisou de correção durante uma sessão.
- **`architecture`** — um padrão descoberto só depois de tentativas que falharam.
- **`business-rule`** — algo que afeta o código mas não é óbvio só de ler ele.
- **`reference`** — onde uma informação externa mora.

## Critério de salvamento

Antes de escrever uma entrada, aplique o teste literal:

> Uma sessão futura ficaria surpresa e grata de saber disso antes de começar,
> em vez de descobrir do jeito difícil?

Se a resposta for não, **não salve**. Isso exclui explicitamente:

- Qualquer coisa derivável de ler o código ou o histórico do git.
- Prazo, motivação ou contexto temporário do momento.
- Receita de debug — isso mora na mensagem do commit.
- **Qualquer coisa já documentada no `CLAUDE.md` ou num arquivo de `.claude/agents/`.**
  Duplicar ali cria duas fontes da verdade que divergem.

Erre para o lado de **não** salvar. Um índice pequeno e de alto sinal vence um
índice grande e ignorado, sempre.

### A divisão de trabalho neste projeto

Regra operacional acionável no momento do trabalho → vai para o arquivo do
especialista em `.claude/agents/`. A **evidência** que sustenta a regra (o que
quebrou, quanto custou, por que o teste verde não pegou) → vai para cá. Uma
entrada daqui pode apontar para o agente que aplica a regra; o contrário não.

## Política de crescimento

Antes de adicionar entrada nova, conte as linhas **não vazias** do `MEMORY.md`.
Abaixo de **130**, só adicione. Acima, sanitize primeiro:

1. Pontue cada entrada existente: recência × especificidade × chance de evitar um
   erro real no futuro.
2. Para cada entrada de baixa pontuação, **migre — nunca simplesmente apague** —
   nesta ordem exata:
   1. **Dedup** — procure o mesmo assunto no destino; estenda a nota existente em
      vez de criar duplicata.
   2. **Adeque ao template do destino.**
   3. **Crie** a nota no destino.
   4. **Confirme** lendo a nota de volta. Sem leitura confirmada, a migração não
      aconteceu.
   5. **Só então** apague o arquivo de tópico e a linha dele no `MEMORY.md`.
3. Reescreva o índice com o que sobrou.
4. Só depois adicione a entrada nova.

Apagar antes da leitura de volta é **perda de dado**, não faxina. Na dúvida sobre
se uma entrada ainda merece o lugar dela, deixe — migrar depois não custa nada.

## Camada 2 — armazenamento de longo prazo

**Vault dedicado em `C:\Projetos\SpotifyDataAnalysis-vault`** (fora do repositório e
fora do OneDrive), acessado **exclusivamente** pelas ferramentas MCP do servidor
`vault` (`mcpvault`). Estrutura PARA: `01-projetos/`, `02-areas/`,
`03-conhecimento/`, `04-referencia/`, `daily/`, `templates/`.

**Nunca leia nem escreva esses arquivos direto.** Um hook de `PreToolUse`
(`vault-guard.mjs`) bloqueia `Read`, `Grep`, `Glob`, `Write` e `Edit` apontados
para lá — a única exceção é **leitura** de `daily/`. Escrita direta pularia a
validação de frontmatter, que é a razão de o vault existir em vez de uma pasta
solta de notas.

Se faltar uma ferramenta MCP para o que você precisa, isso é um pedido de
ferramenta — não motivo para contornar o bloqueio.

**Destino por tipo, ao migrar da camada 1:**

| `type` da camada 1 | Pasta de destino |
|---|---|
| `architecture`, `business-rule`, `feedback` | `03-conhecimento/` |
| `reference` | `04-referencia/` |

Antes de criar, leia o template da pasta de destino (`templates/<pasta>.md`) e
siga as seções na ordem. **Ressalva:** o `mcpvault` valida frontmatter, mas **não**
impõe as seções do template — isso é convenção que você cumpre, não regra que a
ferramenta garante.
