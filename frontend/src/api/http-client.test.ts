import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from './api-error'

// ---------------------------------------------------------------------------
// Mock de configuração: expõe rawBaseUrl como mutável para cada teste
// ---------------------------------------------------------------------------

const mockConfig = {
  rawBaseUrl: 'http://localhost:5140' as string | undefined,
  timeoutMs: 20_000,
}

vi.mock('./config', () => ({
  // resolveBaseUrl importada como valor real — sem await no top-level
  resolveBaseUrl: (raw: string | undefined): string => {
    const trimmed = raw?.trim() ?? ''
    if (trimmed.length === 0) return ''
    if (trimmed.startsWith('/') && !trimmed.startsWith('//')) {
      return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
    }
    const lower = trimmed.toLowerCase()
    if (!lower.startsWith('http://') && !lower.startsWith('https://')) {
      throw new Error(`Invalid VITE_API_BASE_URL: "${trimmed}".`)
    }
    try {
      new URL(trimmed)
    } catch {
      throw new Error(`Invalid VITE_API_BASE_URL: "${trimmed}".`)
    }
    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
  },
  get apiConfig() {
    return mockConfig
  },
}))

// Importar postResource DEPOIS do mock
const { postResource } = await import('./http-client')

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const VALID_BASE = 'http://localhost:5140'

function makeEnvelope<T>(data: T) {
  return JSON.stringify({ success: true, message: '', data })
}

function makeProblem(status: number, title: string, detail?: string) {
  return JSON.stringify({ status, title, detail })
}

function makeResponse(body: string, status = 200): Response {
  return new Response(body, {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

// ---------------------------------------------------------------------------
// Setup
// ---------------------------------------------------------------------------

beforeEach(() => {
  mockConfig.rawBaseUrl = VALID_BASE
  vi.stubGlobal('fetch', vi.fn())
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('postResource', () => {
  it('emite POST com Content-Type e corpo serializado', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(makeResponse(makeEnvelope({ ok: true })))

    await postResource({ path: '/api/predictions/popularity', body: { trackId: 'abc123' } })

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]

    expect(url).toBe(`${VALID_BASE}/api/predictions/popularity`)
    expect(init.method).toBe('POST')

    const headers = init.headers as Record<string, string>
    expect(headers['Content-Type']).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ trackId: 'abc123' }))
  })

  it('resposta 200 com envelope válido → devolve data desembrulhado', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(makeResponse(makeEnvelope({ predictedPopularity: 72 })))

    const result = await postResource<{ predictedPopularity: number }>({
      path: '/api/predictions/popularity',
      body: { trackId: 'abc' },
    })

    expect(result).toEqual({ predictedPopularity: 72 })
  })

  it('resposta 404 com ProblemDetails → lança ApiError notFound', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      makeResponse(makeProblem(404, 'Not Found', 'Track does not exist'), 404),
    )

    await expect(
      postResource({ path: '/api/predictions/popularity', body: { trackId: 'missing' } }),
    ).rejects.toSatisfy((e: ApiError) => e instanceof ApiError && e.kind === 'notFound')
  })

  it('resposta 200 com envelope quebrado → lança ApiError malformed', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(makeResponse(JSON.stringify({ wrong: 'shape' })))

    await expect(
      postResource({ path: '/api/predictions/popularity', body: { trackId: 'abc' } }),
    ).rejects.toSatisfy((e: ApiError) => e instanceof ApiError && e.kind === 'malformed')
  })

  it('VITE_API_BASE_URL malformada → lança ApiError configuration antes do fetch', async () => {
    mockConfig.rawBaseUrl = 'not-a-valid-url'

    await expect(
      postResource({ path: '/api/predictions/popularity', body: { trackId: 'abc' } }),
    ).rejects.toSatisfy((e: ApiError) => e instanceof ApiError && e.kind === 'configuration')

    expect(fetch).not.toHaveBeenCalled()
  })
})
