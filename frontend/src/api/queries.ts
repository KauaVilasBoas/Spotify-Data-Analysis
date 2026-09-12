import { useQuery, type UseQueryOptions } from '@tanstack/react-query'
import { useMemo } from 'react'
import { toApiError } from './api-error'
import type { ApiResource } from '@/hooks/use-api-resource'
import {
  getTrackById,
  searchTracks,
  type TrackDetail,
  type TrackListItem,
  type TrackSort,
} from './catalog'
import type { PagedResult } from './contracts'
import {
  getAlbumYearInsights,
  getArtistInsights,
  getAudioFeatureDistribution,
  getCatalogSummary,
  getFeatureCorrelations,
  getGenreInsights,
  getPopularityRanking,
  type AlbumYearInsightItem,
  type ArtistInsightItem,
  type AudioFeatureDistribution,
  type AudioFeatureName,
  type CatalogSummary,
  type ContinuousAudioFeatureName,
  type FeatureCorrelations,
  type GenreInsightItem,
  type PopularityRankingItem,
} from './insights'
import {
  getCurrentModel,
  getDatasetStats,
  getTrackRecommendations,
  predictPopularity,
  resolveRecommendationParams,
  type DatasetStats,
  type ModelVersion,
  type PopularityPredictionResponse,
  type RecommendationParams,
  type TrackRecommendations,
} from './prediction'

export const queryKeys = {
  summary: ['insights', 'summary'] as const,
  popularity: (pageSize: number, genre: string | null) =>
    ['insights', 'popularity', pageSize, genre] as const,
  distribution: (
    feature: AudioFeatureName | ContinuousAudioFeatureName,
    buckets: number,
    includeImputed: boolean,
  ) => ['insights', 'distribution', feature, buckets, includeImputed] as const,
  correlations: (includeImputed: boolean) => ['insights', 'correlations', includeImputed] as const,
  genres: (pageSize: number) => ['insights', 'genres', pageSize] as const,
  albumsByYear: (pageSize: number) => ['insights', 'albums', 'by-year', pageSize] as const,
  artists: (pageSize: number, includeUnenriched: boolean) =>
    ['insights', 'artists', pageSize, includeUnenriched] as const,
  trackSearch: (search: string, pageSize: number) => ['catalog', 'tracks', search, pageSize] as const,
  catalogSearch: (search: string, page: number, sort: TrackSort, pageSize: number) =>
    ['catalog', 'search', search, page, sort, pageSize] as const,
  track: (id: string) => ['catalog', 'track', id] as const,
  model: ['prediction', 'model', 'current'] as const,
  datasetStats: ['prediction', 'dataset', 'stats'] as const,
  recommendations: (trackId: string, params: RecommendationParams) =>
    ['prediction', 'recommendations', trackId, params] as const,
  prediction: (trackId: string) => ['prediction', 'popularity', trackId] as const,
}

type QueryConfig<T> = Omit<UseQueryOptions<T, Error, T, readonly unknown[]>, 'queryKey' | 'queryFn'>

/**
 * Bridges TanStack Query onto the `ApiResource<T>` state machine the design system already
 * mandates, so `ResourceBoundary` remains the single place that renders loading and error.
 */
function useApiQuery<T>(
  queryKey: readonly unknown[],
  queryFn: (signal: AbortSignal) => Promise<T>,
  config?: QueryConfig<T>,
): ApiResource<T> {
  const query = useQuery<T, Error, T, readonly unknown[]>({
    queryKey,
    queryFn: ({ signal }) => queryFn(signal),
    ...config,
  })

  const { data, error, isPending, refetch } = query

  return useMemo(() => {
    if (isPending) {
      return { state: { status: 'loading' }, reload: () => void refetch() }
    }

    if (error !== null) {
      return { state: { status: 'failed', error: toApiError(error) }, reload: () => void refetch() }
    }

    return { state: { status: 'ready', data: data as T }, reload: () => void refetch() }
  }, [data, error, isPending, refetch])
}

export function useCatalogSummary(): ApiResource<CatalogSummary> {
  return useApiQuery(queryKeys.summary, getCatalogSummary)
}

export function usePopularityRanking(
  pageSize: number,
  genre: string | null = null,
): ApiResource<PagedResult<PopularityRankingItem>> {
  return useApiQuery(
    queryKeys.popularity(pageSize, genre),
    (signal) => getPopularityRanking(pageSize, genre, signal),
    { placeholderData: (previous) => previous },
  )
}

export function useAudioFeatureDistribution(
  feature: AudioFeatureName | ContinuousAudioFeatureName,
  buckets: number,
  includeImputed = false,
): ApiResource<AudioFeatureDistribution> {
  return useApiQuery(queryKeys.distribution(feature, buckets, includeImputed), (signal) =>
    getAudioFeatureDistribution(feature, buckets, includeImputed, signal),
  )
}

export function useFeatureCorrelations(includeImputed = false): ApiResource<FeatureCorrelations> {
  return useApiQuery(queryKeys.correlations(includeImputed), (signal) =>
    getFeatureCorrelations(includeImputed, signal),
  )
}

export function useGenreInsights(pageSize: number): ApiResource<PagedResult<GenreInsightItem>> {
  return useApiQuery(queryKeys.genres(pageSize), (signal) => getGenreInsights(pageSize, signal))
}

export function useAlbumYearInsights(
  pageSize: number,
): ApiResource<PagedResult<AlbumYearInsightItem>> {
  return useApiQuery(queryKeys.albumsByYear(pageSize), (signal) =>
    getAlbumYearInsights(pageSize, signal),
  )
}

export function useArtistInsights(
  pageSize: number,
  includeUnenriched = false,
): ApiResource<PagedResult<ArtistInsightItem>> {
  return useApiQuery(queryKeys.artists(pageSize, includeUnenriched), (signal) =>
    getArtistInsights(pageSize, includeUnenriched, signal),
  )
}

export function useTrackSearch(
  search: string,
  pageSize: number,
  enabled: boolean,
): ApiResource<PagedResult<TrackListItem>> {
  return useApiQuery(
    queryKeys.trackSearch(search, pageSize),
    (signal) => searchTracks({ search, page: 1, pageSize, sort: 'Name' }, signal),
    { enabled, placeholderData: (previous) => previous },
  )
}

/**
 * Catalog screen search: the full parameter set the page controls (paging + sort). Keeps the
 * previous page as placeholder so navigating pages does not flash the loading state, while the
 * server keeps its stable ordering (the client never re-sorts).
 */
export function useCatalogSearch(
  search: string,
  page: number,
  sort: TrackSort,
  pageSize: number,
): ApiResource<PagedResult<TrackListItem>> {
  return useApiQuery(
    queryKeys.catalogSearch(search, page, sort, pageSize),
    (signal) => searchTracks({ search, page, pageSize, sort }, signal),
    { placeholderData: (previous) => previous },
  )
}

export function useTrack(id: string | null): ApiResource<TrackDetail> {
  return useApiQuery(queryKeys.track(id ?? ''), (signal) => getTrackById(id as string, signal), {
    enabled: id !== null,
  })
}

export function useCurrentModel(): ApiResource<ModelVersion> {
  return useApiQuery(queryKeys.model, getCurrentModel)
}

export function useDatasetStats(): ApiResource<DatasetStats> {
  return useApiQuery(queryKeys.datasetStats, getDatasetStats)
}

export function useTrackRecommendations(
  trackId: string | null,
  params: RecommendationParams = {},
): ApiResource<TrackRecommendations> {
  // Defaults resolvidos aqui, antes de montar a chave, para que {} e os defaults
  // explícitos gerem a mesma entrada de cache.
  const resolved = resolveRecommendationParams(params)
  return useApiQuery(
    queryKeys.recommendations(trackId ?? '', resolved),
    (signal) => getTrackRecommendations(trackId as string, resolved, signal),
    { enabled: trackId !== null },
  )
}

/**
 * Query (não mutation) habilitada só quando há faixa selecionada.
 * Predição é idempotente por trackId — query é mais simples que mutation
 * e integra direto no ResourceBoundary sem estado local de submit.
 */
export function usePopularityPrediction(trackId: string | null): ApiResource<PopularityPredictionResponse> {
  return useApiQuery(
    queryKeys.prediction(trackId ?? ''),
    (signal) => predictPopularity(trackId as string, signal),
    { enabled: trackId !== null },
  )
}
