import { useArtistInsights } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatCompact, formatInteger } from '@/lib/format'

const TOP_ARTISTS = 8

/**
 * The artist ranking, kept deliberately small (Block C of the card). Artist popularity and
 * followers require Spotify enrichment that has not run, so every artist is un-enriched and the
 * default endpoint (enriched-only) returns nothing. Rather than a headline chart of zeros, this
 * panel states the gap honestly and only draws a list if real enriched data ever arrives.
 */
export function ArtistInsightPanel() {
  const resource = useArtistInsights(TOP_ARTISTS, false)

  return (
    <Panel eyebrow="Artists" title="Artist standing">
      <ResourceBoundary resource={resource} loadingLabel="Loading artists" loadingRows={3}>
        {(page) =>
          page.items.length === 0 ? (
            <div className="space-y-2">
              <p className="text-sm text-text-dim">No enriched artists yet.</p>
              <p className="max-w-prose text-xs leading-relaxed text-text-faint">
                Artist popularity and follower counts come from the Spotify API, which has not been
                run against this catalog, so every artist is un-enriched and this ranking is empty by
                design. Showing a chart of zeros would misrepresent the data; the panel stays quiet
                until real figures exist (tracked as card E2.7).
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              <ul className="space-y-2.5">
                {page.items.map((artist, index) => (
                  <li key={artist.artistId} className="flex items-center gap-3">
                    <span className="w-4 shrink-0 text-right font-mono text-xs text-text-faint">
                      {index + 1}
                    </span>
                    <span className="min-w-0 flex-1 truncate text-sm text-text">{artist.name}</span>
                    <span className="shrink-0 font-mono text-xs text-text-dim">
                      {formatCompact(artist.followers)} followers
                    </span>
                    <span className="w-8 shrink-0 text-right font-mono text-xs text-text-dim">
                      {artist.popularity}
                    </span>
                  </li>
                ))}
              </ul>
              <p className="text-xs leading-relaxed text-text-faint">
                Top {formatInteger(page.items.length)} enriched artists of{' '}
                {formatInteger(page.totalCount)}, by their own Spotify popularity. Un-enriched
                artists are excluded so the list is not a tail of zeros.
              </p>
            </div>
          )
        }
      </ResourceBoundary>
    </Panel>
  )
}
