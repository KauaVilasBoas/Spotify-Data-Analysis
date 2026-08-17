import { ArrowLeft } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { Panel } from '@/components/foundation/Panel'
import { Button } from '@/components/ui/button'
import { findSectionByPath } from '@/navigation'

export function UnderConstructionPage() {
  const { pathname } = useLocation()
  const section = findSectionByPath(pathname)

  return (
    <div className="max-w-3xl space-y-8 py-6">
      <header className="space-y-4">
        <p className="label-micro">planned section</p>
        <h2 className="numeral text-[clamp(2.25rem,5vw,3.5rem)] text-text">
          {section?.label ?? 'Planned section'}
        </h2>
      </header>

      <Panel
        eyebrow="Status"
        title="Not built yet"
        action={
          section?.card !== undefined ? (
            <span className="label-micro shrink-0 rounded-full border border-line-strong px-2.5 py-1">
              card {section.card}
            </span>
          ) : undefined
        }
      >
        <div className="space-y-3">
          <p className="text-text-dim">{section?.summary ?? 'This section is not implemented.'}</p>
          <p className="max-w-prose text-sm text-text-faint">
            The route exists and navigation reaches it on purpose — the shell keeps no dead links.
            The content lands in this section&rsquo;s own card, reusing the same typed API client
            and the same loading, empty and error states already defined here.
          </p>
        </div>
      </Panel>

      <Button asChild size="sm" variant="outline" className="border-line-strong bg-transparent">
        <Link to="/">
          <ArrowLeft aria-hidden="true" />
          Back to overview
        </Link>
      </Button>
    </div>
  )
}
