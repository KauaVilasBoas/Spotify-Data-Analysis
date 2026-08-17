/**
 * Espelho TypeScript dos três contratos públicos da API .NET.
 *
 * - `ApiResult<T>`     -> SpotifyDataAnalysis.SharedKernel.Http.ApiResult<T>
 * - `PagedResult<T>`   -> SpotifyDataAnalysis.SharedKernel.Messaging.PagedResult<TItem>
 * - `ProblemDetails`   -> RFC 7807, escrito pelo ExceptionHandlingMiddleware do Host
 *
 * O Host serializa em camelCase (default do System.Text.Json no ASP.NET Core) e converte
 * enums pelo NOME (JsonStringEnumConverter), não pelo ordinal.
 */

export interface ApiResult<T> {
  success: boolean
  message: string
  data?: T
}

export interface PagedResult<TItem> {
  items: TItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  traceId?: string
  errors?: Record<string, string[]>
  [extension: string]: unknown
}

export function isApiResult<T>(value: unknown): value is ApiResult<T> {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const candidate = value as Record<string, unknown>

  return typeof candidate.success === 'boolean' && typeof candidate.message === 'string'
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const candidate = value as Record<string, unknown>

  return (
    typeof candidate.title === 'string' ||
    typeof candidate.detail === 'string' ||
    typeof candidate.status === 'number'
  )
}
