import { useCallback, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { TrackSort } from '@/api/catalog'
import { useTrack } from '@/api/queries'
import { TrackDetailPanel } from '@/components/catalog/TrackDetailPanel'
import { TrackSearchPanel } from '@/components/catalog/TrackSearchPanel'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'

const TRACK_PARAM = 'track'

export function CatalogPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<TrackSort>('Name')
  const [page, setPage] = useState(1)

  const selectedTrackId = searchParams.get(TRACK_PARAM)
  const trackResource = useTrack(selectedTrackId)

  const selectTrack = useCallback(
    (trackId: string) => {
      setSearchParams(
        (previous) => {
          const next = new URLSearchParams(previous)
          next.set(TRACK_PARAM, trackId)
          return next
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  const handleSearchChange = useCallback((value: string) => {
    setSearch(value)
    setPage(1)
  }, [])

  const handleSortChange = useCallback((value: TrackSort) => {
    setSort(value)
    setPage(1)
  }, [])

  return (
    <div className="space-y-6 pb-8">
      <header className="space-y-3">
        <p className="label-micro">Catalog</p>
        <h1 className="numeral text-[clamp(1.9rem,4vw,2.75rem)] text-text">Search the catalog</h1>
        <p className="max-w-prose text-sm text-text-dim">
          Find a track by name or by any credited artist across the 89,740 tracks, then inspect its
          metadata and audio profile. This is the entry point for recommendations and popularity
          prediction.
        </p>
      </header>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)] xl:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
        <div className="lg:sticky lg:top-20 lg:self-start">
          <TrackSearchPanel
            search={search}
            onSearchChange={handleSearchChange}
            sort={sort}
            onSortChange={handleSortChange}
            page={page}
            onPageChange={setPage}
            selectedTrackId={selectedTrackId}
            onSelectTrack={selectTrack}
          />
        </div>

        <div>
          {selectedTrackId === null ? (
            <Panel>
              <EmptyState
                title="No track selected"
                description="Search on the left and choose a track to see its metadata, its nine continuous audio features and whether those values were measured, imputed or absent."
                hint="The command palette (⌘K) opens tracks here too."
              />
            </Panel>
          ) : (
            <ResourceBoundary
              resource={trackResource}
              loadingLabel="Loading track detail"
              loadingRows={6}
            >
              {(track) => <TrackDetailPanel track={track} />}
            </ResourceBoundary>
          )}
        </div>
      </div>
    </div>
  )
}
