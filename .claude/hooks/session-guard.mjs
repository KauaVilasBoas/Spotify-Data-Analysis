// Hook de SessionStart: abre a sessao com o contexto de git que sempre e perguntado
// nos primeiros minutos, e com o unico alarme que este repositorio ja pagou caro para
// aprender — arquivo-fonte escondido por padrao de .gitignore sem ancora.
//
// Historia: o padrao `models/` (sem barra inicial) casava com QUALQUER pasta Models/ da
// solucao, nao so a de artefatos de ML na raiz. O agregado ModelVersion e companhia
// ficaram fora do commit, e a `main` deixou de compilar a partir de clone limpo. O build
// local continuava verde, porque os arquivos existiam no disco. So o CI pegou.
//
// DISCIPLINA: este hook FALHA ABERTO. Qualquer erro aqui termina com exit 0 e sem saida.
// Um hook que trava a sessao inteira por causa de um bug proprio e pior que nenhum hook.

import { execFileSync } from "node:child_process";
import { parseHookEvent, readStdinRaw } from "./hook-io.mjs";

function git(args) {
  return execFileSync("git", args, {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"],
    timeout: 5000,
  });
}

function main() {
  // parseHookEvent devolve null tanto para JSON invalido quanto para o literal "null" —
  // `JSON.parse("null")` nao lanca, e e assim que uma guarda ingenua passa batido.
  const event = parseHookEvent(readStdinRaw());
  if (event === null) {
    return;
  }

  const branch = git(["branch", "--show-current"]).trim();
  const sujos = git(["status", "--porcelain"]).split("\n").filter(Boolean).length;

  const linhas = [`branch ${branch || "(destacado)"} · ${sujos} arquivo(s) modificado(s)`];

  // O alarme: fonte C# ignorada fora de bin/obj nunca e intencional neste repositorio.
  const escondidos = git(["ls-files", "--others", "--ignored", "--exclude-standard", "--", "src", "tests"])
    .split("\n")
    .filter((p) => /\.(cs|csproj)$/.test(p) && !/\/(bin|obj)\//.test(p));

  if (escondidos.length > 0) {
    linhas.push(
      `ALERTA: ${escondidos.length} arquivo(s) de codigo ignorado(s) pelo .gitignore fora de bin/obj.`,
      `Um clone limpo nao compila. Confira antes de commitar:`,
      ...escondidos.slice(0, 10).map((p) => `  ${p}`),
    );
  }

  process.stdout.write(linhas.join("\n") + "\n");
}

try {
  main();
} catch {
  // Silencio proposital: fora de um repositorio git, sem git no PATH, timeout — nada disso
  // justifica atrapalhar a sessao. O hook e conveniencia, nunca pre-requisito.
}

process.exit(0);
