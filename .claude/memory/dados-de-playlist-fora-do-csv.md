---
name: dados-de-playlist-fora-do-csv
description: o dataset.csv não tem playlist nenhuma, então co-ocorrência é zero — e a fonte externa identifica faixa por nome+artista, que o catálogo hoje não guarda
metadata:
  type: architecture
---

**O fato não óbvio:** o `dataset.csv` do projeto (Kaggle "Spotify Tracks
Dataset") é **uma linha por faixa** — `track_id`, audio-features,
`track_genre` — **sem nenhuma coluna ou agrupamento de playlist**. Foi ele que
populou as ~89,7k faixas do catálogo.

Consequência direta: o recomendador **content-based roda 100% só com o CSV**, mas
o **colaborativo não tem insumo nenhum** ali. Co-ocorrência de faixas no CSV é
exatamente zero. Um plano de item-item que assuma o contrário nasce morto.

**A fonte de playlists evoluiu — e a primeira escolha não existe mais.** O
Spotify Million Playlist Dataset (MPD) **saiu do ar**: a AIcrowd removeu e manda
contatar a Spotify Research. Cuidado com falsos positivos na busca — os datasets
"million song" no HuggingFace são de **letras**, não de playlists, e o subset
20k-semantic jogou fora o mapeamento playlist→faixa. A fonte adotada em
2026-08-04 é o **"Spotify Playlists" de Pichl et al.** (Kaggle
`andrewmvd/spotify-playlists`, ~1,2 GB, colunas `user_id` / `playlistname` /
`artistname` / `trackname`, ~12M pares), baixável sem portão de termos. Ingerir
playlists pela API do Spotify foi descartado: traria poucas, com densidade magra.

**A pegadinha da reconciliação.** O Pichl identifica faixa por **nome + artista**,
não por `track_id`. O projeto já tem a máquina exata para isso: o VO
`TrackMatchKey` ("artista|título" normalizado — corta sufixo editorial, acento,
parênteses; `FromArtistList` lê a coluna `artists` separada por `;`).

**Mas:** o seeder que populou o catálogo gravou as faixas **sem nome de artista**
(`artists: Array.Empty`), então o `match_key` persistido hoje é **só o título** —
casar por ele colidiria em massa. O `dataset.csv` original **tem** a coluna
`artists`; a reconciliação precisa reconstruir `track_id → "artista|título"` a
partir do CSV, não a partir do que está no banco.

Nota de volume, para quando isso for implementado: `catalog.playlists` guarda
`track_ids` como jsonb sem FK. Co-ocorrência em escala provavelmente pede tabela
de pares materializada, não self-join sobre `jsonb_array_elements` por request.
