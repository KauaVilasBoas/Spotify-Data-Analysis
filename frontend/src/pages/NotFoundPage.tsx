import { ArrowLeft } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { Button } from '@/components/ui/button'

export function NotFoundPage() {
  const { pathname } = useLocation()

  return (
    <div className="max-w-2xl space-y-8 py-10">
      <div>
        <p className="label-micro text-danger">route not found</p>
        <h2 className="numeral mt-4 text-[clamp(2.25rem,5vw,3.5rem)] text-text">
          Nothing lives at{' '}
          <span className="font-mono text-[0.45em] font-medium text-text-dim">{pathname}</span>
        </h2>
      </div>

      <Button asChild size="sm" variant="outline" className="border-line-strong bg-transparent">
        <Link to="/">
          <ArrowLeft aria-hidden="true" />
          Back to overview
        </Link>
      </Button>
    </div>
  )
}
