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
 * The catalog search parameter is `search` — confirmed against the running API. Sending
 * `searchTerm` is silently ignored and returns the entire unfiltered catalog.
 */
export function searchTracks(
  search: string,
  pageSize: number,
  signal?: AbortSignal,
): Promise<PagedResult<TrackListItem>> {
  return getResource<PagedResult<TrackListItem>>({
    path: '/api/tracks',
    query: { search: search.trim().length > 0 ? search.trim() : undefined, page: 1, pageSize },
    signal,
  })
}

export function getTrackById(id: string, signal?: AbortSignal): Promise<TrackDetail> {
  return getResource<TrackDetail>({ path: `/api/tracks/${encodeURIComponent(id)}`, signal })
}
