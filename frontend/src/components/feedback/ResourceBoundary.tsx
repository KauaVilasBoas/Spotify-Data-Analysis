import type { ReactNode } from 'react'
import type { ApiResource } from '@/hooks/use-api-resource'
import { ErrorState } from './ErrorState'
import { LoadingState } from './LoadingState'

interface ResourceBoundaryProps<T> {
  resource: ApiResource<T>
  loadingLabel?: string
  loadingRows?: number
  children: (data: T) => ReactNode
}

/**
 * Traduz `ResourceState<T>` para os componentes compartilhados de carregamento e erro, deixando a
 * tela responsável apenas pelo caso de sucesso. É o único lugar da SPA que faz esse mapeamento.
 */
export function ResourceBoundary<T>({
  resource,
  loadingLabel,
  loadingRows,
  children,
}: ResourceBoundaryProps<T>) {
  const { state, reload } = resource

  if (state.status === 'loading') {
    return <LoadingState label={loadingLabel} rows={loadingRows} />
  }

  if (state.status === 'failed') {
    return <ErrorState error={state.error} onRetry={reload} />
  }

  return <>{children(state.data)}</>
}
