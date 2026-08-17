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
 * Ciclo de vida de uma leitura da API reduzido a uma máquina de estados fechada, para nenhuma tela
 * precisar combinar `isLoading` com `data` nula na mão. Cancela a requisição em curso ao desmontar e
 * ao recarregar, e converte qualquer rejeição em `ApiError`.
 *
 * O `fetcher` participa das dependências: memorize-o com `useCallback` no chamador.
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
