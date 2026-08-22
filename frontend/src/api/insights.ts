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
  totalConsidered: number
  measuredCount: number
  imputedCount: number
  includedImputed: boolean
  minValue: number | null
  maxValue: number | null
}

export interface FeatureCorrelation {
  feature: string
  /** Null when PostgreSQL cannot compute `corr()` (fewer than two pairs or zero variance). Never coerce to zero. */
  coefficient: number | null
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

/** Mirrors `AlbumYearInsightItem`. `year` is null for the bucket of tracks with no known release year. */
export interface AlbumYearInsightItem {
  year: number | null
  averagePopularity: number
  trackCount: number
  albumCount: number
}

/** Mirrors `ArtistInsightItem`. `isEnriched` tells real Spotify figures apart from the zeros of an un-enriched artist. */
export interface ArtistInsightItem {
  artistId: string
  name: string
  popularity: number
  followers: number
  isEnriched: boolean
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

/**
 * The nine continuous audio features the correlation endpoint reports, in the same order the API
 * returns them. `tempo` (BPM) and `loudness` (dB) live on their own scales, but the distribution
 * endpoint buckets each feature between its own observed min and max, so they render just fine.
 * The API binds the enum case-insensitively, so lowercase names travel on the URL as usual.
 */
export const CONTINUOUS_AUDIO_FEATURES = [
  'danceability',
  'energy',
  'valence',
  'tempo',
  'acousticness',
  'instrumentalness',
  'liveness',
  'speechiness',
  'loudness',
] as const

export type ContinuousAudioFeatureName = (typeof CONTINUOUS_AUDIO_FEATURES)[number]

export function getCatalogSummary(signal?: AbortSignal): Promise<CatalogSummary> {
  return getResource<CatalogSummary>({ path: '/api/insights/summary', signal })
}

export function getPopularityRanking(
  pageSize: number,
  genre: string | null,
  signal?: AbortSignal,
): Promise<PagedResult<PopularityRankingItem>> {
  return getResource<PagedResult<PopularityRankingItem>>({
    path: '/api/insights/popularity/top',
    query: { page: 1, pageSize, genre: genre ?? undefined },
    signal,
  })
}

export function getAudioFeatureDistribution(
  feature: AudioFeatureName | ContinuousAudioFeatureName,
  buckets: number,
  includeImputed: boolean,
  signal?: AbortSignal,
): Promise<AudioFeatureDistribution> {
  return getResource<AudioFeatureDistribution>({
    path: `/api/insights/distributions/${feature}`,
    query: { buckets, includeImputed },
    signal,
  })
}

export function getFeatureCorrelations(
  includeImputed: boolean,
  signal?: AbortSignal,
): Promise<FeatureCorrelations> {
  return getResource<FeatureCorrelations>({
    path: '/api/insights/correlations',
    query: { includeImputed },
    signal,
  })
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

export function getAlbumYearInsights(
  pageSize: number,
  signal?: AbortSignal,
): Promise<PagedResult<AlbumYearInsightItem>> {
  return getResource<PagedResult<AlbumYearInsightItem>>({
    path: '/api/insights/albums/by-year',
    query: { page: 1, pageSize, sort: 'YearAsc' },
    signal,
  })
}

export function getArtistInsights(
  pageSize: number,
  includeUnenriched: boolean,
  signal?: AbortSignal,
): Promise<PagedResult<ArtistInsightItem>> {
  return getResource<PagedResult<ArtistInsightItem>>({
    path: '/api/insights/artists',
    query: { page: 1, pageSize, includeUnenriched },
    signal,
  })
}
