import { useLocation, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Panel } from '@/components/foundation/Panel'
import { TickRule } from '@/components/foundation/TickRule'
import { findSectionByPath } from '@/navigation'

export function UnderConstructionPage() {
  const { pathname } = useLocation()
  const section = findSectionByPath(pathname)

  return (
    <div className="max-w-3xl space-y-10">
      <header>
        <div className="flex flex-wrap items-center gap-3">
          <p className="label-micro">{section?.index ?? '--'} · seção planejada</p>
          <Badge
            variant="outline"
            className="rounded-none border-hairline-strong font-mono text-[0.625rem] tracking-widest text-bone-faint uppercase"
          >
            em construção
          </Badge>
        </div>
        <h2 className="display-wonk mt-3 text-[clamp(2.25rem,5vw,3.5rem)] leading-[0.95] font-semibold text-bone">
          {section?.label ?? 'Seção planejada'}
        </h2>
        <TickRule className="mt-6" />
      </header>

      <Panel
        eyebrow="Status"
        aside={
          section?.card !== undefined ? (
            <span className="font-mono text-[0.6875rem] text-bone-faint">card {section.card}</span>
          ) : undefined
        }
      >
        <div className="flex flex-col gap-5 sm:flex-row sm:items-start">
          <div className="hatched h-16 w-16 shrink-0 border border-hairline-strong" aria-hidden="true" />
          <div className="space-y-3">
            <p className="text-bone-dim">{section?.summary ?? 'Seção ainda não implementada.'}</p>
            <p className="max-w-prose text-sm text-bone-faint">
              A rota existe e a navegação chega até aqui de propósito: o shell não guarda link morto. O
              conteúdo entra no card próprio desta seção, reusando o mesmo cliente tipado e os mesmos estados
              de carregamento, vazio e erro já definidos aqui.
            </p>
          </div>
        </div>
      </Panel>

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
