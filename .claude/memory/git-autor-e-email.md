---
name: git-autor-e-email
description: o autor dos commits é Kauã Vilas Boas e o email vem do git config dele — não sobrescrever, não reescrever histórico antigo
metadata:
  type: feedback
---

Os commits deste repositório já saíram, no começo, com autor **`spotify-dev`** —
a config local do repo tinha `user.name=spotify-dev`. O usuário cobrou
explicitamente a correção: é o **git dele**, não do agente.

**O que é invariável:** `user.name` = `Kauã Vilas Boas`, nunca um nome de agente,
e **nunca** o trailer `Co-Authored-By` (a proibição em si está no `CLAUDE.md`; o
que mora aqui é o histórico de por que ela é cobrada).

**O email mudou ao longo do projeto — não force um valor fixo:**

- E0–E2 (jul/2026): `vilasboaskaua73@gmail.com`.
- A partir do E3 (2026-07-30): ele mudou o `git config` local **e** global para
  `kauacaldeira@hotmail.com` e, perguntado, confirmou manter.

**Regra prática:** use o email que estiver no `git config`. Nunca sobrescreva.
Garanta apenas o `user.name` correto e zero co-author. Confira com
`git log -1 --format="%an <%ae>%n%b"`.

**Não reescreva histórico antigo** para uniformizar. Emails diferentes fragmentam
a atribuição no GitHub (o hotmail pode não estar ligado à conta dele) — isso é
uma consequência conhecida e aceita, não um problema a corrigir por conta própria.
