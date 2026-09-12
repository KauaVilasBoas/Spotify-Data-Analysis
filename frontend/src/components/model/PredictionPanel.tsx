import { AlertTriangle } from 'lucide-react'
import type { PopularityPredictionResponse } from '@/api/prediction'
import { MetricTile } from '@/components/data/MetricTile'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ResourceBoundary } from '@/components/feedback/ResourceBoundary'
import { Panel } from '@/components/foundation/Panel'
import type { ApiResource } from '@/hooks/use-api-resource'
import { formatDecimal } from '@/lib/format'

interface PredictionPanelProps {
  predictionResource: ApiResource<PopularityPredictionResponse> | null
}

export function PredictionPanel({ predictionResource }: PredictionPanelProps) {
  return (
    <Panel eyebrow="Popularity prediction" title="Predicted score">
      {predictionResource === null ? (
        <EmptyState
          title="No track selected"
          description="Select a track from the list to see its predicted popularity score."
          hint="Use the search to find any of the 89,740 tracks in the catalog."
        />
      ) : (
        <ResourceBoundary
          resource={predictionResource}
          loadingLabel="Predicting popularity"
          loadingRows={3}
        >
          {(prediction) => <PredictionResult prediction={prediction} />}
        </ResourceBoundary>
      )}
    </Panel>
  )
}

interface PredictionResultProps {
  prediction: PopularityPredictionResponse
}

function PredictionResult({ prediction }: PredictionResultProps) {
  return (
    <div className="space-y-6">
      {/* Número herói */}
      <div className="flex flex-wrap items-end gap-8">
        <MetricTile
          label="Predicted popularity"
          value={prediction.predictedPopularity}
          fractionDigits={0}
          emphasis
          caption={`Model v${String(prediction.modelVersion)} · ${prediction.mode}`}
        />

        {/* Raw score só aparece quando houve clamping */}
        {prediction.wasClamped && (
          <div className="space-y-1.5">
            <p className="label-micro">Raw score</p>
            <p className="numeral text-[1.7rem] text-text-dim">
              {formatDecimal(prediction.rawScore, 2)}
            </p>
            <p className="max-w-xs text-xs leading-relaxed text-text-faint">
              The raw output was outside [0, 100] and was clamped to{' '}
              <span className="font-mono">{String(prediction.predictedPopularity)}</span>.
            </p>
          </div>
        )}
      </div>

      {/* Warnings — obrigatórios, nunca escondidos */}
      {prediction.warnings.length > 0 && (
        <div className="rounded-[var(--radius-sm)] border border-imputed/40 bg-imputed/8 p-4">
          <div className="flex items-start gap-3">
            <AlertTriangle
              className="mt-0.5 size-4 shrink-0 text-imputed"
              aria-hidden="true"
            />
            <div className="space-y-2">
              <p className="text-sm font-medium text-text">Prediction warnings</p>
              <ul className="space-y-1">
                {prediction.warnings.map((warning, i) => (
                  // índice é chave segura: lista estática por resposta
                  <li key={i} className="text-xs leading-relaxed text-text-dim">
                    {warning}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
