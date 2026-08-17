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
    <div className={cn('flex flex-col items-start gap-4 py-6', className)}>
      <div className="hatched h-10 w-24 border border-hairline-strong" aria-hidden="true" />

      <div className="space-y-1.5">
        <p className="display-wonk text-xl text-bone">{title}</p>
        <p className="max-w-prose text-sm text-bone-dim">{description}</p>
        {hint !== undefined && <p className="label-micro pt-1">{hint}</p>}
      </div>

      {action}
    </div>
  )
}
