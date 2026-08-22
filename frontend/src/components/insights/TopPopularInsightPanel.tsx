import { X } from 'lucide-react'
import { usePopularityRanking } from '@/api/queries'
import { TrackArt } from '@/components/data/TrackArt'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatInteger } from '@/lib/format'

interface TopPopularInsightPanelProps {
  genre: string | null
  onClearGenre: () => void
}

const TOP_COUNT = 12

export function TopPopularInsightPanel({ genre, onClearGenre }: TopPopularInsightPanelProps) {
  const resource = usePopularityRanking(TOP_COUNT, genre)

  return (
    <Panel
      eyebrow="Popularity ranking"
      title="Most popular tracks"
      bodyClassName="p-0"
      action={
        genre !== null ? (
          <button
            type="button"
            onClick={onClearGenre}
            className="flex shrink-0 items-center gap-1.5 rounded-full border border-brand/50 px-2.5 py-1 text-xs text-brand-bright transition-colors duration-200 hover:bg-brand/10"
          >
            <span className="capitalize">{genre}</span>
            <X aria-hidden="true" className="size-3" />
            <span className="sr-only">Clear genre filter</span>
          </button>
        ) : undefined
      }
    >
      {genre !== null && (
        <p className="border-b border-imputed/25 bg-imputed/5 px-5 py-2.5 text-xs leading-relaxed text-imputed">
          Filtered to <span className="font-semibold capitalize">{genre}</span>. A genre filter
          narrows the ranking to tracks that carry audio features, so tracks without features drop
          out silently: this is no longer the whole-catalog ranking.
        </p>
      )}

      <ResourceBoundary resource={resource} loadingLabel="Loading popularity ranking" loadingRows={6}>
        {(ranking) =>
          ranking.items.length === 0 ? (
            <div className="px-5">
              <EmptyState
                title="No tracks for this genre"
                description="The genre carried no tracks in the ranking window. Clear the filter to see the whole catalog again."
              />
            </div>
          ) : (
            <>
              <ol>
                {ranking.items.map((track, index) => (
                  <li
                    key={track.trackId}
                    className="animate-rise flex items-center gap-4 border-b border-line/50 px-5 py-3 last:border-b-0 hover:bg-surface-2/50"
                    style={{ animationDelay: `${index * 40}ms` }}
                  >
                    <span className="w-5 shrink-0 text-right font-mono text-xs text-text-faint">
                      {index + 1}
                    </span>

                    <TrackArt trackId={track.trackId} size={40} />

                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium text-text">{track.name}</p>
                      <p className="truncate text-xs text-text-faint">
                        {track.primaryArtist ?? 'n/a'}
                      </p>
                    </div>

                    <div className="flex w-16 shrink-0 items-center gap-2">
                      <div className="h-1 flex-1 overflow-hidden rounded-full bg-surface-2">
                        <div
                          className="h-full rounded-full bg-brand"
                          style={{ width: `${track.popularity}%` }}
                        />
                      </div>
                      <span className="w-6 text-right font-mono text-xs text-text-dim">
                        {track.popularity}
                      </span>
                    </div>
                  </li>
                ))}
              </ol>

              <p className="px-5 py-3.5 text-xs leading-relaxed text-text-faint">
                Top {formatInteger(ranking.items.length)} of {formatInteger(ranking.totalCount)}{' '}
                ranked tracks
                {genre === null
                  ? ', across the whole catalog including tracks with no audio features.'
                  : `, within ${genre}.`}
              </p>
            </>
          )
        }
      </ResourceBoundary>
    </Panel>
  )
}
