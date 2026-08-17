import type { PagedResult } from './contracts'
import { getResource } from './http-client'

/** Mirrors `CatalogSummaryResult` from the Analytics module. Counts are `long` on .NET. */
export interface CatalogSummary {
  totalTracks: number
  tracksWithAudioFeatures: number
  tracksWithoutAudioFeatures: number
  tracksWithMeasuredFeatures: number
  tracksWithImputedFeatures: number
  distinctArtists: number
  distinctAlbums: number
  distinctGenres: number
}

export interface PopularityRankingItem {
  trackId: string
  name: string
  primaryArtist: string
  popularity: number
  genre: string | null
}

export interface DistributionBucket {
  index: number
  lowerBound: number
  upperBound: number
  count: number
}

export interface AudioFeatureDistribution {
  feature: string
  buckets: DistributionBucket[]
}

export interface FeatureCorrelation {
  feature: string
  coefficient: number
  n: number
}

export interface FeatureCorrelations {
  correlations: FeatureCorrelation[]
  consideredCount: number
  measuredCount: number
  imputedCount: number
  includedImputed: boolean
}

export interface GenreInsightItem {
  genre: string
  averagePopularity: number
  medianPopularity: number
  trackCount: number
  imputedTrackCount: number
}

export const AUDIO_FEATURES = [
  'danceability',
  'energy',
  'valence',
  'acousticness',
  'instrumentalness',
  'liveness',
  'speechiness',
] as const

export type AudioFeatureName = (typeof AUDIO_FEATURES)[number]

export function getCatalogSummary(signal?: AbortSignal): Promise<CatalogSummary> {
  return getResource<CatalogSummary>({ path: '/api/insights/summary', signal })
}

export function getPopularityRanking(
  pageSize: number,
  signal?: AbortSignal,
): Promise<PagedResult<PopularityRankingItem>> {
  return getResource<PagedResult<PopularityRankingItem>>({
    path: '/api/insights/popularity/top',
    query: { page: 1, pageSize },
    signal,
  })
}

export function getAudioFeatureDistribution(
  feature: AudioFeatureName,
  buckets: number,
  signal?: AbortSignal,
): Promise<AudioFeatureDistribution> {
  return getResource<AudioFeatureDistribution>({
    path: `/api/insights/distributions/${feature}`,
    query: { buckets },
    signal,
  })
}

export function getFeatureCorrelations(signal?: AbortSignal): Promise<FeatureCorrelations> {
  return getResource<FeatureCorrelations>({ path: '/api/insights/correlations', signal })
}

export function getGenreInsights(
  pageSize: number,
  signal?: AbortSignal,
): Promise<PagedResult<GenreInsightItem>> {
  return getResource<PagedResult<GenreInsightItem>>({
    path: '/api/insights/genres',
    query: { page: 1, pageSize },
    signal,
  })
}
