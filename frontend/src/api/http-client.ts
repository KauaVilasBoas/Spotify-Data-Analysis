import { apiErrorFromKind, apiErrorFromProblem, toApiError } from './api-error'
import { apiConfig, resolveBaseUrl } from './config'
import { isApiResult, isProblemDetails, type ApiResult, type ProblemDetails } from './contracts'

export type QueryValue = string | number | boolean | undefined | null

export interface ApiRequest {
  path: string
  query?: Record<string, QueryValue>
  signal?: AbortSignal
}

export interface ApiPostRequest<B = unknown> extends ApiRequest {
  body: B
}

const JSON_ACCEPT = 'application/json, application/problem+json'
const JSON_CONTENT = 'application/json'

function buildUrl(baseUrl: string, path: string, query: Record<string, QueryValue> | undefined): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null) {
      search.append(key, String(value))
    }
  }

  const queryString = search.toString()

  return `${baseUrl}${normalizedPath}${queryString.length > 0 ? `?${queryString}` : ''}`
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

async function unwrapEnvelope<T>(path: string, response: Response): Promise<T> {
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
      `The response from ${path} did not arrive in the expected ApiResult<T> envelope.`,
    )
  }

  const envelope = payload as ApiResult<T>

  if (!envelope.success || envelope.data === undefined || envelope.data === null) {
    throw apiErrorFromKind(
      'malformed',
      envelope.message.trim().length > 0
        ? envelope.message
        : `The API answered 200 on ${path} but sent no payload.`,
    )
  }

  return envelope.data
}

function resolveBase(): string {
  try {
    return resolveBaseUrl(apiConfig.rawBaseUrl)
  } catch (err) {
    throw apiErrorFromKind('configuration', err instanceof Error ? err.message : String(err))
  }
}

/**
 * The two HTTP egress points of the SPA. Both unwrap the API `ApiResult<T>` and translate any
 * failure (ProblemDetails, broken envelope or no response at all) into `ApiError`.
 * No screen should ever read `.data.data` or inspect `response.status`.
 */
export async function getResource<T>({ path, query, signal }: ApiRequest): Promise<T> {
  const baseUrl = resolveBase()
  const [requestSignal, dispose] = linkSignals(signal, apiConfig.timeoutMs)

  let response: Response

  try {
    response = await fetch(buildUrl(baseUrl, path, query), {
      method: 'GET',
      headers: { Accept: JSON_ACCEPT },
      signal: requestSignal,
    })
  } catch (error) {
    throw toApiError(error)
  } finally {
    dispose()
  }

  return unwrapEnvelope<T>(path, response)
}

export async function postResource<T>({ path, query, body, signal }: ApiPostRequest): Promise<T> {
  const baseUrl = resolveBase()
  const [requestSignal, dispose] = linkSignals(signal, apiConfig.timeoutMs)

  let response: Response

  try {
    response = await fetch(buildUrl(baseUrl, path, query), {
      method: 'POST',
      headers: { Accept: JSON_ACCEPT, 'Content-Type': JSON_CONTENT },
      body: JSON.stringify(body),
      signal: requestSignal,
    })
  } catch (error) {
    throw toApiError(error)
  } finally {
    dispose()
  }

  return unwrapEnvelope<T>(path, response)
}
