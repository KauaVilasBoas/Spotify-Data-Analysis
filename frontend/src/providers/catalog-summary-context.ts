import { createContext, useContext } from 'react'
import type { CatalogSummary } from '@/api/insights'
import type { ApiResource } from '@/hooks/use-api-resource'

export const CatalogSummaryContext = createContext<ApiResource<CatalogSummary> | null>(null)

export function useCatalogSummary(): ApiResource<CatalogSummary> {
  const resource = useContext(CatalogSummaryContext)

  if (resource === null) {
    throw new Error('useCatalogSummary exige um CatalogSummaryProvider acima na árvore.')
  }

  return resource
}
