import { lazy, Suspense, useState } from 'react'
import { useAudioFeatureDistribution } from '@/api/queries'
import { AUDIO_FEATURES, type AudioFeatureName } from '@/api/insights'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatInteger } from '@/lib/format'
import { cn } from '@/lib/utils'

const DistributionChart = lazy(() =>
  import('@/components/charts/DistributionChart').then((module) => ({
    default: module.DistributionChart,
  })),
)

const BUCKET_COUNT = 20

export function DistributionPanel() {
  const [feature, setFeature] = useState<AudioFeatureName>('energy')
  const resource = useAudioFeatureDistribution(feature, BUCKET_COUNT)

  return (
    <Panel eyebrow="Audio-feature distribution" title="How the catalog is shaped">
      <div className="mb-5 flex flex-wrap gap-1.5" role="group" aria-label="Choose an audio feature">
        {AUDIO_FEATURES.map((name) => (
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

      <ResourceBoundary resource={resource} loadingLabel="Loading distribution" loadingRows={4}>
        {(distribution) => (
          <div className="space-y-3">
            <Suspense
              fallback={<div className="skeleton h-[190px] w-full" aria-hidden="true" />}
            >
              <DistributionChart distribution={distribution} />
            </Suspense>
            <p className="text-xs text-text-faint">
              {formatInteger(
                distribution.buckets.reduce((total, bucket) => total + bucket.count, 0),
              )}{' '}
              tracks across {distribution.buckets.length} equal-width buckets, measured features
              only.
            </p>
          </div>
        )}
      </ResourceBoundary>
    </Panel>
  )
}
