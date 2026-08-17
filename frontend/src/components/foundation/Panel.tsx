import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface PanelProps {
  title?: string
  eyebrow?: string
  action?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}

export function Panel({ title, eyebrow, action, children, className, bodyClassName }: PanelProps) {
  const hasHeader = title !== undefined || eyebrow !== undefined || action !== undefined

  return (
    <section className={cn('surface-card relative overflow-hidden', className)}>
      {hasHeader && (
        <header className="flex items-start justify-between gap-4 border-b border-line/70 px-5 py-4">
          <div className="min-w-0 space-y-1">
            {eyebrow !== undefined && <p className="label-micro">{eyebrow}</p>}
            {title !== undefined && (
              <h2 className="truncate text-[0.98rem] font-semibold tracking-tight text-text">
                {title}
              </h2>
            )}
          </div>
          {action}
        </header>
      )}

      <div className={cn('px-5 py-5', bodyClassName)}>{children}</div>
    </section>
  )
}
