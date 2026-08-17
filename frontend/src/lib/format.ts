const integerFormatter = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 })

const percentFormatter = new Intl.NumberFormat('pt-BR', {
  style: 'percent',
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
})

export function formatInteger(value: number): string {
  return integerFormatter.format(value)
}

export function formatShare(part: number, whole: number): string {
  return whole === 0 ? '—' : percentFormatter.format(part / whole)
}

export function share(part: number, whole: number): number {
  return whole === 0 ? 0 : part / whole
}
