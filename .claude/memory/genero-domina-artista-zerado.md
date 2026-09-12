---
name: genero-domina-artista-zerado
description: gênero é o sinal dominante da popularidade; as colunas de popularidade/seguidores de artista estão zeradas e perseguí-las é beco sem saída
metadata:
  type: architecture
---

Achados **medidos** no catálogo real (89.740 faixas, split 71.762/17.978, seed
20260730), com ML.NET FastTree e popularidade da faixa como alvo:

- **Gênero (`audio_features.Genre`, one-hot) é o sinal dominante.** Sozinho,
  levou o modelo de MAE 15,17 / R² 0,15 — só com as 11 audio-features — para
  **MAE 11,54 / R² 0,39**. É o campeão em produção.
- **Key / Mode / TimeSignature não pagam.** Pioraram levemente o MAE (−0,12%) e
  foram reprovados pela regra de ganho mínimo de 2% por bloco. Removidos.
- **Features de artista são beco sem saída HOJE.** `catalog.artists.popularity` e
  `followers` estão **zeradas** (`is_enriched=false`): só a API do Spotify as
  preencheria, e ela não roda sobre o dataset Kaggle que populou o catálogo.

**Por que isto vira memória:** uma coluna de zeros não se anuncia. Uma sessão
futura olhando o schema vê `artists.popularity` e conclui, razoavelmente, que ali
há sinal a explorar — e gasta um ciclo inteiro treinando contra zeros antes de
descobrir. Isso é dívida técnica registrada, não uma pista.

Existe um proxy possível sem a API — frequency-encoding do id do artista, ou
número de faixas — mas ele exige **split agrupado por artista** para não vazar
alvo. Isso é um card-spike próprio, nunca uma emenda no pipeline existente.

Nota de interpretação: só com audio-features puras o modelo converge para a média
e erra os extremos. Isso é comportamento **correto** de um modelo com pouco sinal,
não bug do serving.
