import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { TrackSort } from '@/api/catalog'
import type { RecommendationParams } from '@/api/prediction'
import { useTrackRecommendations } from '@/api/queries'
import { RecommendationControls } from '@/components/recommendations/RecommendationControls'
import { RecommendationHonesty } from '@/components/recommendations/RecommendationHonesty'
import { RecommendationList } from '@/components/recommendations/RecommendationList'
import { TrackSearchPanel } from '@/components/catalog/TrackSearchPanel'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'

// Defaults dos controles expostos ao usuário (brief §2).
const DEFAULT_LIMIT = 10
const DEFAULT_STRATEGY = 'content' as const
const DEFAULT_GENRE_MODE = 'boost' as const

type ExposedParams = Required<Pick<RecommendationParams, 'limit' | 'strategy' | 'genreMode'>>

export function RecommendationsPage() {
  // URL: ?seed=<spotifyTrackId>. Trocar semente reflete na URL (brief §5).
  const [searchParams, setSearchParams] = useSearchParams()
  const seedFromUrl = searchParams.get('seed')

  // Estado do seletor de semente (TrackSearchPanel é totalmente controlado por props).
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<TrackSort>('PopularityDesc')
  const [page, setPage] = useState(1)

  // Semente selecionada: URL tem prioridade; o painel atualiza a URL ao selecionar.
  const selectedTrackId = seedFromUrl

  function handleSelectTrack(trackId: string) {
    setSearchParams({ seed: trackId })
    setPage(1)
  }

  // Parâmetros do recomendador (apenas os três controles do brief).
  const [params, setParams] = useState<ExposedParams>({
    limit: DEFAULT_LIMIT,
    strategy: DEFAULT_STRATEGY,
    genreMode: DEFAULT_GENRE_MODE,
  })

  // Hook de recomendações — desabilitado quando não há semente.
  const recommendationsResource = useTrackRecommendations(selectedTrackId, params)

  // A estratégia efetiva exibida ao usuário normaliza a caixa do enum .NET.
  const showBlend = params.strategy === 'blend'

  return (
    <div className="space-y-6 pb-8">
      <header className="space-y-1">
        <p className="label-micro">recommendations</p>
        <h2 className="numeral text-[clamp(2rem,4vw,3rem)] text-text">Recommendations</h2>
      </header>

      <div className="grid gap-6 lg:grid-cols-[380px_1fr]">
        {/* Coluna esquerda: seletor de semente */}
        <div className="space-y-4">
          <TrackSearchPanel
            search={search}
            onSearchChange={(v) => { setSearch(v); setPage(1) }}
            sort={sort}
            onSortChange={(v) => { setSort(v); setPage(1) }}
            page={page}
            onPageChange={setPage}
            selectedTrackId={selectedTrackId}
            onSelectTrack={handleSelectTrack}
          />
        </div>

        {/* Coluna direita: controles + resultados */}
        <div className="space-y-4">
          {selectedTrackId === null ? (
            <Panel>
              <EmptyState
                title="Select a seed track"
                description="Pick a track from the list on the left. The recommender will find the most similar tracks in the catalog and show you exactly why each one was chosen."
                hint="The URL updates as you select, so you can share or bookmark a specific seed."
              />
            </Panel>
          ) : (
            <>
              {/* Controles do recomendador */}
              <Panel eyebrow="Parameters" title="Recommender settings">
                <RecommendationControls params={params} onChange={setParams} />
              </Panel>

              {/* Honestidade + lista de resultados */}
              <ResourceBoundary
                resource={recommendationsResource}
                loadingLabel="Computing recommendations"
                loadingRows={6}
              >
                {(rec) => (
                  <div className="space-y-4">
                    {/* Painel de honestidade: warnings, fallbacks, estatísticas */}
                    <Panel eyebrow="Status" title="What the API applied">
                      <RecommendationHonesty
                        rec={rec}
                        requestedStrategy={params.strategy}
                      />
                    </Panel>

                    {/* Lista de recomendações */}
                    <Panel
                      eyebrow={`${rec.recommendations.length} results`}
                      title={`Similar to ${rec.seedName}`}
                    >
                      <div className="-mx-5 -mb-5">
                        <RecommendationList
                          items={rec.recommendations}
                          showBlend={showBlend}
                        />
                      </div>
                    </Panel>
                  </div>
                )}
              </ResourceBoundary>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
