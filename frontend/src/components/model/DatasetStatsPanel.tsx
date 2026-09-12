import type { DatasetStats } from '@/api/prediction'
import { Panel } from '@/components/foundation/Panel'
import { formatInteger, formatShare } from '@/lib/format'

interface DatasetStatsPanelProps {
  stats: DatasetStats
}

interface StatRowProps {
  label: string
  note: string
  value: number
  total: number
  highlight?: 'exclusion' | 'result'
}

function StatRow({ label, note, value, total, highlight }: StatRowProps) {
  const valueClass =
    highlight === 'result'
      ? 'text-brand-bright'
      : highlight === 'exclusion'
        ? 'text-danger'
        : 'text-text'

  return (
    <div className="flex items-start justify-between gap-4 py-3 border-b border-line/50 last:border-b-0">
      <div className="min-w-0 space-y-0.5">
        <p className="text-sm text-text">{label}</p>
        <p className="text-xs text-text-faint">{note}</p>
      </div>
      <div className="shrink-0 text-right">
        <p className={`font-mono text-sm ${valueClass}`}>{formatInteger(value)}</p>
        <p className="font-mono text-[0.68rem] text-text-faint">{formatShare(value, total)}</p>
      </div>
    </div>
  )
}

export function DatasetStatsPanel({ stats }: DatasetStatsPanelProps) {
  const imputedLabel = stats.includedImputed ? 'included' : 'excluded'

  return (
    <Panel eyebrow="Training dataset" title="What went into the model">
      {/* Exclusões */}
      <div className="space-y-0 border-b border-line/70 pb-4 mb-4">
        <p className="label-micro mb-3">Catalog</p>
        <StatRow
          label="Total tracks"
          note="Everything in the catalog"
          value={stats.totalTracks}
          total={stats.totalTracks}
        />
        <StatRow
          label="Missing popularity"
          note="No training target, so it cannot be used"
          value={stats.excludedMissingPopularity}
          total={stats.totalTracks}
          highlight="exclusion"
        />
        <StatRow
          label="Missing audio features"
          note="No input signal at all"
          value={stats.excludedMissingAudioFeatures}
          total={stats.totalTracks}
          highlight="exclusion"
        />
        <StatRow
          label="Incomplete audio features"
          note="Has some features but not all required fields"
          value={stats.excludedIncompleteAudioFeatures}
          total={stats.totalTracks}
          highlight="exclusion"
        />
      </div>

      {/* Elegíveis separados por proveniência */}
      <div className="space-y-0 border-b border-line/70 pb-4 mb-4">
        <p className="label-micro mb-3">Eligible</p>
        <StatRow
          label="Measured features"
          note="All audio features come from direct measurement"
          value={stats.eligibleWithMeasuredFeatures}
          total={stats.totalTracks}
        />
        <StatRow
          label="Imputed features"
          note="Audio features filled in by imputation, never promoted to measured"
          value={stats.eligibleWithImputedFeatures}
          total={stats.totalTracks}
        />
        <StatRow
          label="Dropped by imputation policy"
          note={`Imputed tracks were ${imputedLabel} in this training run`}
          value={stats.excludedByImputationPolicy}
          total={stats.totalTracks}
          highlight={stats.excludedByImputationPolicy > 0 ? 'exclusion' : undefined}
        />
      </div>

      {/* Resultado */}
      <div className="space-y-0">
        <p className="label-micro mb-3">Training set</p>
        <StatRow
          label="Trainable tracks"
          note="Survived all filters"
          value={stats.trainableTracks}
          total={stats.totalTracks}
          highlight="result"
        />
        <StatRow
          label="Train split"
          note="Used to fit the model"
          value={stats.trainingSampleCount}
          total={stats.trainableTracks}
        />
        <StatRow
          label="Test split"
          note="Held out for evaluation, never seen during training"
          value={stats.testSampleCount}
          total={stats.trainableTracks}
        />
      </div>

      <p className="mt-5 border-t border-line/70 pt-4 text-xs leading-relaxed text-text-faint">
        Imputed rows: <span className="font-mono">{imputedLabel}</span>. Measured and imputed are
        always reported separately, so imputed features never silently count as measured.
      </p>
    </Panel>
  )
}
