import { useState } from 'react'
import { ArtistInsightPanel } from '@/components/insights/ArtistInsightPanel'
import { CorrelationInsightPanel } from '@/components/insights/CorrelationInsightPanel'
import { DistributionInsightPanel } from '@/components/insights/DistributionInsightPanel'
import { GenreInsightPanel } from '@/components/insights/GenreInsightPanel'
import { TopPopularInsightPanel } from '@/components/insights/TopPopularInsightPanel'
import { YearInsightPanel } from '@/components/insights/YearInsightPanel'

export function InsightsPage() {
  // Lifted so picking a genre on the left narrows the popularity ranking on the right.
  const [genre, setGenre] = useState<string | null>(null)

  return (
    <div className="space-y-6 pb-8">
      <header className="space-y-3">
        <p className="label-micro">Insights</p>
        <h1 className="numeral text-[clamp(1.9rem,4vw,2.75rem)] text-text">
          Exploring the catalog
        </h1>
        <p className="max-w-prose text-sm text-text-dim">
          What the audio features say about popularity, how the catalog is shaped, and where the
          popular tracks sit by genre and by year. Everything here is computed in the database and
          read as-is; the panels state their sample and their caveats rather than smoothing them
          over. Correlation is not causation.
        </p>
      </header>

      <div className="grid gap-6 lg:grid-cols-2">
        <CorrelationInsightPanel />
        <DistributionInsightPanel />
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
        <GenreInsightPanel selectedGenre={genre} onSelectGenre={setGenre} />
        <TopPopularInsightPanel genre={genre} onClearGenre={() => setGenre(null)} />
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <YearInsightPanel />
        <ArtistInsightPanel />
      </div>
    </div>
  )
}
