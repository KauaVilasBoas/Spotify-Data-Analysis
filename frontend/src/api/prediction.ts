import { getResource } from './http-client'

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
  effectiveGenreMode: string
  effectiveStrategy: string
  genreFellBackToCosineOnly: boolean
  collaborativeSignalUnavailable: boolean
  dedupeApplied: boolean
  totalDuplicatesCollapsed: number
  recommendations: RecommendationItem[]
}

export function getCurrentModel(signal?: AbortSignal): Promise<ModelVersion> {
  return getResource<ModelVersion>({ path: '/api/model/current', signal })
}

export function getDatasetStats(signal?: AbortSignal): Promise<DatasetStats> {
  return getResource<DatasetStats>({ path: '/api/predictions/dataset/stats', signal })
}

export function getTrackRecommendations(
  trackId: string,
  limit: number,
  signal?: AbortSignal,
): Promise<TrackRecommendations> {
  return getResource<TrackRecommendations>({
    path: `/api/recommendations/track/${encodeURIComponent(trackId)}`,
    query: { limit },
    signal,
  })
}
