import type { GenreInsightItem } from '@/api/insights'
import { useGenreInsights } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatDecimal, formatInteger } from '@/lib/format'
import { cn } from '@/lib/utils'

/** The API bucket for tracks without a declared genre. Kept visible, relabelled for the English copy. */
const NO_GENRE_LABEL = '(sem gênero)'

interface GenreInsightPanelProps {
  selectedGenre: string | null
  onSelectGenre: (genre: string | null) => void
}

function isFilterable(item: GenreInsightItem): boolean {
  return item.genre !== NO_GENRE_LABEL
}

function displayGenre(genre: string): string {
  return genre === NO_GENRE_LABEL ? 'no genre' : genre
}

const TOP_GENRES = 14

export function GenreInsightPanel({ selectedGenre, onSelectGenre }: GenreInsightPanelProps) {
  const resource = useGenreInsights(TOP_GENRES)

  return (
    <Panel
      eyebrow="Popularity by genre"
      title="Where the popular tracks cluster"
      bodyClassName="p-0"
    >
      <ResourceBoundary resource={resource} loadingLabel="Loading genres" loadingRows={6}>
        {(page) => {
          const peak = page.items.reduce((max, item) => Math.max(max, item.averagePopularity), 1)

          return (
            <>
              <ul>
                {page.items.map((item) => {
                  const filterable = isFilterable(item)
                  const active = selectedGenre !== null && selectedGenre === item.genre

                  return (
                    <li key={item.genre}>
                      <button
                        type="button"
                        disabled={!filterable}
                        onClick={() => onSelectGenre(active ? null : item.genre)}
                        aria-pressed={active}
                        className={cn(
                          'flex w-full items-center gap-3 border-b border-line/50 px-5 py-3 text-left last:border-b-0 transition-colors duration-200',
                          filterable ? 'hover:bg-surface-2/50' : 'cursor-default opacity-80',
                          active && 'bg-surface-2/70',
                        )}
                      >
                        <span
                          className={cn(
                            'min-w-0 flex-1 truncate text-sm capitalize',
                            active ? 'text-brand-bright' : 'text-text',
                            !filterable && 'italic text-text-faint',
                          )}
                        >
                          {displayGenre(item.genre)}
                        </span>

                        <div className="hidden w-28 items-center gap-2 sm:flex">
                          <div className="h-1 flex-1 overflow-hidden rounded-full bg-surface-2">
                            <div
                              className="h-full rounded-full bg-brand"
                              style={{ width: `${(item.averagePopularity / peak) * 100}%` }}
                            />
                          </div>
                        </div>

                        <span className="w-10 shrink-0 text-right font-mono text-xs text-text-dim">
                          {formatDecimal(item.averagePopularity, 1)}
                        </span>
                        <span className="w-14 shrink-0 text-right font-mono text-xs text-text-faint">
                          {formatInteger(item.trackCount)}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>

              <p className="px-5 py-3.5 text-xs leading-relaxed text-text-faint">
                Top {formatInteger(page.items.length)} genres by average popularity, of{' '}
                {formatInteger(page.totalCount)}. The <em>no genre</em> bucket stays in the list so
                the counts still add up to the whole catalog; it is the only row that cannot filter
                the ranking. Numbers are average popularity and track count. Pick a genre to narrow
                the ranking on the right.
              </p>
            </>
          )
        }}
      </ResourceBoundary>
    </Panel>
  )
}
