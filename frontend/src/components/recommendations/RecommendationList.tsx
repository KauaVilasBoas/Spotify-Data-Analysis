import type { RecommendationItem } from '@/api/prediction'
import { TrackArt } from '@/components/data/TrackArt'
import { formatDecimal, formatInteger, formatSignedDecimal } from '@/lib/format'
import { cn } from '@/lib/utils'

// Exibição de um item individual de recomendação, com decomposição completa do score
// e indicadores de proveniência (imputed, collapsed versions).

interface RecommendationItemCardProps {
  item: RecommendationItem
  rank: number
  showBlend: boolean
}

function FeatureRow({
  feature,
  seedValue,
  candidateValue,
  contribution,
}: {
  feature: string
  seedValue: number
  candidateValue: number
  contribution: number
}) {
  return (
    <div className="grid grid-cols-[1fr_auto_auto_auto] items-center gap-x-4 gap-y-0.5 text-xs">
      <span className="font-mono text-text-faint">{feature}</span>
      <span className="text-right text-text-dim" title="Seed value">
        {formatDecimal(seedValue, 3)}
      </span>
      <span className="text-right text-text-dim" title="Candidate value">
        {formatDecimal(candidateValue, 3)}
      </span>
      <span
        className={cn(
          'text-right font-mono',
          contribution > 0 ? 'text-brand-bright' : contribution < 0 ? 'text-danger' : 'text-text-faint',
        )}
        title="Contribution to cosine score"
      >
        {formatSignedDecimal(contribution)}
      </span>
    </div>
  )
}

function RecommendationItemCard({ item, rank, showBlend }: RecommendationItemCardProps) {
  return (
    <li className="animate-rise border-b border-line/50 px-5 py-4 last:border-b-0">
      <div className="flex items-start gap-4">
        {/* Rank + arte gerada */}
        <div className="flex shrink-0 items-center gap-3">
          <span className="w-5 text-right font-mono text-xs text-text-faint">{rank}</span>
          <TrackArt trackId={item.trackId} size={48} />
        </div>

        {/* Identidade da faixa */}
        <div className="min-w-0 flex-1 space-y-1.5">
          <div className="flex flex-wrap items-baseline gap-2">
            <span className="truncate text-sm font-medium text-text">{item.name}</span>
            {item.isImputed && (
              <span className="rounded-full border border-imputed/60 px-2 py-0.5 text-[0.65rem] font-medium text-imputed">
                imputed
              </span>
            )}
            {item.equivalentVersionsCollapsed > 0 && (
              <span className="rounded-full border border-line-strong px-2 py-0.5 text-[0.65rem] text-text-faint">
                +{formatInteger(item.equivalentVersionsCollapsed)} collapsed
              </span>
            )}
          </div>
          <p className="text-xs text-text-faint">
            {item.artist}
            {item.album !== null && item.album.trim().length > 0 && (
              <> · {item.album}</>
            )}
          </p>
          {item.sharedGenre !== null && (
            <p className="text-xs text-text-faint">
              Shared genre: <span className="text-text-dim">{item.sharedGenre}</span>
            </p>
          )}
        </div>

        {/* Score e decomposição */}
        <div className="shrink-0 space-y-1 text-right">
          <p className="font-mono text-sm font-semibold text-text" title="Hybrid score">
            {formatDecimal(item.score, 4)}
          </p>
          <p className="text-[0.65rem] text-text-faint">
            cosine{' '}
            <span className="font-mono text-text-dim">{formatDecimal(item.cosineScore, 4)}</span>
            {item.genreBoost !== 0 && (
              <>
                {' '}+ genre{' '}
                <span className="font-mono text-brand-bright">
                  {formatDecimal(item.genreBoost, 4)}
                </span>
              </>
            )}
          </p>
          {/* Blend: co-ocorrência em playlist */}
          {showBlend && item.coOccurrenceScore > 0 && (
            <p className="text-[0.65rem] text-text-faint">
              co-occurrence{' '}
              <span className="font-mono text-text-dim">
                {formatDecimal(item.coOccurrenceScore, 4)}
              </span>
              {item.coPlaylists > 0 && (
                <> · {formatInteger(item.coPlaylists)} playlists</>
              )}
            </p>
          )}
        </div>
      </div>

      {/* Top features (explicabilidade) */}
      {item.topFeatures.length > 0 && (
        <div className="mt-3 rounded-[var(--radius-sm)] border border-line/60 bg-surface-1 px-4 py-3">
          <div className="mb-2 grid grid-cols-[1fr_auto_auto_auto] gap-x-4 text-[0.65rem] uppercase tracking-wider text-text-faint">
            <span>Feature</span>
            <span>Seed</span>
            <span>Candidate</span>
            <span>Contribution</span>
          </div>
          <div className="space-y-1.5">
            {item.topFeatures.map((f) => (
              <FeatureRow
                key={f.feature}
                feature={f.feature}
                seedValue={f.seedValue}
                candidateValue={f.candidateValue}
                contribution={f.contribution}
              />
            ))}
          </div>
        </div>
      )}
    </li>
  )
}

interface RecommendationListProps {
  items: RecommendationItem[]
  showBlend: boolean
}

export function RecommendationList({ items, showBlend }: RecommendationListProps) {
  if (items.length === 0) {
    return (
      <p className="px-5 py-8 text-sm text-text-faint">
        No recommendations returned for this seed and parameters.
      </p>
    )
  }

  return (
    <ul>
      {items.map((item, index) => (
        <RecommendationItemCard
          key={item.trackId}
          item={item}
          rank={index + 1}
          showBlend={showBlend}
        />
      ))}
    </ul>
  )
}
