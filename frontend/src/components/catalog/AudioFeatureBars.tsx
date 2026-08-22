import type { TrackAudioFeatures } from '@/api/catalog'
import { formatDecimal } from '@/lib/format'
import { cn } from '@/lib/utils'

interface AudioFeatureBarsProps {
  features: TrackAudioFeatures
}

interface FeatureRow {
  key: string
  label: string
  /** Rendered value, already humanized (n/a never happens here: a present feature has numbers). */
  display: string
  /** 0..1 fill for the bar, clamped. */
  fill: number
}

/** Musical pitch classes, index 0 = C. `key = -1` means "no key detected" on the Spotify scale. */
const PITCH_CLASSES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'] as const

function clamp01(value: number): number {
  if (value < 0) return 0
  return value > 1 ? 1 : value
}

/**
 * Loudness is decibels (typically about -60..0 dBFS); map that window onto 0..1 for the bar while
 * still showing the real dB value, so the reader never mistakes the fill for a normalized score.
 */
function loudnessFill(loudnessDb: number): number {
  return clamp01((loudnessDb + 60) / 60)
}

/** Tempo window for the bar only; the label always shows the true BPM. */
function tempoFill(bpm: number): number {
  return clamp01(bpm / 220)
}

function buildRows(features: TrackAudioFeatures): FeatureRow[] {
  return [
    { key: 'danceability', label: 'Danceability', display: formatDecimal(features.danceability), fill: clamp01(features.danceability) },
    { key: 'energy', label: 'Energy', display: formatDecimal(features.energy), fill: clamp01(features.energy) },
    { key: 'valence', label: 'Valence', display: formatDecimal(features.valence), fill: clamp01(features.valence) },
    { key: 'acousticness', label: 'Acousticness', display: formatDecimal(features.acousticness), fill: clamp01(features.acousticness) },
    { key: 'instrumentalness', label: 'Instrumentalness', display: formatDecimal(features.instrumentalness), fill: clamp01(features.instrumentalness) },
    { key: 'liveness', label: 'Liveness', display: formatDecimal(features.liveness), fill: clamp01(features.liveness) },
    { key: 'speechiness', label: 'Speechiness', display: formatDecimal(features.speechiness), fill: clamp01(features.speechiness) },
    { key: 'tempo', label: 'Tempo', display: `${formatDecimal(features.tempo, 0)} BPM`, fill: tempoFill(features.tempo) },
    { key: 'loudness', label: 'Loudness', display: `${formatDecimal(features.loudness, 1)} dB`, fill: loudnessFill(features.loudness) },
  ]
}

/**
 * The nine continuous audio features rendered as labelled bars rather than a raw number table.
 * Provenance drives the colour: measured features fill in brand green, imputed ones in the amber
 * `--imputed` token, so a glance already separates observed from estimated. The imputed banner is
 * rendered by the parent panel, this component only colours the bars.
 */
export function AudioFeatureBars({ features }: AudioFeatureBarsProps) {
  const rows = buildRows(features)
  const fillClass = features.isImputed ? 'bg-imputed' : 'bg-measured'
  const valueClass = features.isImputed ? 'text-imputed' : 'text-text-dim'

  return (
    <div className="space-y-3.5">
      {rows.map((row, index) => (
        <div key={row.key} className="animate-rise space-y-1.5" style={{ animationDelay: `${index * 35}ms` }}>
          <div className="flex items-baseline justify-between gap-3">
            <span className="label-micro">{row.label}</span>
            <span className={cn('font-mono text-xs', valueClass)}>{row.display}</span>
          </div>
          <div className="h-1.5 w-full overflow-hidden rounded-full bg-surface-2">
            <div
              className={cn('animate-sweep h-full rounded-full', fillClass)}
              style={{ width: `${row.fill * 100}%` }}
            />
          </div>
        </div>
      ))}

      <dl className="grid grid-cols-3 gap-4 border-t border-line/60 pt-4">
        <div className="space-y-1">
          <dt className="label-micro">Key</dt>
          <dd className="font-mono text-sm text-text-dim">
            {features.key >= 0 && features.key < PITCH_CLASSES.length ? PITCH_CLASSES[features.key] : 'n/a'}
          </dd>
        </div>
        <div className="space-y-1">
          <dt className="label-micro">Mode</dt>
          <dd className="font-mono text-sm text-text-dim">{features.mode === 1 ? 'Major' : 'Minor'}</dd>
        </div>
        <div className="space-y-1">
          <dt className="label-micro">Time signature</dt>
          <dd className="font-mono text-sm text-text-dim">
            {features.timeSignature > 0 ? `${features.timeSignature}/4` : 'n/a'}
          </dd>
        </div>
      </dl>
    </div>
  )
}
