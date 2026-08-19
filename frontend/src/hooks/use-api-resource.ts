import { useCallback, useEffect, useState } from 'react'
import { toApiError, type ApiError } from '@/api/api-error'

export type ResourceState<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'failed'; error: ApiError }

export interface ApiResource<T> {
  state: ResourceState<T>
  reload: () => void
}

export type ResourceFetcher<T> = (signal: AbortSignal) => Promise<T>

/**
 * The lifecycle of an API read reduced to a closed state machine, so no screen has to combine
 * `isLoading` with a null `data` by hand. Cancels the in-flight request on unmount and on
 * reload, and converts any rejection into `ApiError`.
 *
 * The `fetcher` participates in the dependencies: memoize it with `useCallback` in the caller.
 */
export function useApiResource<T>(fetcher: ResourceFetcher<T>): ApiResource<T> {
  const [state, setState] = useState<ResourceState<T>>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  const reload = useCallback(() => setAttempt((previous) => previous + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    setState({ status: 'loading' })

    fetcher(controller.signal)
      .then((data) => {
        if (active) {
          setState({ status: 'ready', data })
        }
      })
      .catch((error: unknown) => {
        if (active && !controller.signal.aborted) {
          setState({ status: 'failed', error: toApiError(error) })
        }
      })

    return () => {
      active = false
      controller.abort()
    }
  }, [fetcher, attempt])

  return { state, reload }
}
