import { cn } from '@/lib/utils'

interface LoadingStateProps {
  label?: string
  rows?: number
  className?: string
}

export function LoadingState({ label = 'Querying the API', rows = 3, className }: LoadingStateProps) {
  return (
    <div className={cn('space-y-4', className)} role="status" aria-live="polite" aria-busy="true">
      <div className="flex items-center gap-2">
        <span className="animate-pulse-dot size-1.5 rounded-full bg-brand-bright" />
        <span className="label-micro">{label}</span>
      </div>

      <div className="space-y-2.5">
        {Array.from({ length: rows }, (_, index) => (
          <div
            key={index}
            className="skeleton h-3.5 w-full"
            style={{ maxWidth: `${100 - index * 13}%`, animationDelay: `${index * 130}ms` }}
          />
        ))}
      </div>
    </div>
  )
}
