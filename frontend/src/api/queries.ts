import { useQuery, type UseQueryOptions } from '@tanstack/react-query'
import { useMemo } from 'react'
import { toApiError } from './api-error'
import type { ApiResource } from '@/hooks/use-api-resource'
import { getTrackById, searchTracks, type TrackDetail, type TrackListItem } from './catalog'
import type { PagedResult } from './contracts'
import {
  getAudioFeatureDistribution,
  getCatalogSummary,
  getFeatureCorrelations,
  getGenreInsights,
  getPopularityRanking,
  type AudioFeatureDistribution,
  type AudioFeatureName,
  type CatalogSummary,
  type FeatureCorrelations,
  type GenreInsightItem,
  type PopularityRankingItem,
} from './insights'
import {
  getCurrentModel,
  getDatasetStats,
  getTrackRecommendations,
  type DatasetStats,
  type ModelVersion,
  type TrackRecommendations,
} from './prediction'

export const queryKeys = {
  summary: ['insights', 'summary'] as const,
  popularity: (pageSize: number) => ['insights', 'popularity', pageSize] as const,
  distribution: (feature: AudioFeatureName, buckets: number) =>
    ['insights', 'distribution', feature, buckets] as const,
  correlations: ['insights', 'correlations'] as const,
  genres: (pageSize: number) => ['insights', 'genres', pageSize] as const,
  trackSearch: (search: string, pageSize: number) => ['catalog', 'tracks', search, pageSize] as const,
  track: (id: string) => ['catalog', 'track', id] as const,
  model: ['prediction', 'model', 'current'] as const,
  datasetStats: ['prediction', 'dataset', 'stats'] as const,
  recommendations: (trackId: string, limit: number) =>
    ['prediction', 'recommendations', trackId, limit] as const,
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

export function usePopularityRanking(pageSize: number): ApiResource<PagedResult<PopularityRankingItem>> {
  return useApiQuery(queryKeys.popularity(pageSize), (signal) =>
    getPopularityRanking(pageSize, signal),
  )
}

export function useAudioFeatureDistribution(
  feature: AudioFeatureName,
  buckets: number,
): ApiResource<AudioFeatureDistribution> {
  return useApiQuery(queryKeys.distribution(feature, buckets), (signal) =>
    getAudioFeatureDistribution(feature, buckets, signal),
  )
}

export function useFeatureCorrelations(): ApiResource<FeatureCorrelations> {
  return useApiQuery(queryKeys.correlations, getFeatureCorrelations)
}

export function useGenreInsights(pageSize: number): ApiResource<PagedResult<GenreInsightItem>> {
  return useApiQuery(queryKeys.genres(pageSize), (signal) => getGenreInsights(pageSize, signal))
}

export function useTrackSearch(
  search: string,
  pageSize: number,
  enabled: boolean,
): ApiResource<PagedResult<TrackListItem>> {
  return useApiQuery(
    queryKeys.trackSearch(search, pageSize),
    (signal) => searchTracks(search, pageSize, signal),
    { enabled, placeholderData: (previous) => previous },
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
  limit: number,
): ApiResource<TrackRecommendations> {
  return useApiQuery(
    queryKeys.recommendations(trackId ?? '', limit),
    (signal) => getTrackRecommendations(trackId as string, limit, signal),
    { enabled: trackId !== null },
  )
}
