# Sistema visual — Spotify Data Analysis

Entregável do card E5.1. As telas de Catálogo (E5.2), Insights (E5.3) e as seguintes reusam este
vocabulário; nenhuma delas deve introduzir paleta, fonte ou raio próprios.

## Conceito

**Instrumento de laboratório editorial.** Um painel de medição, não um dashboard de marketing: fundo
de tinta quente quase preto, filetes finos em vez de cartões flutuantes, rótulos em monoespaçada
maiúscula como em escala de equipamento, e números grandes numa serifa com personalidade. O acento é
um verde-limão ácido — deliberadamente deslocado do verde da marca do Spotify, para o painel parecer
um instrumento sobre os dados, não uma imitação do produto.

A regra que sustenta o conceito: **medido e imputado nunca se misturam na tela**, do mesmo jeito que
não se misturam na API. Cor, rótulo e nota de rodapé sempre dizem qual é qual.

Diretriz permanente: sombras suaves, gradientes roxos, cantos muito arredondados e grades de cartões
iguais estão fora. A hierarquia vem de escala tipográfica, filete e espaço negativo.

## Paleta

Tokens em `src/index.css` (`:root`), expostos ao Tailwind por `@theme inline`.

| Token                    | Valor     | Uso                                                       |
| ------------------------ | --------- | --------------------------------------------------------- |
| `--ink`                  | `#0b0c09` | Fundo da aplicação.                                       |
| `--ink-raised`           | `#121410` | Painéis sobre o fundo.                                    |
| `--ink-sunken`           | `#070805` | Barra de conexão, rail, trilhos e fundo do documento.     |
| `--hairline`             | `#22261b` | Filete padrão, divisórias.                                |
| `--hairline-strong`      | `#363b2b` | Filete de contorno de controles e segmento neutro.        |
| `--bone`                 | `#eae7da` | Texto primário.                                           |
| `--bone-dim`             | `#a3a292` | Texto secundário, prosa.                                  |
| `--bone-faint`           | `#6b6c5e` | Rótulos micro, notas de rodapé.                           |
| `--acid`                 | `#c8f24e` | Acento único: estado ativo, valor medido, foco, seleção.  |
| `--acid-deep` / `-shadow`| —         | Anel de foco e texto sobre acento.                        |
| `--clay`                 | `#e2714a` | Erro e **valor imputado** — nunca decoração.              |
| `--mist`                 | `#7fb8a8` | Reserva para uma terceira série em gráficos (E5.3).       |

Os tokens do shadcn (`--background`, `--primary`, `--destructive`, …) são apelidos destes; alterar a
paleta é mexer só no bloco `:root`.

**Regra de cor:** o acento é escasso. Se mais de ~10% da tela estiver em `--acid`, ele deixou de ser
acento. `--clay` é reservado a erro e a imputação — usar clay como enfeite quebra a leitura.

## Tipografia

Três famílias variáveis, carregadas por `<link>` do Google Fonts (custo zero no bundle JS).

| Papel                  | Família            | Onde                                                          |
| ---------------------- | ------------------ | ------------------------------------------------------------- |
| Display / números      | **Fraunces**       | Títulos e todo numeral grande. Eixos `SOFT`/`WONK` ligados.   |
| Interface / prosa      | **Archivo**        | Corpo, descrições, botões.                                    |
| Dados / rótulos        | **JetBrains Mono** | Rótulos micro, endpoints, `traceId`, valores brutos da API.   |

Utilitários do próprio projeto (`@utility` em `src/index.css`):

- `.label-micro` — mono 10px, `tracking .14em`, maiúscula, `--bone-faint`. É a etiqueta de tudo.
- `.numeral` — Fraunces com `SOFT 20 / WONK 1` e `tabular-nums`. Todo número que o usuário compara.
- `.display-wonk` — Fraunces com `SOFT 40 / WONK 1`. Títulos de seção e de painel.

Escala de título: `text-[clamp(2.25rem,5vw,3.5rem)]` para o título da página; o número herói usa
`clamp(4.5rem,13vw,9rem)`. `font-variant-numeric: tabular-nums` é global, para colunas de números não
dançarem.

Números são formatados em pt-BR por `src/lib/format.ts` (`89.740`, `100,0%`) — nunca por `toString()`.

## Densidade e forma

- **Raio 4px** (`--radius`). Botões e badges recebem `rounded-none` na aplicação: canto reto é parte
  da identidade. O default arredondado do shadcn é sempre sobrescrito.
- **Filete, não sombra.** Nenhuma `box-shadow` de elevação. Separação por `border-hairline`.
- `Panel` é a superfície padrão: contorno de 1px, cabeçalho com `.label-micro` à esquerda e metadado
  em mono à direita, e marcas de canto em `--acid` (`::before`/`::after`) que dão o ar de instrumento.
- Ritmo vertical: `space-y-12` entre blocos de página, `space-y-4/5` dentro de um painel.
- Largura de leitura sempre limitada por `max-w-prose`; números e painéis podem ocupar a largura toda.

## Textura

- `.surface-grain` — grão de filme por `feTurbulence` em data-URI, `opacity .055`, `mix-blend-overlay`,
  aplicado uma vez no shell. Tira o aspecto de fundo chapado sem custar requisição.
- `.hatched` — hachura diagonal a 45°. É **o sinal de "em construção"**: rail de navegação e páginas
  de seção planejada. Não usar para outra coisa.
- `TickRule` — régua de traços sob títulos e sob o número herói.
- A barra de cobertura leva uma sobreposição de traços verticais, para ler como escala de medição e
  não como bloco de cor.

## Movimento

CSS puro; nenhuma biblioteca de animação entra no bundle.

| Animação             | Onde                                                            |
| -------------------- | --------------------------------------------------------------- |
| `.animate-rise`      | Entrada de blocos de conteúdo, escalonada por `animationDelay`. |
| `.animate-sweep`     | Segmentos da barra de cobertura crescendo da esquerda.          |
| `.animate-pulse-dot` | Indicador de conexão ao vivo.                                   |
| `.animate-ticker`    | Varredura do estado de carregamento.                            |

Curva padrão `cubic-bezier(.16,1,.3,1)`, 620–900ms na entrada. `prefers-reduced-motion` desliga tudo
globalmente — já está no `index.css`, não repetir por componente.

## Componentes base

| Componente         | Papel                                                                              |
| ------------------ | ---------------------------------------------------------------------------------- |
| `Panel`            | Superfície padrão com eyebrow e metadado. Toda seção de conteúdo vive dentro de um. |
| `TickRule`         | Régua decorativa de separação.                                                      |
| `MetricTile`       | Rótulo + numeral + nota. Métrica secundária.                                        |
| `CoverageBar`      | Barra empilhada + legenda com valor absoluto, percentual e nota por segmento.        |
| `LoadingState`     | Carregamento. Nunca improvisar spinner.                                             |
| `EmptyState`       | Vazio, com hachura, explicação e ação opcional.                                      |
| `ErrorState`       | Erro: manchete em pt-BR + evidência crua do ProblemDetails + próximo passo.          |
| `ResourceBoundary` | Ponte entre `useApiResource` e os três estados acima.                                |

**Regra dura:** uma tela nova nunca escreve o seu próprio "carregando…", "nada aqui" ou "deu erro".
Ela envolve a leitura num `ResourceBoundary` e escreve só o caso de sucesso.

## Honestidade do dado na tela

A API distingue medido, imputado e ausente, e devolve `warnings` e `isImputed` em vários endpoints. A
interface tem que preservar isso:

- Valor **medido** → `--acid`.
- Valor **imputado** → `--clay`, sempre com nota dizendo que foi preenchido.
- Valor **ausente** → `--hairline-strong`, contado e rotulado, nunca omitido.
- Percentual nunca aparece sozinho: sempre ao lado do valor absoluto e da base.

`CoverageBar` já implementa esse contrato de leitura e é o modelo para os recortes do E5.3.

## Acessibilidade

- Foco visível global: contorno de 2px em `--acid` com `outline-offset: 2px`. Não remover.
- A hachura de "em construção" é decorativa (`aria-hidden`) e acompanhada de texto em `sr-only`.
- Estados anunciados: carregamento com `role="status"` + `aria-busy`, erro com `role="alert"`.
- A barra de cobertura expõe `role="img"` com `aria-label` descrevendo cada segmento.
- Contraste do texto primário (`--bone` sobre `--ink`) e do acento sobre o fundo passa AA.
