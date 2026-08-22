import { ChevronLeft, ChevronRight, Loader2, Search } from 'lucide-react'
import type { TrackListItem, TrackSort } from '@/api/catalog'
import { useCatalogSearch } from '@/api/queries'
import { TrackArt } from '@/components/data/TrackArt'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ErrorState } from '@/components/feedback/ErrorState'
import { LoadingState } from '@/components/feedback/LoadingState'
import { Panel } from '@/components/foundation/Panel'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { formatDuration, formatInteger } from '@/lib/format'
import { cn } from '@/lib/utils'

const PAGE_SIZE = 20
const DEBOUNCE_MS = 220

const SORT_OPTIONS: readonly { value: TrackSort; label: string }[] = [
  { value: 'Name', label: 'Name' },
  { value: 'PopularityDesc', label: 'Popularity' },
]

interface TrackSearchPanelProps {
  search: string
  onSearchChange: (search: string) => void
  sort: TrackSort
  onSortChange: (sort: TrackSort) => void
  page: number
  onPageChange: (page: number) => void
  selectedTrackId: string | null
  onSelectTrack: (trackId: string) => void
}

export function TrackSearchPanel({
  search,
  onSearchChange,
  sort,
  onSortChange,
  page,
  onPageChange,
  selectedTrackId,
  onSelectTrack,
}: TrackSearchPanelProps) {
  const debouncedSearch = useDebouncedValue(search, DEBOUNCE_MS)
  const resource = useCatalogSearch(debouncedSearch.trim(), page, sort, PAGE_SIZE)

  const isSearching = resource.state.status === 'loading'
  const isStale = search.trim() !== debouncedSearch.trim()

  return (
    <Panel bodyClassName="p-0">
      <div className="space-y-3 border-b border-line/70 p-4">
        <div className="flex items-center gap-3 rounded-[var(--radius-sm)] border border-line-strong bg-surface-1 px-3.5">
          {isSearching || isStale ? (
            <Loader2 className="size-4 shrink-0 animate-spin text-brand-bright" aria-hidden="true" />
          ) : (
            <Search className="size-4 shrink-0 text-text-faint" aria-hidden="true" />
          )}
          <input
            type="search"
            value={search}
            onChange={(event) => onSearchChange(event.target.value)}
            placeholder="Search 89,740 tracks by name or artist…"
            aria-label="Search tracks by name or artist"
            className="h-11 w-full bg-transparent text-sm text-text outline-none placeholder:text-text-faint"
          />
        </div>

        <div className="flex items-center gap-2">
          <span className="label-micro">Sort</span>
          <div className="flex gap-1">
            {SORT_OPTIONS.map((option) => (
              <button
                key={option.value}
                type="button"
                onClick={() => onSortChange(option.value)}
                aria-pressed={sort === option.value}
                className={cn(
                  'rounded-full px-3 py-1 text-xs font-medium transition-colors duration-150',
                  sort === option.value
                    ? 'bg-surface-3 text-text'
                    : 'text-text-faint hover:bg-surface-2 hover:text-text-dim',
                )}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      {resource.state.status === 'loading' && (
        <div className="p-5">
          <LoadingState label="Searching the catalog" rows={6} />
        </div>
      )}

      {resource.state.status === 'failed' && (
        <div className="p-5">
          <ErrorState error={resource.state.error} onRetry={resource.reload} />
        </div>
      )}

      {resource.state.status === 'ready' &&
        (resource.state.data.items.length === 0 ? (
          <div className="px-5">
            <EmptyState
              title={
                debouncedSearch.trim().length > 0
                  ? 'No track matches that search'
                  : 'Start by searching the catalog'
              }
              description={
                debouncedSearch.trim().length > 0
                  ? `Nothing in the 89,740 tracks matches “${debouncedSearch.trim()}”. Search runs over the track name and every credited artist.`
                  : 'Type a track name or an artist name above. Search is case-insensitive and matches either, so one input covers both.'
              }
              hint="Pick a result to inspect its metadata and audio profile."
            />
          </div>
        ) : (
          <>
            <ResultList
              items={resource.state.data.items}
              selectedTrackId={selectedTrackId}
              onSelectTrack={onSelectTrack}
            />
            <Pagination
              page={resource.state.data.page}
              totalPages={resource.state.data.totalPages}
              totalCount={resource.state.data.totalCount}
              shown={resource.state.data.items.length}
              busy={isSearching || isStale}
              onPageChange={onPageChange}
            />
          </>
        ))}
    </Panel>
  )
}

interface ResultListProps {
  items: TrackListItem[]
  selectedTrackId: string | null
  onSelectTrack: (trackId: string) => void
}

function ResultList({ items, selectedTrackId, onSelectTrack }: ResultListProps) {
  return (
    <ul>
      {items.map((track, index) => (
        <li key={track.trackId}>
          <button
            type="button"
            onClick={() => onSelectTrack(track.trackId)}
            aria-current={track.trackId === selectedTrackId}
            className={cn(
              'animate-rise flex w-full items-center gap-3.5 border-b border-line/50 px-5 py-3 text-left transition-colors last:border-b-0',
              track.trackId === selectedTrackId
                ? 'bg-surface-2'
                : 'hover:bg-surface-2/50',
            )}
            style={{ animationDelay: `${index * 25}ms` }}
          >
            <TrackArt trackId={track.trackId} size={40} />

            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium text-text">{track.name}</span>
              <span className="block truncate text-xs text-text-faint">{track.primaryArtist}</span>
            </span>

            {!track.hasAudioFeatures && (
              <span className="label-micro hidden shrink-0 rounded-full border border-absent/60 px-2 py-0.5 text-text-faint sm:inline-block">
                no features
              </span>
            )}

            <span className="shrink-0 font-mono text-xs text-text-faint">
              {formatDuration(track.durationMs)}
            </span>
            <span className="w-8 shrink-0 text-right font-mono text-xs text-brand-bright">
              {track.popularity}
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}

interface PaginationProps {
  page: number
  totalPages: number
  totalCount: number
  shown: number
  busy: boolean
  onPageChange: (page: number) => void
}

function Pagination({ page, totalPages, totalCount, shown, busy, onPageChange }: PaginationProps) {
  const canPrev = page > 1
  const canNext = page < totalPages

  return (
    <div className="flex items-center justify-between gap-4 px-5 py-3.5">
      <p className="text-xs text-text-faint">
        Page {formatInteger(page)} of {formatInteger(Math.max(totalPages, 1))} · {formatInteger(shown)}{' '}
        of {formatInteger(totalCount)} tracks
      </p>

      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={!canPrev || busy}
          aria-label="Previous page"
          className="grid size-8 place-items-center rounded-[var(--radius-sm)] border border-line-strong text-text-dim transition-colors hover:border-brand/50 hover:text-text disabled:pointer-events-none disabled:opacity-40"
        >
          <ChevronLeft className="size-4" aria-hidden="true" />
        </button>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={!canNext || busy}
          aria-label="Next page"
          className="grid size-8 place-items-center rounded-[var(--radius-sm)] border border-line-strong text-text-dim transition-colors hover:border-brand/50 hover:text-text disabled:pointer-events-none disabled:opacity-40"
        >
          <ChevronRight className="size-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  )
}
