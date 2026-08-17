import type { CatalogSummary } from '@/api/insights'
import { CoverageBar, type CoverageSegment } from '@/components/data/CoverageBar'
import { MetricTile } from '@/components/data/MetricTile'
import { ErrorProbePanel } from '@/components/diagnostics/ErrorProbePanel'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { TickRule } from '@/components/foundation/TickRule'
import { formatInteger, formatShare } from '@/lib/format'
import { useCatalogSummary } from '@/providers/catalog-summary-context'

function buildCoverage(summary: CatalogSummary): readonly CoverageSegment[] {
  return [
    {
      key: 'medidas',
      label: 'Features medidas',
      value: summary.tracksWithMeasuredFeatures,
      swatch: 'var(--acid)',
      note: 'Valores observados no dataset. Nunca incluem imputação.',
    },
    {
      key: 'imputadas',
      label: 'Features imputadas',
      value: summary.tracksWithImputedFeatures,
      swatch: 'var(--clay)',
      note: 'Preenchidas no tratamento de faltantes e contadas à parte.',
    },
    {
      key: 'ausentes',
      label: 'Sem audio-features',
      value: summary.tracksWithoutAudioFeatures,
      swatch: 'var(--hairline-strong)',
      note: 'Faixas ainda não casadas com o dataset externo.',
    },
  ]
}

function Masthead() {
  return (
    <header className="max-w-3xl">
      <p className="label-micro">01 · visão geral</p>
      <h2 className="display-wonk mt-3 text-[clamp(2.25rem,5vw,3.5rem)] leading-[0.95] font-semibold text-bone">
        O catálogo, medido <span className="text-acid">antes</span> de ser interpretado.
      </h2>
      <p className="mt-5 max-w-prose text-bone-dim">
        Todo número desta página vem de{' '}
        <code className="font-mono text-[0.8125rem] text-bone">GET /api/insights/summary</code>, lido do
        Postgres pelo read-side em Dapper. Medido e imputado aparecem separados, porque média sobre valor
        imputado não é observação.
      </p>
    </header>
  )
}

function HeroTotal({ summary }: { summary: CatalogSummary }) {
  return (
    <div className="grid gap-10 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)] lg:gap-16">
      <div className="animate-rise">
        <p className="label-micro">faixas no catálogo</p>
        <p className="numeral mt-2 text-[clamp(4.5rem,13vw,9rem)] leading-[0.82] text-bone">
          {formatInteger(summary.totalTracks)}
        </p>
        <TickRule className="mt-6" />
        <p className="mt-4 max-w-md text-sm text-bone-dim">
          {formatInteger(summary.tracksWithAudioFeatures)} delas têm audio-features disponíveis, ou{' '}
          {formatShare(summary.tracksWithAudioFeatures, summary.totalTracks)} do catálogo.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-x-6 gap-y-7 self-end sm:grid-cols-3 lg:grid-cols-1 lg:gap-y-8">
        <MetricTile label="Artistas distintos" value={summary.distinctArtists} delayMs={90} />
        <MetricTile label="Álbuns distintos" value={summary.distinctAlbums} delayMs={180} />
        <MetricTile
          label="Gêneros distintos"
          value={summary.distinctGenres}
          footnote="Declarados nas audio-features."
          delayMs={270}
        />
      </div>
    </div>
  )
}

function SummaryContent({ summary }: { summary: CatalogSummary }) {
  if (summary.totalTracks === 0) {
    return (
      <Panel eyebrow="Catálogo vazio">
        <EmptyState
          title="Nenhuma faixa ingerida ainda"
          description="A API respondeu com sucesso, mas o catálogo está zerado. Rode a ingestão do módulo Catalog para popular o Postgres antes de usar o painel."
          hint="estado vazio compartilhado"
        />
      </Panel>
    )
  }

  return (
    <div className="space-y-12">
      <HeroTotal summary={summary} />

      <Panel
        eyebrow="Cobertura de audio-features"
        aside={
          <span className="font-mono text-[0.6875rem] text-bone-faint">
            base {formatInteger(summary.totalTracks)}
          </span>
        }
      >
        <CoverageBar total={summary.totalTracks} segments={buildCoverage(summary)} />
      </Panel>
    </div>
  )
}

export function OverviewPage() {
  const resource = useCatalogSummary()

  return (
    <div className="space-y-12">
      <Masthead />

      <ResourceBoundary resource={resource} loadingLabel="Lendo o resumo do catálogo" loadingRows={4}>
        {(summary) => <SummaryContent summary={summary} />}
      </ResourceBoundary>

      <ErrorProbePanel />
    </div>
  )
}
