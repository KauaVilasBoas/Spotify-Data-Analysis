import { GitCompare, Sparkles } from 'lucide-react'
import type { TrackDetail } from '@/api/catalog'
import { AudioFeatureBars } from '@/components/catalog/AudioFeatureBars'
import { TrackArt } from '@/components/data/TrackArt'
import { Panel } from '@/components/foundation/Panel'
import { Button } from '@/components/ui/button'
import { formatDuration } from '@/lib/format'
import { cn } from '@/lib/utils'

interface TrackDetailPanelProps {
  track: TrackDetail
}

interface ProvenanceState {
  eyebrow: string
  swatch: string
  badgeClass: string
  note: string
}

/**
 * Absent, imputed and measured are three different domain states and the backend went out of its
 * way not to conflate them, so the UI must not either. This maps each state to its own label,
 * colour token and honest note.
 */
function resolveProvenance(track: TrackDetail): ProvenanceState {
  if (track.audioFeatures === null) {
    return {
      eyebrow: 'Audio features absent',
      swatch: 'bg-absent',
      badgeClass: 'border-absent/60 text-text-dim',
      note: 'This track was never matched to the external dataset, so it carries no audio features. Nothing below is estimated because there is nothing to estimate.',
    }
  }

  if (track.audioFeatures.isImputed) {
    return {
      eyebrow: 'Audio features imputed',
      swatch: 'bg-imputed',
      badgeClass: 'border-imputed/60 text-imputed',
      note: 'These values were estimated from the genre median, not measured from the audio. They are shown in amber so they are never read as observed data.',
    }
  }

  return {
    eyebrow: 'Audio features measured',
    swatch: 'bg-measured',
    badgeClass: 'border-measured/50 text-measured',
    note: 'These values were measured from the audio and carried through the dataset.',
  }
}

function metaValue(value: string | null): string {
  return value !== null && value.trim().length > 0 ? value : 'n/a'
}

export function TrackDetailPanel({ track }: TrackDetailPanelProps) {
  const provenance = resolveProvenance(track)
  const secondaryArtists = track.artists.slice(1)

  return (
    <div className="space-y-6">
      <Panel bodyClassName="p-0">
        <div className="flex flex-col gap-5 p-5 sm:flex-row sm:items-center">
          <TrackArt
            trackId={track.trackId}
            features={track.audioFeatures}
            size={112}
            animated
            className="self-start"
          />

          <div className="min-w-0 flex-1 space-y-2">
            <p className="label-micro">Track</p>
            <h2 className="text-2xl leading-tight font-semibold tracking-tight text-text">
              {track.name}
            </h2>
            <p className="truncate text-sm text-text-dim">
              {track.artists.length > 0 ? track.artists.map((artist) => artist.name).join(', ') : 'n/a'}
            </p>

            <div className="flex flex-wrap items-center gap-2 pt-1.5">
              <span
                className={cn(
                  'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[0.7rem] font-medium',
                  provenance.badgeClass,
                )}
              >
                <span className={cn('size-2 rounded-full', provenance.swatch)} aria-hidden="true" />
                {provenance.eyebrow}
              </span>
              {track.explicit && (
                <span className="rounded-full border border-line-strong px-2.5 py-1 text-[0.7rem] font-medium text-text-faint">
                  Explicit
                </span>
              )}
            </div>
          </div>

          <div className="shrink-0 text-right">
            <p className="label-micro">Popularity</p>
            <p className="numeral text-[2.5rem] text-text">{track.popularity}</p>
          </div>
        </div>

        <dl className="grid grid-cols-2 gap-x-6 gap-y-4 border-t border-line/60 px-5 py-4 sm:grid-cols-4">
          <div className="space-y-1">
            <dt className="label-micro">Duration</dt>
            <dd className="font-mono text-sm text-text-dim">{formatDuration(track.durationMs)}</dd>
          </div>
          <div className="space-y-1">
            <dt className="label-micro">ISRC</dt>
            <dd className="font-mono text-sm text-text-dim">{metaValue(track.isrc)}</dd>
          </div>
          <div className="space-y-1">
            <dt className="label-micro">Genre</dt>
            <dd className="font-mono text-sm text-text-dim">
              {metaValue(track.audioFeatures?.genre ?? null)}
            </dd>
          </div>
          <div className="space-y-1">
            <dt className="label-micro">Track id</dt>
            <dd className="truncate font-mono text-xs text-text-faint" title={track.trackId}>
              {track.trackId}
            </dd>
          </div>
        </dl>

        {secondaryArtists.length > 0 && (
          <div className="border-t border-line/60 px-5 py-4">
            <p className="label-micro mb-2">Also credited</p>
            <div className="flex flex-wrap gap-2">
              {secondaryArtists.map((artist) => (
                <span
                  key={artist.id}
                  className="rounded-full border border-line-strong px-2.5 py-1 text-xs text-text-dim"
                >
                  {artist.name}
                </span>
              ))}
            </div>
          </div>
        )}
      </Panel>

      <Panel eyebrow={provenance.eyebrow} title="Audio profile">
        <div
          className={cn(
            'mb-5 flex gap-3 rounded-[var(--radius-sm)] border p-3.5',
            track.audioFeatures === null
              ? 'border-absent/50 bg-absent/10'
              : track.audioFeatures.isImputed
                ? 'border-imputed/40 bg-imputed/[0.07]'
                : 'border-measured/30 bg-measured/[0.06]',
          )}
        >
          <span className={cn('mt-1 size-2 shrink-0 rounded-full', provenance.swatch)} aria-hidden="true" />
          <p className="text-sm leading-relaxed text-text-dim">{provenance.note}</p>
        </div>

        {track.audioFeatures !== null ? (
          <AudioFeatureBars features={track.audioFeatures} />
        ) : (
          <p className="py-2 text-sm text-text-faint">
            The nine continuous audio features are unavailable for this track.
          </p>
        )}
      </Panel>

      <Panel eyebrow="Next" title="Use this track as a seed">
        <p className="mb-4 max-w-prose text-sm text-text-dim">
          The catalog is the entry point for the other tools: pick a track here and carry its id
          into recommendations and popularity prediction, instead of pasting an id by hand.
        </p>
        <div className="flex flex-wrap gap-3">
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled
            title="Arrives with the recommendations screen (E5.4)"
            className="border-line-strong bg-transparent"
          >
            <GitCompare aria-hidden="true" />
            Find similar tracks
            <span className="label-micro ml-1 text-text-faint">soon</span>
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled
            title="Arrives with the model screen (E5.3)"
            className="border-line-strong bg-transparent"
          >
            <Sparkles aria-hidden="true" />
            Predict popularity
            <span className="label-micro ml-1 text-text-faint">soon</span>
          </Button>
        </div>
      </Panel>
    </div>
  )
}
