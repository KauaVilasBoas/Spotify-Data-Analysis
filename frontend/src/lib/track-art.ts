import type { TrackAudioFeatures } from '@/api/catalog'

export interface TrackArtSpec {
  hue: number
  hueAccent: number
  petals: number
  rings: number
  rotation: number
  jitter: number
  glow: number
  outline: string
  petalPath: string
  corePath: string
  radii: number[]
}

const FEATURE_ORDER = [
  'danceability',
  'energy',
  'valence',
  'acousticness',
  'instrumentalness',
  'liveness',
  'speechiness',
] as const

/**
 * FNV-1a over the track id. The catalog has no cover artwork at all: every album row is
 * `is_enriched = false` and the schema carries no image column, so identity has to be derived,
 * and it has to be stable across renders and sessions.
 */
function hashTrackId(trackId: string): number {
  let hash = 0x811c9dc5

  for (let index = 0; index < trackId.length; index += 1) {
    hash ^= trackId.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }

  return hash >>> 0
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value))
}

interface SyntheticFeatures {
  energy: number
  valence: number
  danceability: number
  acousticness: number
  tempo: number
  loudness: number
  key: number
  hue: number
}

/**
 * Deterministic unit-interval stream derived from the id hash, used to stand in for audio
 * features on the list endpoints, which return track identity but no feature vector. The art
 * therefore stays unique and stable per track whether or not features were loaded.
 */
function seededUnits(seed: number): SyntheticFeatures {
  let state = seed === 0 ? 0x9e3779b9 : seed

  const nextUnit = (): number => {
    state ^= state << 13
    state >>>= 0
    state ^= state >>> 17
    state ^= state << 5
    state >>>= 0

    return state / 0xffffffff
  }

  return {
    energy: nextUnit(),
    valence: nextUnit(),
    danceability: nextUnit(),
    acousticness: nextUnit(),
    tempo: nextUnit(),
    loudness: nextUnit(),
    key: nextUnit(),
    hue: nextUnit(),
  }
}

function catmullRomLoop(points: Array<[number, number]>): string {
  const count = points.length

  if (count === 0) {
    return ''
  }

  const at = (index: number): [number, number] =>
    points[((index % count) + count) % count] as [number, number]
  const segments: string[] = [`M ${at(0)[0].toFixed(2)} ${at(0)[1].toFixed(2)}`]

  for (let index = 0; index < count; index += 1) {
    const p0 = at(index - 1)
    const p1 = at(index)
    const p2 = at(index + 1)
    const p3 = at(index + 2)

    const c1x = p1[0] + (p2[0] - p0[0]) / 6
    const c1y = p1[1] + (p2[1] - p0[1]) / 6
    const c2x = p2[0] - (p3[0] - p1[0]) / 6
    const c2y = p2[1] - (p3[1] - p1[1]) / 6

    segments.push(
      `C ${c1x.toFixed(2)} ${c1y.toFixed(2)} ${c2x.toFixed(2)} ${c2y.toFixed(2)} ${p2[0].toFixed(2)} ${p2[1].toFixed(2)}`,
    )
  }

  return `${segments.join(' ')} Z`
}

function radialPolygon(radii: number[], center: number, rotation: number): Array<[number, number]> {
  const step = (Math.PI * 2) / radii.length

  return radii.map((radius, index) => {
    const angle = index * step + rotation
    return [center + Math.cos(angle) * radius, center + Math.sin(angle) * radius] as [number, number]
  })
}

/**
 * Derives a deterministic visual identity from a track's measured audio features.
 * Same track id plus same features always yields the same artwork.
 */
export function buildTrackArtSpec(
  trackId: string,
  features: TrackAudioFeatures | null,
  size = 100,
): TrackArtSpec {
  const hash = hashTrackId(trackId)
  const center = size / 2
  const maxRadius = size * 0.42

  const seeded = seededUnits(hash)

  const energy = clamp01(features?.energy ?? seeded.energy)
  const valence = clamp01(features?.valence ?? seeded.valence)
  const danceability = clamp01(features?.danceability ?? seeded.danceability)
  const acousticness = clamp01(features?.acousticness ?? seeded.acousticness)
  const tempo = features?.tempo ?? 70 + seeded.tempo * 110
  const loudness = features?.loudness ?? -22 + seeded.loudness * 20
  const musicalKey = features?.key ?? Math.floor(seeded.key * 12)

  const hue = (seeded.hue * 360 + musicalKey * 12 + valence * 40) % 360
  const hueAccent = (hue + 35 + energy * 85) % 360

  const resolution = 96
  const lobes = 3 + Math.round(danceability * 5)
  const jitter = 0.08 + acousticness * 0.16

  const radii: number[] = []

  for (let index = 0; index < resolution; index += 1) {
    const angle = (index / resolution) * Math.PI * 2
    const lobeWave = Math.sin(angle * lobes) * (0.1 + energy * 0.22)
    const secondary = Math.sin(angle * (lobes * 2 + 1) + hash * 0.0001) * jitter * 0.5
    const breathing = Math.cos(angle * 2 + valence * Math.PI) * 0.05

    radii.push(maxRadius * (0.62 + lobeWave + secondary + breathing))
  }

  const rotation = ((tempo % 60) / 60) * Math.PI * 2
  const corePoints = radialPolygon(
    radii.map((radius) => radius * (0.3 + acousticness * 0.22)),
    center,
    -rotation * 0.5,
  )

  return {
    hue,
    hueAccent,
    petals: lobes,
    rings: 1 + (musicalKey % 4),
    rotation: (rotation * 180) / Math.PI,
    jitter,
    glow: clamp01((loudness + 24) / 24),
    outline: catmullRomLoop(radialPolygon(radii, center, rotation)),
    petalPath: catmullRomLoop(radialPolygon(radii, center, rotation)),
    corePath: catmullRomLoop(corePoints),
    radii,
  }
}

export function featureVector(features: TrackAudioFeatures): Array<{ name: string; value: number }> {
  return FEATURE_ORDER.map((name) => ({ name, value: clamp01(features[name]) }))
}
