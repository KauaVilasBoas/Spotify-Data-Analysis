import { lazy, Suspense } from 'react'
import { useAlbumYearInsights } from '@/api/queries'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import { formatDecimal, formatInteger } from '@/lib/format'

const YearTrendChart = lazy(() =>
  import('@/components/charts/YearTrendChart').then((module) => ({
    default: module.YearTrendChart,
  })),
)

const MAX_YEARS = 200

export function YearInsightPanel() {
  const resource = useAlbumYearInsights(MAX_YEARS)

  return (
    <Panel eyebrow="Release-year trend" title="Popularity over time">
      <ResourceBoundary resource={resource} loadingLabel="Loading release-year trend" loadingRows={4}>
        {(page) => {
          const known = page.items.filter((item) => item.year !== null)
          const nullYear = page.items.find((item) => item.year === null)

          if (known.length === 0) {
            return (
              <EmptyState
                title="No release years yet"
                description="Every track currently reports a null release year, so there is no temporal trend to draw. This is a known data gap (card E1.12), not a rendering issue."
                hint={
                  nullYear !== undefined
                    ? `${formatInteger(nullYear.trackCount)} tracks sit in the null-year bucket.`
                    : undefined
                }
              />
            )
          }

          return (
            <div className="space-y-3">
              <Suspense fallback={<div className="skeleton h-[260px] w-full" aria-hidden="true" />}>
                <YearTrendChart points={known} />
              </Suspense>

              <p className="text-xs leading-relaxed text-text-faint">
                Average popularity per release year across {formatInteger(known.length)} years.
                {nullYear !== undefined
                  ? ` A further ${formatInteger(nullYear.trackCount)} tracks have no known release year and are held in a separate null-year bucket (avg ${formatDecimal(nullYear.averagePopularity, 1)}), kept so the counts still add up to the whole catalog.`
                  : ' No tracks fall outside a known year.'}
              </p>
            </div>
          )
        }}
      </ResourceBoundary>
    </Panel>
  )
}
