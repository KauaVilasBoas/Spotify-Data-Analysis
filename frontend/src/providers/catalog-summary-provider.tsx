import { useCallback, type ReactNode } from 'react'
import { getCatalogSummary } from '@/api/insights'
import { useApiResource } from '@/hooks/use-api-resource'
import { CatalogSummaryContext } from './catalog-summary-context'

/**
 * Uma única leitura de `/api/insights/summary` compartilhada pela barra de conexão (que a usa como
 * prova de vida da API) e pela tela de visão geral (que a usa como conteúdo), sem duplicar a
 * requisição nem manter dois estados que podem divergir.
 */
export function CatalogSummaryProvider({ children }: { children: ReactNode }) {
  const fetcher = useCallback((signal: AbortSignal) => getCatalogSummary(signal), [])
  const resource = useApiResource(fetcher)

  return <CatalogSummaryContext.Provider value={resource}>{children}</CatalogSummaryContext.Provider>
}
