import { describe, expect, it } from 'vitest'
import {
  apiErrorFromKind,
  apiErrorFromProblem,
  toApiError,
} from './api-error'

describe('apiErrorFromProblem — 503 unavailable', () => {
  it('status 503 sem Retry-After → kind unavailable, retryAfterSeconds undefined', () => {
    const err = apiErrorFromProblem(503, undefined, undefined)
    expect(err.kind).toBe('unavailable')
    expect(err.retryAfterSeconds).toBeUndefined()
  })

  it('status 503 com Retry-After numérico → retryAfterSeconds preservado', () => {
    const err = apiErrorFromProblem(503, undefined, 8)
    expect(err.kind).toBe('unavailable')
    expect(err.retryAfterSeconds).toBe(8)
  })

  it('status 503 com Retry-After = 0 → retryAfterSeconds 0', () => {
    const err = apiErrorFromProblem(503, { status: 503, title: 'Unavailable', type: '' }, 0)
    expect(err.kind).toBe('unavailable')
    expect(err.retryAfterSeconds).toBe(0)
  })

  it('status 503 headline não menciona "internal" nem "logs"', () => {
    const err = apiErrorFromProblem(503, undefined, 5)
    expect(err.headline.toLowerCase()).not.toContain('internal')
    expect(err.nextStep.toLowerCase()).not.toContain('logs')
  })

  it('status 503 detail usa texto do ProblemDetails quando presente', () => {
    const err = apiErrorFromProblem(
      503,
      { status: 503, title: 'Similarity index warming up', detail: 'Index is warming up', type: '' },
      5,
    )
    expect(err.detail).toBe('Index is warming up')
  })

  it('status 503 detail faz fallback quando ProblemDetails ausente', () => {
    const err = apiErrorFromProblem(503, undefined, 5)
    expect(err.detail.length).toBeGreaterThan(0)
  })

  it('status 500 → kind server, não unavailable', () => {
    const err = apiErrorFromProblem(500, undefined, undefined)
    expect(err.kind).toBe('server')
    expect(err.retryAfterSeconds).toBeUndefined()
  })

  it('status 400 → retryAfterSeconds é undefined', () => {
    const err = apiErrorFromProblem(400, undefined, undefined)
    expect(err.retryAfterSeconds).toBeUndefined()
  })
})

describe('ApiError.isRecoverable', () => {
  it('kind unavailable é recuperável', () => {
    const err = apiErrorFromKind('unavailable')
    expect(err.isRecoverable).toBe(true)
  })

  it('kind server continua recuperável', () => {
    const err = apiErrorFromKind('server')
    expect(err.isRecoverable).toBe(true)
  })

  it('kind notFound não é recuperável', () => {
    const err = apiErrorFromKind('notFound')
    expect(err.isRecoverable).toBe(false)
  })
})

describe('toApiError — passthrough', () => {
  it('ApiError já formado passa sem alteração', () => {
    const original = apiErrorFromKind('unavailable')
    expect(toApiError(original)).toBe(original)
  })
})

describe('apiErrorFromProblem — status existentes não regridem', () => {
  it('400 → validation', () => expect(apiErrorFromProblem(400, undefined, undefined).kind).toBe('validation'))
  it('403 → forbidden', () => expect(apiErrorFromProblem(403, undefined, undefined).kind).toBe('forbidden'))
  it('404 → notFound', () => expect(apiErrorFromProblem(404, undefined, undefined).kind).toBe('notFound'))
  it('409 → conflict', () => expect(apiErrorFromProblem(409, undefined, undefined).kind).toBe('conflict'))
  it('422 → validation', () => expect(apiErrorFromProblem(422, undefined, undefined).kind).toBe('validation'))
  it('418 → businessRule (4xx genérico)', () => expect(apiErrorFromProblem(418, undefined, undefined).kind).toBe('businessRule'))
})
