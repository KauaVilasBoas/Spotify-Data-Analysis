import { lazy, Suspense, useState } from 'react'
import { useFeatureCorrelations } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatInteger } from '@/lib/format'
import { ImputedToggle } from './ImputedToggle'

const FeatureCorrelationChart = lazy(() =>
  import('@/components/charts/FeatureCorrelationChart').then((module) => ({
    default: module.FeatureCorrelationChart,
  })),
)

export function CorrelationInsightPanel() {
  const [includeImputed, setIncludeImputed] = useState(false)
  const resource = useFeatureCorrelations(includeImputed)

  return (
    <Panel
      eyebrow="Pearson correlation"
      title="Audio features against popularity"
      action={<ImputedToggle value={includeImputed} onChange={setIncludeImputed} />}
    >
      <ResourceBoundary resource={resource} loadingLabel="Loading correlations" loadingRows={6}>
        {(data) => {
          const notComputable = data.correlations.filter((item) => item.coefficient === null).length

          return (
            <div className="space-y-3">
              <Suspense fallback={<div className="skeleton h-[320px] w-full" aria-hidden="true" />}>
                <FeatureCorrelationChart correlations={data.correlations} />
              </Suspense>

              <p className="text-xs leading-relaxed text-text-faint">
                Pearson r for each of the nine continuous audio features, with the sample size shown
                per feature. Computed over {formatInteger(data.consideredCount)} tracks; imputed rows{' '}
                {data.includedImputed ? 'included' : 'excluded'} (
                {formatInteger(data.imputedCount)} available).{' '}
                {includeImputed
                  ? 'Imputed rows are in: median imputation compresses variance, so coefficients read weaker than on measured data alone.'
                  : 'Imputed rows are out by default: median imputation compresses variance and distorts the coefficient.'}
                {notComputable > 0 &&
                  ` ${notComputable} feature${notComputable > 1 ? 's are' : ' is'} not computable and shown as n/a, never as zero.`}
              </p>

              <p className="text-xs leading-relaxed text-text-faint">
                Every coefficient is small: no single audio feature explains popularity, and none of
                this implies causation.
              </p>
            </div>
          )
        }}
      </ResourceBoundary>
    </Panel>
  )
}
