import { cn } from '@/lib/utils'

export function TickRule({ className }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      className={cn('h-3 w-full border-t border-hairline', className)}
      style={{
        backgroundImage:
          'repeating-linear-gradient(90deg, var(--hairline-strong) 0 1px, transparent 1px 12px)',
        backgroundSize: '100% 6px',
        backgroundRepeat: 'no-repeat',
      }}
    />
  )
}
