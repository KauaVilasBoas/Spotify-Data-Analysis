import { RotateCw } from 'lucide-react'
import { flattenFieldErrors, type ApiError } from '@/api/api-error'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

interface ErrorStateProps {
  error: ApiError
  onRetry?: () => void
  className?: string
}

export function ErrorState({ error, onRetry, className }: ErrorStateProps) {
  const fieldErrors = flattenFieldErrors(error)

  return (
    <div
      role="alert"
      className={cn(
        'rounded-[var(--radius-md)] border border-danger/35 bg-danger/[0.07] p-5',
        className,
      )}
    >
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="label-micro text-danger">
          {error.status !== undefined ? `HTTP ${error.status}` : 'NO RESPONSE'}
        </span>
        <p className="text-lg font-semibold tracking-tight text-text">{error.headline}</p>
      </div>

      <dl className="mt-3 space-y-2 border-l border-line-strong pl-3">
        {error.apiTitle !== undefined && (
          <div className="flex flex-wrap gap-x-3">
            <dt className="label-micro pt-0.5">title</dt>
            <dd className="font-mono text-xs text-text-dim">{error.apiTitle}</dd>
          </div>
        )}
        <div className="flex flex-wrap gap-x-3">
          <dt className="label-micro pt-0.5">detail</dt>
          <dd className="max-w-prose font-mono text-xs text-text-dim">{error.detail}</dd>
        </div>
        {fieldErrors.map((message) => (
          <div key={message} className="flex flex-wrap gap-x-3">
            <dt className="label-micro pt-0.5">error</dt>
            <dd className="font-mono text-xs text-text-dim">{message}</dd>
          </div>
        ))}
        {error.traceId !== undefined && (
          <div className="flex flex-wrap gap-x-3">
            <dt className="label-micro pt-0.5">traceId</dt>
            <dd className="font-mono text-xs text-text-faint">{error.traceId}</dd>
          </div>
        )}
      </dl>

      <p className="mt-4 max-w-prose text-sm text-text-dim">{error.nextStep}</p>

      {onRetry !== undefined && (
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={onRetry}
          className="mt-4 border-line-strong bg-transparent font-mono text-xs tracking-wider uppercase hover:border-brand hover:bg-brand/10 hover:text-brand-bright"
        >
          <RotateCw aria-hidden="true" />
          Try again
        </Button>
      )}
    </div>
  )
}
