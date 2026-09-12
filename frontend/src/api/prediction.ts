import { getResource, postResource } from './http-client'

export interface RegressionMetrics {
  rSquared: number
  meanAbsoluteError: number
  rootMeanSquaredError: number
}

export interface FeatureImportanceItem {
  feature: string
  slotCount: number
  rSquaredDropMean: number
  rSquaredDropStandardDeviation: number
  meanAbsoluteErrorIncreaseMean: number
  meanAbsoluteErrorIncreaseStandardDeviation: number
}

export interface ModelVersion {
  version: number
  trainedAtUtc: string
  trainer: string
  features: string[]
  featureImportance: FeatureImportanceItem[]
  seed: number
  testFraction: number
  trainingSampleCount: number
  testSampleCount: number
  trainedOnImputed: boolean
  model: RegressionMetrics
  baseline: RegressionMetrics
  artifactHash: string
  artifactSizeBytes: number
}

export interface DatasetStats {
  totalTracks: number
  excludedMissingPopularity: number
  excludedMissingAudioFeatures: number
  excludedIncompleteAudioFeatures: number
  eligibleWithMeasuredFeatures: number
  eligibleWithImputedFeatures: number
  eligibleIncludingImputed: number
  excludedByImputationPolicy: number
  trainableTracks: number
  trainingSampleCount: number
  testSampleCount: number
  includedImputed: boolean
  seed: number
  testFraction: number
}

export interface RecommendationFeatureContribution {
  feature: string
  seedValue: number
  candidateValue: number
  contribution: number
}

export interface RecommendationItem {
  trackId: string
  name: string
  artist: string
  album: string | null
  genre: string | null
  score: number
  cosineScore: number
  genreBoost: number
  isImputed: boolean
  sharedGenre: string | null
  topFeatures: RecommendationFeatureContribution[]
  equivalentVersionsCollapsed: number
  coPlaylists: number
  coOccurrenceScore: number
}

export interface TrackRecommendations {
  seedTrackId: string
  seedName: string
  seedArtist: string
  seedGenre: string | null
  seedIsImputed: boolean
  indexedTrackCount: number
  requestedGenreMode: string
  effectiveGenreMode: string
  effectiveStrategy: string
  genreFellBackToCosineOnly: boolean
  collaborativeSignalUnavailable: boolean
  dedupeApplied: boolean
  totalDuplicatesCollapsed: number
  recommendations: RecommendationItem[]
  warnings: string[]
}

export interface PopularityPredictionResponse {
  predictedPopularity: number
  rawScore: number
  wasClamped: boolean
  modelVersion: number
  mode: string
  warnings: string[]
}

export function getCurrentModel(signal?: AbortSignal): Promise<ModelVersion> {
  return getResource<ModelVersion>({ path: '/api/model/current', signal })
}

export function getDatasetStats(signal?: AbortSignal): Promise<DatasetStats> {
  return getResource<DatasetStats>({ path: '/api/predictions/dataset/stats', signal })
}

export interface RecommendationParams {
  limit?: number
  explainTopK?: number
  genreMode?: 'boost' | 'off' | 'sameGenreOnly'
  dedupe?: boolean
  strategy?: 'content' | 'blend'
  blendWeight?: number
}

// Defaults de todos os parâmetros de recomendação — aplicados uma única vez aqui,
// antes de montar a chave de cache, para que {} e os defaults explícitos mapeiem
// para a mesma entrada no TanStack Query.
export function resolveRecommendationParams(params: RecommendationParams): Required<RecommendationParams> {
  return {
    limit: params.limit ?? 10,
    explainTopK: params.explainTopK ?? 3,
    genreMode: params.genreMode ?? 'boost',
    dedupe: params.dedupe ?? true,
    strategy: params.strategy ?? 'content',
    blendWeight: params.blendWeight ?? 0.35,
  }
}

export function getTrackRecommendations(
  trackId: string,
  params: RecommendationParams = {},
  signal?: AbortSignal,
): Promise<TrackRecommendations> {
  const resolved = resolveRecommendationParams(params)
  return getResource<TrackRecommendations>({
    path: `/api/recommendations/track/${encodeURIComponent(trackId)}`,
    query: resolved,
    signal,
  })
}

export function predictPopularity(
  trackId: string,
  signal?: AbortSignal,
): Promise<PopularityPredictionResponse> {
  return postResource<PopularityPredictionResponse>({
    path: '/api/predictions/popularity',
    body: { trackId },
    signal,
  })
}
