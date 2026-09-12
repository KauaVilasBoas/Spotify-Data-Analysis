import { useTrackRecommendations } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'

/** Faixa de demonstração enquanto o seletor ainda não existe */
const DEMO_TRACK_ID = '7ucgcKHb0r9orpwBeqF0MV'

export function RecommendationsPage() {
  const recommendationsResource = useTrackRecommendations(DEMO_TRACK_ID)

  return (
    <div className="space-y-6 pb-8">
      <header className="space-y-1">
        <p className="label-micro">recommendations</p>
        <h2 className="numeral text-[clamp(2rem,4vw,3rem)] text-text">Recommendations</h2>
      </header>

      <ResourceBoundary
        resource={recommendationsResource}
        loadingLabel="Loading recommendations"
        loadingRows={5}
      >
        {(rec) => (
          <p className="text-text-dim">
            Seed:{' '}
            <span className="text-text">{rec.seedName}</span> &middot;{' '}
            <span className="text-text">{rec.seedArtist}</span> &middot; strategy{' '}
            <span className="text-text">{rec.effectiveStrategy}</span> &middot;{' '}
            <span className="text-text">{rec.recommendations.length}</span> recommendations
          </p>
        )}
      </ResourceBoundary>
    </div>
  )
}
