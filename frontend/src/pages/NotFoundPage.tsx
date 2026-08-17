import { Link, useLocation } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { TickRule } from '@/components/foundation/TickRule'

export function NotFoundPage() {
  const { pathname } = useLocation()

  return (
    <div className="max-w-2xl space-y-8">
      <div>
        <p className="label-micro text-clay">rota inexistente</p>
        <h2 className="display-wonk mt-3 text-[clamp(2.25rem,5vw,3.5rem)] leading-[0.95] font-semibold text-bone">
          Não há nada em <span className="font-mono text-[0.5em] text-bone-dim">{pathname}</span>
        </h2>
        <TickRule className="mt-6" />
      </div>

      <Button
        asChild
        size="sm"
        variant="outline"
        className="rounded-none border-hairline-strong bg-transparent font-mono text-xs tracking-wider uppercase hover:border-acid hover:bg-acid/10 hover:text-acid"
      >
        <Link to="/">
          <ArrowLeft aria-hidden="true" />
          Voltar para a visão geral
        </Link>
      </Button>
    </div>
  )
}
