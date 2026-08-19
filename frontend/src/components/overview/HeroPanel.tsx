import NumberFlow from '@number-flow/react'
import type { CatalogSummary } from '@/api/insights'
import type { ModelVersion } from '@/api/prediction'
import { CoverageBar } from '@/components/data/CoverageBar'
import { formatDecimal, formatInteger } from '@/lib/format'

interface HeroPanelProps {
  summary: CatalogSummary
  model: ModelVersion | null
}

interface Fact {
  label: string
  value: string
}

export function HeroPanel({ summary, model }: HeroPanelProps) {
  const facts: Fact[] = [
    { label: 'Genres', value: formatInteger(summary.distinctGenres) },
    { label: 'Artists', value: formatInteger(summary.distinctArtists) },
    { label: 'Albums', value: formatInteger(summary.distinctAlbums) },
    {
      label: 'Model R²',
      value: model === null ? 'n/a' : formatDecimal(model.model.rSquared, 2),
    },
    {
      label: 'Model MAE',
      value: model === null ? 'n/a' : formatDecimal(model.model.meanAbsoluteError, 1),
    },
  ]

  return (
    <section className="surface-card animate-rise relative overflow-hidden px-6 py-8 md:px-10 md:py-11">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0"
        style={{
          background:
            'radial-gradient(48rem 26rem at 8% -20%, rgb(29 185 84 / 0.20), transparent 62%)',
        }}
      />

      <div className="relative">
        <p className="label-micro">Kaggle · Spotify Tracks Dataset · static snapshot</p>

        <h1 className="numeral mt-5 flex flex-wrap items-baseline gap-x-4 gap-y-1 text-[clamp(3rem,9vw,6.5rem)] text-text">
          <NumberFlow value={summary.totalTracks} locales="en-US" />
          <span className="text-[0.22em] font-semibold tracking-tight text-text-dim">
            tracks analyzed
          </span>
        </h1>

        <p className="mt-4 max-w-2xl text-[0.95rem] leading-relaxed text-text-dim">
          Every number on this page is read live from the project API. No fixtures, no seeded
          demo data.
        </p>

        <div className="mt-9 max-w-3xl">
          <CoverageBar
            measured={summary.tracksWithMeasuredFeatures}
            imputed={summary.tracksWithImputedFeatures}
            absent={summary.tracksWithoutAudioFeatures}
            total={summary.totalTracks}
          />
        </div>

        <dl className="mt-9 flex flex-wrap items-baseline gap-x-8 gap-y-4 border-t border-line/70 pt-6">
          {facts.map((fact) => (
            <div key={fact.label} className="flex items-baseline gap-2.5">
              <dt className="label-micro">{fact.label}</dt>
              <dd className="numeral text-xl text-text">{fact.value}</dd>
            </div>
          ))}
        </dl>
      </div>
    </section>
  )
}
