import { apiErrorFromKind, apiErrorFromProblem, toApiError } from './api-error'
import { apiConfig } from './config'
import { isApiResult, isProblemDetails, type ApiResult, type ProblemDetails } from './contracts'

export type QueryValue = string | number | boolean | undefined | null

export interface ApiRequest {
  path: string
  query?: Record<string, QueryValue>
  signal?: AbortSignal
}

const JSON_ACCEPT = 'application/json, application/problem+json'

function buildUrl(path: string, query: Record<string, QueryValue> | undefined): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null) {
      search.append(key, String(value))
    }
  }

  const queryString = search.toString()

  return `${apiConfig.baseUrl}${normalizedPath}${queryString.length > 0 ? `?${queryString}` : ''}`
}

async function readJson(response: Response): Promise<unknown> {
  const body = await response.text()

  if (body.trim().length === 0) {
    return undefined
  }

  try {
    return JSON.parse(body) as unknown
  } catch {
    return undefined
  }
}

function linkSignals(external: AbortSignal | undefined, timeoutMs: number): [AbortSignal, () => void] {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(new DOMException('timeout', 'AbortError')), timeoutMs)
  const forward = () => controller.abort(external?.reason)

  external?.addEventListener('abort', forward, { once: true })

  return [
    controller.signal,
    () => {
      clearTimeout(timer)
      external?.removeEventListener('abort', forward)
    },
  ]
}

/**
 * Ponto único de saída HTTP da SPA. Desembrulha o `ApiResult<T>` da API e traduz qualquer
 * falha — ProblemDetails, envelope quebrado ou ausência de resposta — em `ApiError`.
 * Nenhuma tela deve ler `.data.data` nem inspecionar `response.status`.
 */
export async function getResource<T>({ path, query, signal }: ApiRequest): Promise<T> {
  if (!apiConfig.isConfigured) {
    throw apiErrorFromKind(
      'configuration',
      'VITE_API_BASE_URL não foi definida. Copie frontend/.env.example para frontend/.env.local e aponte para a API.',
    )
  }

  const [requestSignal, dispose] = linkSignals(signal, apiConfig.timeoutMs)

  let response: Response

  try {
    response = await fetch(buildUrl(path, query), {
      method: 'GET',
      headers: { Accept: JSON_ACCEPT },
      signal: requestSignal,
    })
  } catch (error) {
    throw toApiError(error)
  } finally {
    dispose()
  }

  const payload = await readJson(response)

  if (!response.ok) {
    throw apiErrorFromProblem(
      response.status,
      isProblemDetails(payload) ? (payload as ProblemDetails) : undefined,
    )
  }

  if (!isApiResult<T>(payload)) {
    throw apiErrorFromKind(
      'malformed',
      `A resposta de ${path} não veio no envelope ApiResult<T> esperado.`,
    )
  }

  const envelope = payload as ApiResult<T>

  if (!envelope.success || envelope.data === undefined || envelope.data === null) {
    throw apiErrorFromKind(
      'malformed',
      envelope.message.trim().length > 0
        ? envelope.message
        : `A API respondeu 200 em ${path}, mas sem payload.`,
    )
  }

  return envelope.data
}
