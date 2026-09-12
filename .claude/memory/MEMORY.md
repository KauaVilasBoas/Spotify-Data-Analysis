# Índice de memória — SpotifyDataAnalysis

> Uma linha por memória. Leia inteiro no começo da sessão — ele é pequeno de
> propósito. As regras de uso estão em [INSTRUCTIONS.md](INSTRUCTIONS.md).
>
> Teto: **130 linhas não vazias**. Acima disso, rode a política de crescimento
> antes de adicionar qualquer entrada nova.

- [Stack é 100% .NET, por decisão](stack-100-porcento-dotnet.md) — propor Python, notebook ou serviço auxiliar reabre uma decisão fechada que é o diferencial do case.
- [Autor e email dos commits](git-autor-e-email.md) — é o git do Kauã: nome fixo, email vem do `git config` dele, zero co-author, histórico antigo não se reescreve.
- [VO com ToJson precisa de setter privado](ef-tojson-setter-privado.md) — propriedade get-only gravou `{}` no jsonb em todas as faixas por dois épicos, sem erro e sem teste vermelho.
- [Teste verde do read-side não prova nada](read-side-count-over-bigint.md) — 18 testes passando com o endpoint dando 500 em 100% das chamadas; o teste de contrato só afirma o texto do SQL.
- [Gênero domina; artista está zerado](genero-domina-artista-zerado.md) — `artists.popularity`/`followers` são colunas de zeros, e treinar contra elas queima um ciclo inteiro.
- [Playlist não está no dataset.csv](dados-de-playlist-fora-do-csv.md) — co-ocorrência é zero no CSV, o MPD saiu do ar, e a fonte adotada casa faixa por nome+artista que o catálogo hoje não guarda.
- [Planejamento mora fora do git](planejamento-mora-fora-do-git.md) — `docs/` é gitignored de propósito; plano, pendências e backlog vivem em PLANO.md, PENDENCIAS.md e no Trello.
