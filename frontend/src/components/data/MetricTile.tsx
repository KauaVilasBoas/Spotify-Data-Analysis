import NumberFlow from '@number-flow/react'
import { cn } from '@/lib/utils'

interface MetricTileProps {
  label: string
  value: number
  caption?: string
  suffix?: string
  fractionDigits?: number
  emphasis?: boolean
  delayMs?: number
  className?: string
}

export function MetricTile({
  label,
  value,
  caption,
  suffix,
  fractionDigits = 0,
  emphasis = false,
  delayMs = 0,
  className,
}: MetricTileProps) {
  return (
    <div className={cn('animate-rise space-y-1.5', className)} style={{ animationDelay: `${delayMs}ms` }}>
      <p className="label-micro">{label}</p>
      <p
        className={cn(
          'numeral flex items-baseline gap-1 text-text',
          emphasis ? 'text-[2.25rem]' : 'text-[1.7rem]',
        )}
      >
        <NumberFlow
          value={value}
          locales="en-US"
          format={{
            minimumFractionDigits: fractionDigits,
            maximumFractionDigits: fractionDigits,
          }}
        />
        {suffix !== undefined && (
          <span className="text-[0.55em] font-semibold text-text-faint">{suffix}</span>
        )}
      </p>
      {caption !== undefined && <p className="text-xs text-text-faint">{caption}</p>}
    </div>
  )
}
