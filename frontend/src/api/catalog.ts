import { getResource } from './http-client'

/** Subconjunto de `TrackDetailResult` suficiente para a sonda de erro 404 da home. */
export interface TrackDetail {
  trackId: string
  name: string
  popularity: number
}

export function getTrackById(id: string, signal?: AbortSignal): Promise<TrackDetail> {
  return getResource<TrackDetail>({ path: `/api/tracks/${encodeURIComponent(id)}`, signal })
}
