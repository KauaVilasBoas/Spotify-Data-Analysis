import { lazy, Suspense } from 'react'
import { useFeatureCorrelations } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatInteger } from '@/lib/format'

const CorrelationChart = lazy(() =>
  import('@/components/charts/CorrelationChart').then((module) => ({
    default: module.CorrelationChart,
  })),
)

export function CorrelationPanel() {
  const resource = useFeatureCorrelations()

  return (
    <Panel eyebrow="Pearson correlation" title="Audio features against popularity">
      <ResourceBoundary resource={resource} loadingLabel="Loading correlations" loadingRows={5}>
        {(data) => (
          <div className="space-y-3">
            <Suspense fallback={<div className="skeleton h-[280px] w-full" aria-hidden="true" />}>
              <CorrelationChart correlations={data.correlations} />
            </Suspense>
            <p className="text-xs leading-relaxed text-text-faint">
              Every coefficient sits inside ±0.13. No single audio feature explains popularity on
              its own. Computed over {formatInteger(data.consideredCount)} tracks with measured
              features; imputed rows {data.includedImputed ? 'included' : 'excluded'}.
            </p>
          </div>
        )}
      </ResourceBoundary>
    </Panel>
  )
}
