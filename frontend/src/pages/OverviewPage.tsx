import { useCatalogSummary, useCurrentModel, usePopularityRanking } from '@/api/queries'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { CorrelationPanel } from '@/components/overview/CorrelationPanel'
import { DistributionPanel } from '@/components/overview/DistributionPanel'
import { FeatureImportancePanel } from '@/components/overview/FeatureImportancePanel'
import { HeroPanel } from '@/components/overview/HeroPanel'
import { ModelPanel } from '@/components/overview/ModelPanel'
import { TopTracksPanel } from '@/components/overview/TopTracksPanel'

const TOP_TRACKS = 8

export function OverviewPage() {
  const summaryResource = useCatalogSummary()
  const modelResource = useCurrentModel()
  const rankingResource = usePopularityRanking(TOP_TRACKS)

  const model = modelResource.state.status === 'ready' ? modelResource.state.data : null

  return (
    <div className="space-y-6 pb-8">
      <ResourceBoundary
        resource={summaryResource}
        loadingLabel="Loading catalog summary"
        loadingRows={5}
      >
        {(summary) => <HeroPanel summary={summary} model={model} />}
      </ResourceBoundary>

      <div className="grid gap-6 lg:grid-cols-3">
        <ResourceBoundary resource={modelResource} loadingLabel="Loading model" loadingRows={4}>
          {(currentModel) => <ModelPanel model={currentModel} />}
        </ResourceBoundary>

        <ResourceBoundary
          resource={modelResource}
          loadingLabel="Loading feature importance"
          loadingRows={6}
        >
          {(currentModel) => <FeatureImportancePanel model={currentModel} />}
        </ResourceBoundary>

        <CorrelationPanel />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <ResourceBoundary
          resource={rankingResource}
          loadingLabel="Loading popularity ranking"
          loadingRows={6}
        >
          {(ranking) => <TopTracksPanel items={ranking.items} totalCount={ranking.totalCount} />}
        </ResourceBoundary>

        <DistributionPanel />
      </div>
    </div>
  )
}
