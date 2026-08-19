import type { ModelVersion } from '@/api/prediction'
import { Panel } from '@/components/foundation/Panel'
import { formatDecimal, formatInteger } from '@/lib/format'

interface FeatureImportancePanelProps {
  model: ModelVersion
  topCount?: number
}

/**
 * Permutation feature importance, expressed as the mean R² drop when a feature is shuffled.
 * The headline finding of the whole project lives here: genre dwarfs every audio feature.
 */
export function FeatureImportancePanel({ model, topCount = 7 }: FeatureImportancePanelProps) {
  const ranked = [...model.featureImportance]
    .sort((left, right) => right.rSquaredDropMean - left.rSquaredDropMean)
    .slice(0, topCount)

  const leader = ranked[0]
  const runnerUp = ranked[1]
  const scale = leader?.rSquaredDropMean ?? 1
  const dominance =
    leader === undefined || runnerUp === undefined || runnerUp.rSquaredDropMean === 0
      ? null
      : leader.rSquaredDropMean / runnerUp.rSquaredDropMean

  return (
    <Panel
      eyebrow="Permutation importance"
      title="What actually predicts popularity"
      bodyClassName="space-y-5"
    >
      {dominance !== null && leader !== undefined && (
        <p className="text-sm leading-relaxed text-text-dim">
          <span className="font-semibold text-text">{leader.feature}</span> is worth{' '}
          <span className="font-mono text-brand-bright">{formatDecimal(dominance, 0)}×</span> the
          next strongest signal. Genre carries the catalog; the audio features barely move the
          needle.
        </p>
      )}

      <ol className="space-y-3">
        {ranked.map((item) => {
          const width = scale === 0 ? 0 : (item.rSquaredDropMean / scale) * 100
          const isLeader = item.feature === leader?.feature

          return (
            <li key={item.feature} className="space-y-1.5">
              <div className="flex items-baseline justify-between gap-3">
                <span className="truncate text-sm text-text">{item.feature}</span>
                <span className="shrink-0 font-mono text-xs text-text-dim">
                  {formatDecimal(item.rSquaredDropMean, 3)}
                </span>
              </div>
              <div className="h-1.5 w-full overflow-hidden rounded-full bg-surface-2">
                <div
                  className="animate-sweep h-full rounded-full"
                  style={{
                    width: `${width}%`,
                    background: isLeader
                      ? 'linear-gradient(90deg, var(--brand-bright), var(--brand))'
                      : 'var(--line-strong)',
                    boxShadow: isLeader ? '0 0 16px -2px rgb(30 215 96 / 0.6)' : undefined,
                  }}
                />
              </div>
              {item.slotCount > 1 && (
                <p className="text-[0.68rem] text-text-faint">
                  {formatInteger(item.slotCount)} categorical slots
                </p>
              )}
            </li>
          )
        })}
      </ol>

      <p className="border-t border-line/70 pt-4 text-xs leading-relaxed text-text-faint">
        Mean R² drop across permutation runs on the held-out test split of{' '}
        {formatInteger(model.testSampleCount)} tracks.
      </p>
    </Panel>
  )
}
