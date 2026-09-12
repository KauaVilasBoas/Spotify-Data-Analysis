import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { TrackSort } from '@/api/catalog'
import { useCurrentModel, useDatasetStats, usePopularityPrediction } from '@/api/queries'
import { FeatureImportancePanel } from '@/components/overview/FeatureImportancePanel'
import { ModelPanel } from '@/components/overview/ModelPanel'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { TrackSearchPanel } from '@/components/catalog/TrackSearchPanel'
import { DatasetStatsPanel } from '@/components/model/DatasetStatsPanel'
import { PredictionPanel } from '@/components/model/PredictionPanel'

export function ModelPage() {
  const [searchParams, setSearchParams] = useSearchParams()

  // Parâmetro de URL: ?track=<spotifyTrackId>
  const trackIdFromUrl = searchParams.get('track')

  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<TrackSort>('PopularityDesc')
  const [page, setPage] = useState(1)
  const [selectedTrackId, setSelectedTrackId] = useState<string | null>(trackIdFromUrl)

  const modelResource = useCurrentModel()
  const statsResource = useDatasetStats()
  // Habilitada só quando há faixa selecionada; 422 cai no ResourceBoundary
  const predictionResource = usePopularityPrediction(selectedTrackId)

  function handleSelectTrack(trackId: string) {
    setSelectedTrackId(trackId)
    // Reflete no URL para a tela ser compartilhável
    setSearchParams({ track: trackId }, { replace: true })
  }

  return (
    <div className="space-y-8 pb-8">
      <header className="space-y-1">
        <p className="label-micro">model</p>
        <h2 className="numeral text-[clamp(2rem,4vw,3rem)] text-text">Popularity model</h2>
      </header>

      {/* Seção 1: métricas e importância de features */}
      <section className="space-y-6" aria-label="Model performance">
        <ResourceBoundary resource={modelResource} loadingLabel="Loading model" loadingRows={6}>
          {(model) => (
            <div className="grid gap-6 lg:grid-cols-2">
              <ModelPanel model={model} />
              {/* topCount = tamanho total da lista: exibe todas as features nesta tela dedicada */}
              <FeatureImportancePanel model={model} topCount={model.featureImportance.length} />
            </div>
          )}
        </ResourceBoundary>
      </section>

      {/* Seção 2: composição do dataset de treino */}
      <section aria-label="Training dataset">
        <ResourceBoundary resource={statsResource} loadingLabel="Loading dataset stats" loadingRows={6}>
          {(stats) => <DatasetStatsPanel stats={stats} />}
        </ResourceBoundary>
      </section>

      {/* Seção 3: predição interativa */}
      <section aria-label="Track prediction">
        <h3 className="label-micro mb-4">Predict for a track</h3>
        <div className="grid gap-6 lg:grid-cols-2">
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
          <PredictionPanel
            predictionResource={selectedTrackId !== null ? predictionResource : null}
          />
        </div>
      </section>
    </div>
  )
}
