import type { PopularityRankingItem } from '@/api/insights'
import { TrackArt } from '@/components/data/TrackArt'
import { Panel } from '@/components/foundation/Panel'
import { formatInteger } from '@/lib/format'

interface TopTracksPanelProps {
  items: PopularityRankingItem[]
  totalCount: number
}

export function TopTracksPanel({ items, totalCount }: TopTracksPanelProps) {
  return (
    <Panel
      eyebrow="Popularity ranking"
      title="Most popular tracks in the dataset"
      bodyClassName="p-0"
    >
      <ol>
        {items.map((track, index) => (
          <li
            key={track.trackId}
            className="animate-rise flex items-center gap-4 border-b border-line/50 px-5 py-3 last:border-b-0 hover:bg-surface-2/50"
            style={{ animationDelay: `${index * 45}ms` }}
          >
            <span className="w-5 shrink-0 text-right font-mono text-xs text-text-faint">
              {index + 1}
            </span>

            <TrackArt trackId={track.trackId} size={44} />

            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-text">{track.name}</p>
              <p className="truncate text-xs text-text-faint">{track.primaryArtist}</p>
            </div>

            {track.genre !== null && (
              <span className="label-micro hidden shrink-0 rounded-full border border-line-strong px-2.5 py-1 sm:block">
                {track.genre}
              </span>
            )}

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
        Top {formatInteger(items.length)} of {formatInteger(totalCount)} ranked tracks. The dataset
        carries no cover images, so every sleeve here is generated from the track&rsquo;s own
        identity and audio profile.
      </p>
    </Panel>
  )
}
