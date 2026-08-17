import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface EmptyStateProps {
  title: string
  description: string
  hint?: string
  action?: ReactNode
  className?: string
}

export function EmptyState({ title, description, hint, action, className }: EmptyStateProps) {
  return (
    <div className={cn('flex flex-col items-start gap-4 py-8', className)}>
      <div
        aria-hidden="true"
        className="surface-inset grid h-12 w-12 place-items-center text-text-faint"
      >
        <span className="font-mono text-lg">∅</span>
      </div>

      <div className="space-y-1.5">
        <p className="text-xl font-semibold tracking-tight text-text">{title}</p>
        <p className="max-w-prose text-sm text-text-dim">{description}</p>
        {hint !== undefined && <p className="label-micro pt-1">{hint}</p>}
      </div>

      {action}
    </div>
  )
}
