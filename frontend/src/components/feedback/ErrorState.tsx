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
    <div role="alert" className={cn('border-l-2 border-clay bg-clay-shadow/40 py-4 pr-4 pl-5', className)}>
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="label-micro text-clay">
          {error.status !== undefined ? `HTTP ${error.status}` : 'sem resposta'}
        </span>
        <p className="display-wonk text-lg text-bone">{error.headline}</p>
      </div>

      <dl className="mt-3 space-y-2 border-l border-hairline-strong pl-3">
        {error.apiTitle !== undefined && (
          <div className="flex flex-wrap gap-x-3">
            <dt className="label-micro pt-0.5">title</dt>
            <dd className="font-mono text-xs text-bone-dim">{error.apiTitle}</dd>
          </div>
        )}
        <div className="flex flex-wrap gap-x-3">
          <dt className="label-micro pt-0.5">detail</dt>
          <dd className="max-w-prose font-mono text-xs text-bone-dim">{error.detail}</dd>
        </div>
        {fieldErrors.map((message) => (
          <div key={message} className="flex flex-wrap gap-x-3">
            <dt className="label-micro pt-0.5">erro</dt>
            <dd className="font-mono text-xs text-bone-dim">{message}</dd>
          </div>
        ))}
        {error.traceId !== undefined && (
          <div className="flex flex-wrap gap-x-3">
            <dt className="label-micro pt-0.5">traceId</dt>
            <dd className="font-mono text-xs text-bone-faint">{error.traceId}</dd>
          </div>
        )}
      </dl>

      <p className="mt-4 max-w-prose text-sm text-bone-dim">{error.nextStep}</p>

      {onRetry !== undefined && (
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={onRetry}
          className="mt-4 rounded-none border-hairline-strong bg-transparent font-mono text-xs tracking-wider uppercase hover:border-acid hover:bg-acid/10 hover:text-acid"
        >
          <RotateCw aria-hidden="true" />
          Tentar de novo
        </Button>
      )}
    </div>
  )
}
