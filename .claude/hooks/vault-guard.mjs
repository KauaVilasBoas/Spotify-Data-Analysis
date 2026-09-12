#!/usr/bin/env node
// Hook PreToolUse — matcher: Read|Grep|Glob|Write|Edit
//
// O vault de longo prazo e MCP-only. Este hook bloqueia acesso DIRETO a arquivo
// dentro dele. Sem isso, a regra "sempre use as ferramentas MCP" e so convencao de
// boa vontade — facil de esquecer no meio de uma sessao, sob pressao de contexto.
//
// Por que MCP-only importa: toda escrita via MCP passa por validacao de frontmatter.
// Escrita direta em arquivo pula essa validacao, e o vault vira "mais uma pilha de
// notas que sai do padrao com o tempo" — exatamente o que ele existe para evitar.
//
// ADAPTACAO em relacao ao doc do toolkit: la o vault mora DENTRO do projeto, e o
// teste e um `path.includes("vault/")`. Aqui ele mora fora do repositorio e em
// caminho Windows, entao o teste normaliza separador (\ -> /) e caixa antes de
// comparar — senao `C:\Projetos\SpotifyDataAnalysis-vault\...` passaria batido.
//
// DISCIPLINA: falha ABERTO. Sem nada para analisar, o hook permite. Ele existe para
// impedir mau habito, nao para travar a sessao quando ele proprio tem bug.

import { parseHookEvent, readStdinRaw } from "./hook-io.mjs";

// Marcador do vault. Vem do ambiente quando definido; o literal e o fallback para
// que o hook continue protegendo mesmo se a variavel nao estiver setada.
const MARCADOR = (process.env.SPOTIFY_VAULT_PATH ?? "SpotifyDataAnalysis-vault")
  .replace(/\\/g, "/")
  .toLowerCase();

const normalizar = (p) => String(p ?? "").replace(/\\/g, "/").toLowerCase();

function main() {
  const event = parseHookEvent(readStdinRaw());
  if (event === null) {
    return; // nada para analisar — falha aberta
  }

  const entrada = event.tool_input ?? {};
  // Um Grep pode carregar o caminho em `path` e o alvo em `pattern`; um Glob, em
  // `pattern`. Checar todos: escapar por um campo nao inspecionado e o buraco obvio.
  const alvos = [entrada.file_path, entrada.path, entrada.pattern, entrada.notebook_path]
    .map(normalizar)
    .filter(Boolean);

  if (!alvos.some((a) => a.includes(MARCADOR))) {
    return; // nao e o vault — permite
  }

  // Excecao estreita: leitura da pasta diaria. Somente leitura, de proposito —
  // liberar escrita direta em daily/ derrubaria a validacao de frontmatter tambem la.
  //
  // O `(\/|$)` nao e enfeite: um Grep aponta para o DIRETORIO (".../daily", sem barra
  // final), e um teste por "/daily/" bloquearia justamente o caso que a excecao existe
  // para permitir. Bug encontrado no teste dos 10 casos, nao em producao.
  const soLeitura = ["Read", "Grep", "Glob"].includes(event.tool_name);
  if (soLeitura && alvos.some((a) => /\/daily(\/|$)/.test(a))) {
    return;
  }

  console.error(
    "O vault de longo prazo e MCP-only. Use as ferramentas MCP do mcpvault, nao acesso " +
      "direto a arquivo — escrita direta pula a validacao de frontmatter. " +
      "Se faltar uma ferramenta MCP para o que voce precisa, isso e um pedido de ferramenta, " +
      "nao motivo para contornar a regra. (Excecao: leitura de daily/.)",
  );
  process.exit(2); // bloqueia; a mensagem acima vai para o agente
}

try {
  main();
} catch {
  // Bug proprio nao pode travar a sessao.
}

process.exit(0);
