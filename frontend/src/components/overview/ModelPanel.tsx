import type { ModelVersion } from '@/api/prediction'
import { Panel } from '@/components/foundation/Panel'
import { formatDate, formatDecimal, formatInteger } from '@/lib/format'

interface ModelPanelProps {
  model: ModelVersion
}

interface Comparison {
  label: string
  model: number
  baseline: number
  digits: number
  betterIsHigher: boolean
}

export function ModelPanel({ model }: ModelPanelProps) {
  const comparisons: Comparison[] = [
    {
      label: 'R²',
      model: model.model.rSquared,
      baseline: model.baseline.rSquared,
      digits: 3,
      betterIsHigher: true,
    },
    {
      label: 'MAE',
      model: model.model.meanAbsoluteError,
      baseline: model.baseline.meanAbsoluteError,
      digits: 2,
      betterIsHigher: false,
    },
    {
      label: 'RMSE',
      model: model.model.rootMeanSquaredError,
      baseline: model.baseline.rootMeanSquaredError,
      digits: 2,
      betterIsHigher: false,
    },
  ]

  return (
    <Panel
      eyebrow={`${model.trainer} · version ${model.version}`}
      title="Popularity regression"
      bodyClassName="space-y-6"
      action={
        <span className="label-micro shrink-0 rounded-full border border-brand/35 bg-brand/10 px-2.5 py-1 text-brand-bright">
          live
        </span>
      }
    >
      <dl className="space-y-4">
        {comparisons.map((comparison) => {
          const improved = comparison.betterIsHigher
            ? comparison.model > comparison.baseline
            : comparison.model < comparison.baseline

          return (
            <div key={comparison.label} className="flex items-baseline justify-between gap-4">
              <dt className="label-micro">{comparison.label}</dt>
              <dd className="flex items-baseline gap-3">
                <span className="numeral text-2xl text-text">
                  {formatDecimal(comparison.model, comparison.digits)}
                </span>
                <span
                  className={`font-mono text-[0.7rem] ${improved ? 'text-brand-bright' : 'text-danger'}`}
                >
                  vs {formatDecimal(comparison.baseline, comparison.digits)} baseline
                </span>
              </dd>
            </div>
          )
        })}
      </dl>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 border-t border-line/70 pt-5 text-xs">
        <div>
          <dt className="label-micro">Train split</dt>
          <dd className="mt-1 font-mono text-text-dim">
            {formatInteger(model.trainingSampleCount)}
          </dd>
        </div>
        <div>
          <dt className="label-micro">Test split</dt>
          <dd className="mt-1 font-mono text-text-dim">{formatInteger(model.testSampleCount)}</dd>
        </div>
        <div>
          <dt className="label-micro">Trained</dt>
          <dd className="mt-1 font-mono text-text-dim">{formatDate(model.trainedAtUtc)}</dd>
        </div>
        <div>
          <dt className="label-micro">Imputed rows</dt>
          <dd className="mt-1 font-mono text-text-dim">
            {model.trainedOnImputed ? 'included' : 'excluded'}
          </dd>
        </div>
      </dl>

      <p className="text-xs leading-relaxed text-text-faint">
        Baseline is the mean-popularity predictor. Seed {formatInteger(model.seed)}, test fraction{' '}
        {formatDecimal(model.testFraction, 2)}.
      </p>
    </Panel>
  )
}
