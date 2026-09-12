import type { ProblemDetails } from './contracts'

export type ApiErrorKind =
  | 'offline'
  | 'timeout'
  | 'validation'
  | 'notFound'
  | 'businessRule'
  | 'conflict'
  | 'forbidden'
  | 'server'
  | 'malformed'
  | 'configuration'

interface ApiErrorShape {
  kind: ApiErrorKind
  detail: string
  status?: number
  apiTitle?: string
  problemType?: string
  traceId?: string
  fieldErrors?: Record<string, string[]>
  cause?: unknown
}

const HEADLINE: Readonly<Record<ApiErrorKind, string>> = {
  offline: 'The API did not respond',
  timeout: 'The API took too long to respond',
  validation: 'Invalid request',
  notFound: 'Resource not found',
  businessRule: 'Business rule violated',
  conflict: 'State conflict',
  forbidden: 'Access denied',
  server: 'Internal API error',
  malformed: 'Response broke the contract',
  configuration: 'Missing configuration',
}

const FALLBACK_DETAIL: Readonly<Record<ApiErrorKind, string>> = {
  offline:
    'No response arrived from the configured host. The API may be down, or this origin may not be allowed by its CORS policy.',
  timeout: 'The request was cancelled after exceeding the configured timeout.',
  validation: 'The parameters sent did not pass the API validation.',
  notFound: 'The API reported that the requested resource does not exist.',
  businessRule: 'The operation was refused by a domain business rule.',
  conflict: 'The current state of the resource does not allow this operation.',
  forbidden: 'The API refused access to this resource.',
  server: 'The API hit an unexpected error while processing the request.',
  malformed: 'The response did not follow the ApiResult<T> envelope published by the API.',
  configuration: 'The application has not been configured correctly.',
}

const NEXT_STEP: Readonly<Record<ApiErrorKind, string>> = {
  offline:
    'Start the host (dotnet run --project src/Host/SpotifyDataAnalysis.Api) and confirm this origin is listed under Cors:AllowedOrigins.',
  timeout:
    'On free hosting the first request wakes the service up. Try again in a few seconds, or raise VITE_API_TIMEOUT_MS.',
  validation: 'Adjust the query parameters and retry.',
  notFound: 'Check the identifier. The API could not find that resource.',
  businessRule: 'Review the preconditions described in the message before retrying.',
  conflict: 'Reload the data before trying again.',
  forbidden: 'This operation requires a permission the current session does not hold.',
  server: 'Check the API logs using the trace id above.',
  malformed: 'This is a backend defect, not a frontend one: the published contract was not honoured.',
  configuration:
    'Fix VITE_API_BASE_URL in frontend/.env.local (http://, https://, a relative path starting with /, or empty for same-origin) and restart the dev server.',
}

const RECOVERABLE_KINDS: ReadonlySet<ApiErrorKind> = new Set<ApiErrorKind>([
  'offline',
  'timeout',
  'server',
])

/**
 * The single error type the whole application consumes. It absorbs both failure ends (the
 * RFC 7807 ProblemDetails returned by the API, and the transport failure where no response
 * arrives at all) and exposes them with one shape, so no screen has to inspect `Response`
 * or `TypeError`.
 *
 * `headline` and `nextStep` are the human reading derived from `kind`; `detail`, `apiTitle`
 * and `problemType` preserve what the API actually said, with no invented translation.
 */
export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly detail: string
  readonly status: number | undefined
  readonly apiTitle: string | undefined
  readonly problemType: string | undefined
  readonly traceId: string | undefined
  readonly fieldErrors: Record<string, string[]> | undefined

  constructor(shape: ApiErrorShape) {
    super(shape.detail, shape.cause === undefined ? undefined : { cause: shape.cause })

    this.name = 'ApiError'
    this.kind = shape.kind
    this.detail = shape.detail
    this.status = shape.status
    this.apiTitle = shape.apiTitle
    this.problemType = shape.problemType
    this.traceId = shape.traceId
    this.fieldErrors = shape.fieldErrors
  }

  get headline(): string {
    return HEADLINE[this.kind]
  }

  get nextStep(): string {
    return NEXT_STEP[this.kind]
  }

  get isRecoverable(): boolean {
    return RECOVERABLE_KINDS.has(this.kind)
  }
}

const KIND_BY_STATUS: Readonly<Record<number, ApiErrorKind>> = {
  400: 'validation',
  403: 'forbidden',
  404: 'notFound',
  409: 'conflict',
  422: 'validation',
}

function resolveKind(status: number, problem: ProblemDetails | undefined): ApiErrorKind {
  if (problem?.errors !== undefined) {
    return 'validation'
  }

  const mapped = KIND_BY_STATUS[status]

  if (mapped !== undefined) {
    return mapped
  }

  return status >= 500 ? 'server' : 'businessRule'
}

function nonEmpty(value: string | undefined): string | undefined {
  return value !== undefined && value.trim().length > 0 ? value : undefined
}

export function apiErrorFromProblem(status: number, problem: ProblemDetails | undefined): ApiError {
  const kind = resolveKind(status, problem)

  return new ApiError({
    kind,
    status,
    apiTitle: nonEmpty(problem?.title),
    problemType: nonEmpty(problem?.type),
    traceId: typeof problem?.traceId === 'string' ? problem.traceId : undefined,
    fieldErrors: problem?.errors,
    detail: nonEmpty(problem?.detail) ?? nonEmpty(problem?.title) ?? FALLBACK_DETAIL[kind],
  })
}

export function apiErrorFromKind(kind: ApiErrorKind, detail?: string, cause?: unknown): ApiError {
  return new ApiError({ kind, detail: detail ?? FALLBACK_DETAIL[kind], cause })
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error
  }

  if (error instanceof DOMException && error.name === 'AbortError') {
    return apiErrorFromKind('timeout', undefined, error)
  }

  return apiErrorFromKind('offline', undefined, error)
}

export function flattenFieldErrors(error: ApiError): string[] {
  if (error.fieldErrors === undefined) {
    return []
  }

  return Object.entries(error.fieldErrors).flatMap(([field, messages]) =>
    messages.map((message) => `${field}: ${message}`),
  )
}
