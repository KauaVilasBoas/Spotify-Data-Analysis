import { getResource } from './http-client'

/** Espelha `CatalogSummaryResult` do módulo Analytics. Contagens são `long` no .NET. */
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

export function getCatalogSummary(signal?: AbortSignal): Promise<CatalogSummary> {
  return getResource<CatalogSummary>({ path: '/api/insights/summary', signal })
}
