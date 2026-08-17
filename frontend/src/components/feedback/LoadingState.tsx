import { cn } from '@/lib/utils'

interface LoadingStateProps {
  label?: string
  rows?: number
  className?: string
}

export function LoadingState({ label = 'Consultando a API', rows = 3, className }: LoadingStateProps) {
  return (
    <div className={cn('space-y-4', className)} role="status" aria-live="polite" aria-busy="true">
      <div className="flex items-center gap-2">
        <span className="size-1.5 rounded-full bg-acid animate-pulse-dot" />
        <span className="label-micro">{label}</span>
      </div>

      <div className="space-y-2">
        {Array.from({ length: rows }, (_, index) => (
          <div
            key={index}
            className="h-3 w-full overflow-hidden bg-hairline"
            style={{ maxWidth: `${100 - index * 14}%` }}
          >
            <div
              className="h-full w-full animate-ticker"
              style={{
                backgroundImage:
                  'linear-gradient(90deg, transparent 0%, color-mix(in srgb, var(--acid) 22%, transparent) 45%, transparent 90%)',
                backgroundSize: '200px 100%',
                backgroundRepeat: 'repeat',
                animationDelay: `${index * 140}ms`,
              }}
            />
          </div>
        ))}
      </div>
    </div>
  )
}
