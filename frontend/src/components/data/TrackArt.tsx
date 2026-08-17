import { useId, useMemo } from 'react'
import type { TrackAudioFeatures } from '@/api/catalog'
import { buildTrackArtSpec } from '@/lib/track-art'
import { cn } from '@/lib/utils'

interface TrackArtProps {
  trackId: string
  features?: TrackAudioFeatures | null
  size?: number
  className?: string
  animated?: boolean
}

const CANVAS = 100

/**
 * Generative cover artwork derived from the track's own audio features. The dataset ships no
 * album imagery, so rather than a borrowed or placeholder image every track renders a unique,
 * deterministic bloom: lobes from danceability, amplitude from energy, hue from musical key
 * and valence, rotation from tempo.
 */
export function TrackArt({ trackId, features, size = 64, className, animated = false }: TrackArtProps) {
  const gradientId = useId()
  const spec = useMemo(() => buildTrackArtSpec(trackId, features ?? null, CANVAS), [trackId, features])

  const shell = `hsl(${spec.hue} 70% 8%)`
  const primary = `hsl(${spec.hue} 82% 58%)`
  const accent = `hsl(${spec.hueAccent} 85% 62%)`

  return (
    <svg
      viewBox={`0 0 ${CANVAS} ${CANVAS}`}
      width={size}
      height={size}
      role="img"
      aria-label={`Generated artwork for this track, derived from its audio features`}
      className={cn('shrink-0 overflow-hidden rounded-[22%]', className)}
    >
      <defs>
        <radialGradient id={`${gradientId}-bg`} cx="30%" cy="24%" r="92%">
          <stop offset="0%" stopColor={`hsl(${spec.hue} 60% 18%)`} />
          <stop offset="100%" stopColor={shell} />
        </radialGradient>
        <linearGradient id={`${gradientId}-fill`} x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor={primary} stopOpacity={0.95} />
          <stop offset="100%" stopColor={accent} stopOpacity={0.75} />
        </linearGradient>
        <filter id={`${gradientId}-blur`} x="-40%" y="-40%" width="180%" height="180%">
          <feGaussianBlur stdDeviation={2 + spec.glow * 3} />
        </filter>
      </defs>

      <rect width={CANVAS} height={CANVAS} fill={`url(#${gradientId}-bg)`} />

      {Array.from({ length: spec.rings }, (_, index) => (
        <circle
          key={index}
          cx={CANVAS / 2}
          cy={CANVAS / 2}
          r={16 + index * 9}
          fill="none"
          stroke={accent}
          strokeOpacity={0.12}
          strokeWidth={0.6}
        />
      ))}

      <g
        style={
          animated
            ? { transformOrigin: 'center', animation: 'art-drift 24s linear infinite' }
            : undefined
        }
      >
        <path d={spec.outline} fill={`url(#${gradientId}-fill)`} opacity={0.34} filter={`url(#${gradientId}-blur)`} />
        <path
          d={spec.outline}
          fill="none"
          stroke={`url(#${gradientId}-fill)`}
          strokeWidth={1.4}
          strokeOpacity={0.9}
        />
        <path d={spec.corePath} fill={`url(#${gradientId}-fill)`} opacity={0.9} />
      </g>

      <rect
        width={CANVAS}
        height={CANVAS}
        fill="none"
        stroke="rgb(255 255 255 / 0.08)"
        strokeWidth={1}
      />
    </svg>
  )
}
