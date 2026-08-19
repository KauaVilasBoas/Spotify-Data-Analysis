export const CHART_COLORS = {
  brand: '#1ed760',
  brandDim: 'rgba(30, 215, 96, 0.28)',
  axis: '#8a93a3',
  split: 'rgba(35, 40, 51, 0.85)',
  text: '#a7b0bf',
  positive: '#1ed760',
  negative: '#f2545b',
} as const

export const CHART_TEXT_STYLE = {
  fontFamily: 'Figtree, ui-sans-serif, system-ui, sans-serif',
  fontSize: 11,
  color: CHART_COLORS.text,
} as const

export const CHART_TOOLTIP = {
  backgroundColor: '#171b23',
  borderColor: '#333a48',
  borderWidth: 1,
  padding: [8, 12] as [number, number],
  textStyle: { color: '#f4f6f8', fontSize: 12, fontFamily: CHART_TEXT_STYLE.fontFamily },
  extraCssText: 'border-radius:10px;box-shadow:0 8px 24px rgb(0 0 0 / .5);',
} as const
