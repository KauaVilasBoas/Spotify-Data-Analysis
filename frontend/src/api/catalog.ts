import type { PagedResult } from './contracts'
import { getResource } from './http-client'

export interface TrackListItem {
  trackId: string
  name: string
  primaryArtist: string
  popularity: number
  durationMs: number
  explicit: boolean
  albumId: string | null
  hasAudioFeatures: boolean
}

export interface TrackAudioFeatures {
  danceability: number
  energy: number
  valence: number
  tempo: number
  acousticness: number
  instrumentalness: number
  liveness: number
  speechiness: number
  loudness: number
  key: number
  mode: number
  timeSignature: number
  genre: string | null
  source: string
  isImputed: boolean
}

export interface TrackCredit {
  id: string
  name: string
}

export interface TrackDetail {
  trackId: string
  name: string
  popularity: number
  durationMs: number
  explicit: boolean
  isrc: string | null
  albumId: string | null
  artists: TrackCredit[]
  audioFeatures: TrackAudioFeatures | null
}

/**
 * Sort order accepted by `GET /api/tracks?sort=`. The API writes enums by name, so the
 * wire values match these string literals verbatim.
 */
export type TrackSort = 'Name' | 'PopularityDesc'

export interface TrackSearchParams {
  search: string
  page: number
  pageSize: number
  sort: TrackSort
}

/**
 * The catalog search parameter is `search`, confirmed against the running API. Sending
 * `searchTerm` is silently ignored and returns the entire unfiltered catalog (and, since E6.3,
 * an unknown parameter is rejected with 400 instead of quietly returning everything).
 *
 * `search` matches by case-insensitive substring against the track name or any credited artist,
 * so one input covers both. Paging is stable on the server (id tie-break); the client never
 * re-sorts, or that guarantee would be lost.
 */
export function searchTracks(
  { search, page, pageSize, sort }: TrackSearchParams,
  signal?: AbortSignal,
): Promise<PagedResult<TrackListItem>> {
  return getResource<PagedResult<TrackListItem>>({
    path: '/api/tracks',
    query: {
      search: search.trim().length > 0 ? search.trim() : undefined,
      page,
      pageSize,
      sort,
    },
    signal,
  })
}

export function getTrackById(id: string, signal?: AbortSignal): Promise<TrackDetail> {
  return getResource<TrackDetail>({ path: `/api/tracks/${encodeURIComponent(id)}`, signal })
}
