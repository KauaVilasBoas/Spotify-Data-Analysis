import { lazy, Suspense, useState } from 'react'
import {
  CONTINUOUS_AUDIO_FEATURES,
  type ContinuousAudioFeatureName,
} from '@/api/insights'
import { useAudioFeatureDistribution } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatDecimal, formatInteger } from '@/lib/format'
import { cn } from '@/lib/utils'
import { ImputedToggle } from './ImputedToggle'

const DistributionChart = lazy(() =>
  import('@/components/charts/DistributionChart').then((module) => ({
    default: module.DistributionChart,
  })),
)

const BUCKET_OPTIONS = [12, 20, 30, 50] as const

export function DistributionInsightPanel() {
  const [feature, setFeature] = useState<ContinuousAudioFeatureName>('energy')
  const [buckets, setBuckets] = useState<number>(20)
  const [includeImputed, setIncludeImputed] = useState(false)
  const resource = useAudioFeatureDistribution(feature, buckets, includeImputed)

  return (
    <Panel
      eyebrow="Audio-feature distribution"
      title="How the catalog is shaped"
      action={<ImputedToggle value={includeImputed} onChange={setIncludeImputed} />}
    >
      <div className="mb-4 flex flex-wrap gap-1.5" role="group" aria-label="Choose an audio feature">
        {CONTINUOUS_AUDIO_FEATURES.map((name) => (
          <button
            key={name}
            type="button"
            onClick={() => setFeature(name)}
            aria-pressed={feature === name}
            className={cn(
              'rounded-full px-3 py-1.5 text-xs capitalize transition-colors duration-200',
              feature === name
                ? 'bg-brand text-brand-ink font-semibold'
                : 'border border-line-strong text-text-faint hover:border-brand/40 hover:text-text-dim',
            )}
          >
            {name}
          </button>
        ))}
      </div>

      <div className="mb-5 flex items-center gap-2" role="group" aria-label="Choose the bucket count">
        <span className="label-micro">buckets</span>
        {BUCKET_OPTIONS.map((count) => (
          <button
            key={count}
            type="button"
            onClick={() => setBuckets(count)}
            aria-pressed={buckets === count}
            className={cn(
              'rounded-full px-2.5 py-1 font-mono text-xs transition-colors duration-200',
              buckets === count
                ? 'bg-surface-3 text-text'
                : 'border border-line-strong text-text-faint hover:border-brand/40 hover:text-text-dim',
            )}
          >
            {count}
          </button>
        ))}
      </div>

      <ResourceBoundary resource={resource} loadingLabel="Loading distribution" loadingRows={4}>
        {(distribution) => (
          <div className="space-y-3">
            <Suspense fallback={<div className="skeleton h-[190px] w-full" aria-hidden="true" />}>
              <DistributionChart distribution={distribution} />
            </Suspense>

            <p className="text-xs leading-relaxed text-text-faint">
              {formatInteger(distribution.totalConsidered)} tracks across{' '}
              {distribution.buckets.length} equal-width buckets, drawn over{' '}
              {distribution.minValue !== null && distribution.maxValue !== null
                ? `[${formatDecimal(distribution.minValue, 2)}, ${formatDecimal(distribution.maxValue, 2)}]`
                : 'n/a'}
              . Measured {formatInteger(distribution.measuredCount)} · imputed{' '}
              {formatInteger(distribution.imputedCount)} ·{' '}
              {distribution.includedImputed
                ? 'imputed rows included in this view.'
                : 'imputed rows excluded by default.'}
            </p>
          </div>
        )}
      </ResourceBoundary>
    </Panel>
  )
}
