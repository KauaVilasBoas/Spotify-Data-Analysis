import { formatInteger } from '@/lib/format'
import { cn } from '@/lib/utils'

interface MetricTileProps {
  label: string
  value: number
  footnote?: string
  accent?: boolean
  delayMs?: number
  className?: string
}

export function MetricTile({
  label,
  value,
  footnote,
  accent = false,
  delayMs = 0,
  className,
}: MetricTileProps) {
  return (
    <div
      className={cn(
        'animate-rise border-t border-hairline pt-3 pr-4',
        accent && 'border-acid',
        className,
      )}
      style={{ animationDelay: `${delayMs}ms` }}
    >
      <p className="label-micro">{label}</p>
      <p
        className={cn(
          'numeral mt-1.5 text-[2.5rem] leading-none',
          accent ? 'text-acid' : 'text-bone',
        )}
      >
        {formatInteger(value)}
      </p>
      {footnote !== undefined && <p className="mt-1.5 text-xs text-bone-faint">{footnote}</p>}
    </div>
  )
}
