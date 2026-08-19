import { formatInteger, formatShare, share } from '@/lib/format'
import { cn } from '@/lib/utils'

interface CoverageBarProps {
  measured: number
  imputed: number
  absent: number
  total: number
  className?: string
}

interface Segment {
  key: 'measured' | 'imputed' | 'absent'
  label: string
  value: number
  swatch: string
  text: string
}

/**
 * Provenance is a first-class domain fact: measured, imputed and absent audio features never
 * share a colour and never share a label, because conflating them would silently overstate how
 * much of the catalog was actually observed.
 */
export function CoverageBar({ measured, imputed, absent, total, className }: CoverageBarProps) {
  const segments: Segment[] = [
    {
      key: 'measured',
      label: 'Measured',
      value: measured,
      swatch: 'bg-measured',
      text: 'text-measured',
    },
    {
      key: 'imputed',
      label: 'Imputed',
      value: imputed,
      swatch: 'bg-imputed',
      text: 'text-imputed',
    },
    {
      key: 'absent',
      label: 'Absent',
      value: absent,
      swatch: 'bg-absent',
      text: 'text-text-dim',
    },
  ]

  return (
    <div className={cn('space-y-5', className)}>
      <div
        className="flex h-2.5 w-full gap-0.5 overflow-hidden rounded-full bg-surface-2"
        role="img"
        aria-label={segments
          .map(
            (segment) =>
              `${segment.label}: ${formatInteger(segment.value)} of ${formatInteger(total)} tracks, ${formatShare(segment.value, total)}`,
          )
          .join('. ')}
      >
        {segments
          .filter((segment) => segment.value > 0)
          .map((segment) => (
            <span
              key={segment.key}
              className={cn(
                'animate-sweep h-full first:rounded-l-full last:rounded-r-full',
                segment.swatch,
              )}
              style={{
                width: `${share(segment.value, total) * 100}%`,
                boxShadow:
                  segment.key === 'measured' ? '0 0 20px -2px rgb(30 215 96 / 0.7)' : undefined,
              }}
            />
          ))}
      </div>

      <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-3">
        {segments.map((segment) => (
          <div key={segment.key} className="space-y-1.5">
            <dt className="flex items-center gap-2">
              <span className={cn('size-2 rounded-full', segment.swatch)} aria-hidden="true" />
              <span className="label-micro">{segment.label}</span>
            </dt>
            <dd>
              <p className={cn('font-mono text-sm', segment.text)}>
                {formatShare(segment.value, total)}
              </p>
              <p className="mt-0.5 text-xs text-text-faint">
                {formatInteger(segment.value)} of {formatInteger(total)} tracks
              </p>
            </dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
