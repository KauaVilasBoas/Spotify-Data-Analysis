const LOCALE = 'en-US'

const integerFormatter = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 })

const compactFormatter = new Intl.NumberFormat(LOCALE, {
  notation: 'compact',
  maximumFractionDigits: 1,
})

const percentFormatter = new Intl.NumberFormat(LOCALE, {
  style: 'percent',
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
})

const dateFormatter = new Intl.DateTimeFormat(LOCALE, {
  year: 'numeric',
  month: 'short',
  day: '2-digit',
})

export function formatInteger(value: number): string {
  return integerFormatter.format(value)
}

export function formatCompact(value: number): string {
  return compactFormatter.format(value)
}

export function formatDecimal(value: number, fractionDigits = 2): string {
  return new Intl.NumberFormat(LOCALE, {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  }).format(value)
}

export function formatShare(part: number, whole: number): string {
  return whole === 0 ? '—' : percentFormatter.format(part / whole)
}

export function share(part: number, whole: number): number {
  return whole === 0 ? 0 : part / whole
}

/**
 * A percentage never travels alone: the design system requires the absolute value and the
 * base alongside every share, so the reader can always audit the denominator.
 */
export function formatShareWithBase(part: number, whole: number): string {
  return `${formatShare(part, whole)} · ${formatInteger(part)} of ${formatInteger(whole)}`
}

export function formatDuration(milliseconds: number): string {
  const totalSeconds = Math.round(milliseconds / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60

  return `${minutes}:${seconds.toString().padStart(2, '0')}`
}

export function formatSignedDecimal(value: number, fractionDigits = 3): string {
  const rendered = formatDecimal(Math.abs(value), fractionDigits)

  if (value > 0) {
    return `+${rendered}`
  }

  return value < 0 ? `−${rendered}` : rendered
}

export function formatDate(isoValue: string): string {
  const parsed = new Date(isoValue)

  return Number.isNaN(parsed.getTime()) ? '—' : dateFormatter.format(parsed)
}
