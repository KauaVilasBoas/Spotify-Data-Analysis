# Sistema visual — Spotify Data Analysis

Contrato do front. As telas de Catálogo (E5.2), Modelo (E5.3) e as seguintes reusam este
vocabulário; nenhuma delas deve introduzir paleta, fonte, raio ou estado de carregamento próprios.

> **Reversão registrada.** A primeira versão deste documento (E5.1) escolheu um verde-limão
> `#c8f24e` **deliberadamente afastado** da marca do Spotify, cantos retos e proibição explícita de
> sombras e gradientes. **O usuário reverteu essa direção.** O sistema atual é o oposto em quase
> tudo: verde oficial `#1DB954`, elevação por sombra, cantos suaves, movimento generoso. Isto está
> escrito aqui para que ninguém "conserte" o sistema de volta ao anterior achando que é regressão.

## Conceito

**Spotify-native elevado.** Deve parecer uma ferramenta interna de dados do próprio Spotify — não um
dashboard genérico e não uma imitação irônica. Fundo quase preto azulado, superfícies que sobem por
sombra e não por borda, verde de marca usado como sinal (nunca como enfeite), números enormes que
contam a história sozinhos, e um `⌘K` que assume que o usuário é profissional.

Três decisões carregam a identidade:

1. **O número é o herói.** `89,740` ocupa a dobra inteira. Rótulo é legenda, não protagonista.
2. **Capa gerada.** O dataset não tem imagem nenhuma (ver seção Arte de faixa). Em vez de placeholder
   cinza, cada faixa vira uma imagem única e determinística.
3. **Movimento com propósito.** Entrada escalonada (`animate-rise`), barras que crescem
   (`animate-sweep`), números que rolam (`NumberFlow`). Nada pisca sem motivo.

## As três regras de domínio (herdadas — não são estética)

Estas sobrevivem a qualquer troca de paleta. Quebrá-las é defeito, não escolha de gosto.

1. **Medido, imputado e ausente nunca compartilham cor nem rótulo.** Tokens `--measured`,
   `--imputed`, `--absent` são distintos e cada segmento carrega o próprio nome na legenda.
   Implementado em `components/data/CoverageBar.tsx`.
2. **Percentual nunca aparece sozinho.** Sempre acompanhado do valor absoluto e da base
   (`100.0%` + `89,740 of 89,740 tracks`). Helper: `formatShareWithBase` em `lib/format.ts`.
3. **Nenhuma tela escreve seu próprio loading, empty ou error.** Tudo passa por
   `components/feedback/ResourceBoundary.tsx`, que é o único ponto que mapeia
   `ResourceState<T>` para `LoadingState` / `ErrorState`.

## Paleta

Tokens em `src/index.css` (`:root`), expostos ao Tailwind por `@theme inline`.

| Token             | Valor     | Uso                                                     |
| ----------------- | --------- | ------------------------------------------------------- |
| `--void`          | `#06070a` | Fundo da aplicação.                                     |
| `--surface-0..3`  | `#0a0c10` → `#1f242e` | Escada de elevação dos painéis.             |
| `--line`          | `#232833` | Divisória padrão.                                       |
| `--line-strong`   | `#333a48` | Contorno de controles.                                  |
| `--text`          | `#f4f6f8` | Texto primário.                                         |
| `--text-dim`      | `#a7b0bf` | Prosa, texto secundário.                                |
| `--text-faint`    | `#8a93a3` | Rótulos micro e notas de rodapé.                        |
| `--brand`         | `#1DB954` | Verde oficial do Spotify. Sinal, nunca decoração.       |
| `--brand-bright`  | `#1ed760` | Estado ativo, foco, destaque sobre fundo escuro.        |
| `--measured`      | `#1ed760` | **Feature medida.**                                     |
| `--imputed`       | `#f0a742` | **Feature imputada.** Nunca decoração.                  |
| `--absent`        | `#3d4552` | **Feature ausente.** Nunca decoração.                   |
| `--danger`        | `#f2545b` | Erro e correlação negativa.                             |

**Regra de cor:** o verde é escasso. Se mais de ~10% da tela estiver em `--brand`, ele deixou de ser
sinal. `--imputed` e `--absent` são reservados à proveniência do dado.

## Contraste — medido, não suposto

Razões WCAG calculadas sobre os fundos reais do sistema:

| Cor              | sobre `--void` | `--surface-1` | `--surface-2` | `--surface-3` |
| ---------------- | -------------- | ------------- | ------------- | ------------- |
| `--text`         | 18.59          | 17.17         | 15.92         | 14.36         |
| `--text-dim`     | 9.21           | 8.51          | 7.89          | 7.11          |
| `--text-faint`   | 6.51           | 6.01          | 5.57          | 5.02          |
| `--brand`        | 7.79           | 7.19          | 6.67          | 6.01          |
| `--brand-bright` | 10.50          | 9.69          | 8.99          | 8.11          |

Todos passam AA (≥ 4.5) para texto pequeno.

> **`--text-faint` foi corrigido neste card.** O valor original `#6b7484` dava 4.27 sobre `--void` e
> **3.30** sobre `--surface-3` — reprovava AA em texto pequeno, justamente onde mais se usa (rótulos
> micro e rodapés). Passou para `#8a93a3`.

> **`#1DB954` sobre fundo claro reprova AA: 2.59.** Este sistema é dark-only, então o verde só aparece
> sobre `--void`/`--surface-*`, onde passa com folga. **Qualquer tela futura em fundo claro não pode
> usar `--brand` em texto pequeno.**

## Tipografia

Duas famílias, carregadas por `<link>` do Google Fonts (custo zero no bundle JS).

| Papel        | Família            | Onde                                                 |
| ------------ | ------------------ | ---------------------------------------------------- |
| Display / UI | **Figtree**        | Tudo. Pesos 400–900; os números grandes usam 800.    |
| Mono         | **JetBrains Mono** | Rótulos micro, métricas, ids, traceId, atalhos.      |

Utilitários em `index.css`:

- `.numeral` — números grandes: peso 800, `tabular-nums`, tracking negativo, leading 0.92.
- `.label-micro` — mono, 11px, uppercase, tracking 0.12em, `--text-faint`.

`font-variant-numeric: tabular-nums` é global no `body`: número em tabela não pode dançar.

## Superfície, elevação e raio

- `.surface-card` — gradiente sutil + borda `--line` + `--elev-2`. É o painel padrão (`Panel.tsx`).
- `.surface-inset` — fundo rebaixado para blocos dentro de um painel.
- `.aurora` / `.grain` — atmosfera de fundo, fixos no `AppShell`, `pointer-events: none`.
- Raio: `--radius-sm` 0.625rem · `--radius` 1rem · `--radius-lg` 1.5rem. Cantos suaves são regra.

## Movimento

`--ease-out: cubic-bezier(0.16, 1, 0.3, 1)` é a curva do sistema.

| Utilitário          | Uso                                                        |
| ------------------- | ---------------------------------------------------------- |
| `animate-rise`      | Entrada de painel/linha, com `animation-delay` escalonado.  |
| `animate-sweep`     | Barra que cresce da esquerda.                               |
| `animate-pulse-dot` | Indicador de atividade.                                     |
| `skeleton`          | Shimmer de carregamento.                                    |

**`prefers-reduced-motion: reduce` zera toda animação e transição** num bloco global no fim do
`index.css`. Nenhum componente pode reintroduzir movimento fora desse guarda-chuva.

## Arte de faixa (capas geradas)

**Descoberta que motivou a decisão:** a tabela `catalog.albums` **não tem coluna de imagem nenhuma**, e
`is_enriched = false` em **57.637 de 57.637** álbuns — o enriquecimento pela API do Spotify nunca
rodou. Não existe capa para exibir, e nunca existiu.

Em vez de placeholder cinza, `lib/track-art.ts` deriva uma imagem única e determinística por faixa:

- Hash FNV-1a do `trackId` semeia um xorshift.
- Quando a faixa traz `audioFeatures` (endpoint de detalhe), as **features reais** mandam:
  lóbulos por `danceability`, amplitude por `energy`, matiz por `key` + `valence`, rotação por
  `tempo`, brilho por `loudness`.
- Quando não traz (endpoints de lista só devolvem identidade), o xorshift **sintetiza** o vetor, para
  a arte continuar única e estável por faixa.

SVG puro, sem rede, sem canvas. O copy na tela **não promete** que a arte veio só de features — diz
que vem "da identidade e do perfil de áudio da faixa", que é o que de fato acontece.

## Idioma

**Todo o front é em inglês** — copy, labels, erros, estados vazios, `aria-label`. Números em `en-US`
(`89,740`, `100.0%`) via `lib/format.ts`. Documentação, commits e comentários seguem em português.

## Marca e atribuição

- O logo do Spotify é o oficial (`components/brand/SpotifyMark.tsx`), proporção original, `#1DB954`,
  sem deformar e com clear space respeitado.
- O rodapé (`SiteFooter.tsx`) declara, sem rodeio: o dado vem do **dataset público do Kaggle**,
  **nunca da API do Spotify**; Spotify é marca da **Spotify AB**; o projeto **não é afiliado**.
- **Nada na interface pode sugerir "Powered by Spotify".**

## Orçamento de bundle

Demo em free tier com spin-down — o primeiro acesso já paga cold start.

- **Teto: 350 kB gzip inicial.**
- ECharts é registrado módulo a módulo (`BarChart`, `GridComponent`, `TooltipComponent`,
  `CanvasRenderer`) em `components/charts/EChart.tsx` e carregado por `lazy()`. Nunca importe o
  barril `echarts`.
- Medição atual: **126 kB gzip** de JS inicial + 7.8 kB de CSS. O chunk do ECharts (165 kB gzip) só
  entra quando um gráfico monta.

## Camada de dados

- `api/http-client.ts` é a **única** saída HTTP. Desembrulha `ApiResult<T>` e converte qualquer falha
  em `ApiError`. Nenhuma tela lê `.data.data` nem inspeciona `response.status`.
- `api/queries.ts` expõe um hook tipado por endpoint sobre TanStack Query e **devolve
  `ApiResource<T>`** — a ponte que mantém o `ResourceBoundary` como único ponto de render de
  loading/erro.
- **Contratos confirmados contra a API viva, não deduzidos do nome.** Armadilhas encontradas:
  - `/api/tracks` filtra por **`search`**; `searchTerm` é ignorado silenciosamente e devolve o
    catálogo inteiro.
  - Os endpoints de insights paginam por `page`/`pageSize`. **Não existe `limit`.**
  - `/api/insights/artists` devolve **lista vazia** sem `includeUnenriched=true`.
