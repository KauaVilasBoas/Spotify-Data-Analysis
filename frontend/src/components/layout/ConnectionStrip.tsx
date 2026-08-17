import { apiConfig } from '@/api/config'
import { useCatalogSummary } from '@/providers/catalog-summary-context'
import { cn } from '@/lib/utils'

type Indicator = { tone: string; dot: string; text: string; pulse: boolean }

const INDICATORS: Readonly<Record<'loading' | 'ready' | 'failed', Indicator>> = {
  loading: { tone: 'text-bone-dim', dot: 'bg-bone-dim', text: 'sondando', pulse: true },
  ready: { tone: 'text-acid', dot: 'bg-acid', text: 'conectado', pulse: true },
  failed: { tone: 'text-clay', dot: 'bg-clay', text: 'sem resposta', pulse: false },
}

export function ConnectionStrip() {
  const { state } = useCatalogSummary()
  const indicator = INDICATORS[state.status]

  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-1 border-b border-hairline bg-ink-sunken px-5 py-2 md:px-8">
      <span className="flex items-center gap-2">
        <span
          className={cn('size-1.5 rounded-full', indicator.dot, indicator.pulse && 'animate-pulse-dot')}
          aria-hidden="true"
        />
        <span className={cn('label-micro', indicator.tone)}>{indicator.text}</span>
      </span>

      <span className="flex items-baseline gap-2 truncate">
        <span className="label-micro">api</span>
        <span className="truncate font-mono text-[0.6875rem] text-bone-dim">
          {apiConfig.isConfigured ? apiConfig.baseUrl : 'VITE_API_BASE_URL não definida'}
        </span>
      </span>

      <span className="ml-auto hidden items-baseline gap-2 sm:flex">
        <span className="label-micro">fonte</span>
        <span className="font-mono text-[0.6875rem] text-bone-dim">postgres / catalog + analytics</span>
      </span>
    </div>
  )
}
