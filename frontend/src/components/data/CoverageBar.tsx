import { formatInteger, formatShare, share } from '@/lib/format'

export interface CoverageSegment {
  key: string
  label: string
  value: number
  swatch: string
  note: string
}

interface CoverageBarProps {
  total: number
  segments: readonly CoverageSegment[]
}

export function CoverageBar({ total, segments }: CoverageBarProps) {
  const visible = segments.filter((segment) => segment.value > 0)

  return (
    <div className="space-y-5">
      <div className="relative">
        <div
          className="flex h-9 w-full overflow-hidden border border-hairline-strong bg-ink-sunken"
          role="img"
          aria-label={visible
            .map((segment) => `${segment.label}: ${formatShare(segment.value, total)}`)
            .join(', ')}
        >
          {visible.map((segment, index) => (
            <div
              key={segment.key}
              className="animate-sweep h-full"
              style={{
                width: `${share(segment.value, total) * 100}%`,
                background: segment.swatch,
                animationDelay: `${index * 130}ms`,
              }}
            />
          ))}
        </div>
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 mix-blend-multiply"
          style={{
            backgroundImage:
              'repeating-linear-gradient(90deg, color-mix(in srgb, var(--ink) 45%, transparent) 0 1px, transparent 1px 2.5%)',
          }}
        />
      </div>

      <dl className="grid gap-x-8 gap-y-4 sm:grid-cols-3">
        {segments.map((segment) => (
          <div key={segment.key} className="flex gap-3">
            <span
              className="mt-1.5 h-3 w-1 shrink-0"
              style={{ background: segment.swatch }}
              aria-hidden="true"
            />
            <div className="min-w-0">
              <dt className="label-micro">{segment.label}</dt>
              <dd className="numeral mt-0.5 text-xl leading-none text-bone">
                {formatInteger(segment.value)}
                <span className="ml-2 font-mono text-[0.6875rem] font-normal tracking-wider text-bone-faint">
                  {formatShare(segment.value, total)}
                </span>
              </dd>
              <p className="mt-1 text-xs text-bone-faint">{segment.note}</p>
            </div>
          </div>
        ))}
      </dl>
    </div>
  )
}
