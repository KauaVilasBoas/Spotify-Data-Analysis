import { cn } from '@/lib/utils'

interface ImputedToggleProps {
  value: boolean
  onChange: (next: boolean) => void
  label?: string
}

/**
 * The shared "include imputed rows" switch. Off by default everywhere, because median imputation
 * compresses variance and distorts the coefficient; the caller explains what changes when it is on.
 */
export function ImputedToggle({ value, onChange, label = 'Include imputed' }: ImputedToggleProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={value}
      onClick={() => onChange(!value)}
      className={cn(
        'flex shrink-0 items-center gap-2 rounded-full border px-3 py-1.5 text-xs transition-colors duration-200',
        value
          ? 'border-imputed/50 text-imputed'
          : 'border-line-strong text-text-faint hover:border-brand/40 hover:text-text-dim',
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'inline-block size-2 rounded-full',
          value ? 'bg-imputed' : 'bg-absent',
        )}
      />
      {label}
    </button>
  )
}
