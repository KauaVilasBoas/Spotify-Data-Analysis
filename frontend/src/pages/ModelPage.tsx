import { useCurrentModel, useDatasetStats } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { formatDecimal, formatInteger } from '@/lib/format'

export function ModelPage() {
  const modelResource = useCurrentModel()
  const statsResource = useDatasetStats()

  return (
    <div className="space-y-6 pb-8">
      <header className="space-y-1">
        <p className="label-micro">model</p>
        <h2 className="numeral text-[clamp(2rem,4vw,3rem)] text-text">Popularity model</h2>
      </header>

      <ResourceBoundary resource={modelResource} loadingLabel="Loading model" loadingRows={4}>
        {(model) => (
          <p className="text-text-dim">
            Version <span className="text-text">{model.version}</span> &middot;{' '}
            {model.features.length} features &middot; R²{' '}
            <span className="text-text">{formatDecimal(model.model.rSquared, 3)}</span>
          </p>
        )}
      </ResourceBoundary>

      <ResourceBoundary resource={statsResource} loadingLabel="Loading dataset stats" loadingRows={3}>
        {(stats) => (
          <p className="text-text-dim">
            <span className="text-text">{formatInteger(stats.trainableTracks)}</span> trainable
            tracks of{' '}
            <span className="text-text">{formatInteger(stats.totalTracks)}</span> in the
            catalog
          </p>
        )}
      </ResourceBoundary>
    </div>
  )
}
