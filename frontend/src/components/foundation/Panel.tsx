import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface PanelProps {
  eyebrow?: string
  aside?: ReactNode
  className?: string
  bodyClassName?: string
  children: ReactNode
}

export function Panel({ eyebrow, aside, className, bodyClassName, children }: PanelProps) {
  return (
    <section
      className={cn(
        'relative border border-hairline bg-ink-raised/70 backdrop-blur-[1px]',
        'before:absolute before:top-[-1px] before:left-[-1px] before:size-2 before:border-t before:border-l before:border-acid/60 before:content-[""]',
        'after:absolute after:right-[-1px] after:bottom-[-1px] after:size-2 after:border-r after:border-b after:border-acid/60 after:content-[""]',
        className,
      )}
    >
      {(eyebrow !== undefined || aside !== undefined) && (
        <header className="flex items-baseline justify-between gap-4 border-b border-hairline px-5 py-3">
          {eyebrow !== undefined && <h2 className="label-micro">{eyebrow}</h2>}
          {aside}
        </header>
      )}
      <div className={cn('px-5 py-5', bodyClassName)}>{children}</div>
    </section>
  )
}
