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
  offline: 'A API não respondeu',
  timeout: 'A API demorou demais para responder',
  validation: 'Requisição inválida',
  notFound: 'Recurso não encontrado',
  businessRule: 'Regra de negócio violada',
  conflict: 'Conflito de estado',
  forbidden: 'Acesso negado',
  server: 'Erro interno da API',
  malformed: 'Resposta fora do contrato',
  configuration: 'Configuração ausente',
}

const FALLBACK_DETAIL: Readonly<Record<ApiErrorKind, string>> = {
  offline:
    'Nenhuma resposta chegou do host configurado. A API pode estar fora do ar, ou a origem deste front pode não estar liberada no CORS.',
  timeout: 'A requisição foi cancelada por exceder o tempo limite configurado.',
  validation: 'Os parâmetros enviados não passaram na validação da API.',
  notFound: 'A API informou que o recurso solicitado não existe.',
  businessRule: 'A operação foi recusada por uma regra de negócio do domínio.',
  conflict: 'O estado atual do recurso não permite esta operação.',
  forbidden: 'A API recusou o acesso a este recurso.',
  server: 'A API encontrou um erro inesperado ao processar a requisição.',
  malformed: 'A resposta não seguiu o envelope ApiResult<T> publicado pela API.',
  configuration: 'A aplicação não foi configurada corretamente.',
}

const NEXT_STEP: Readonly<Record<ApiErrorKind, string>> = {
  offline:
    'Suba o Host (dotnet run --project src/Host/SpotifyDataAnalysis.Api) e confirme que a origem deste front consta em Cors:AllowedOrigins.',
  timeout:
    'Em hospedagem gratuita o primeiro acesso acorda o serviço. Tente de novo em alguns segundos ou aumente VITE_API_TIMEOUT_MS.',
  validation: 'Ajuste os parâmetros da consulta e repita.',
  notFound: 'Confira o identificador informado — a API não encontrou esse recurso.',
  businessRule: 'Reveja as pré-condições descritas na mensagem antes de repetir a operação.',
  conflict: 'Recarregue os dados antes de tentar de novo.',
  forbidden: 'Esta operação exige uma permissão que a sessão atual não possui.',
  server: 'Consulte os logs da API usando o traceId acima.',
  malformed: 'Isto é defeito de backend, não do front: o contrato publicado não foi respeitado.',
  configuration:
    'Defina VITE_API_BASE_URL em frontend/.env.local e reinicie o servidor de desenvolvimento.',
}

const RECOVERABLE_KINDS: ReadonlySet<ApiErrorKind> = new Set<ApiErrorKind>([
  'offline',
  'timeout',
  'server',
])

/**
 * Erro único que toda a aplicação consome. Absorve as duas pontas de falha — o ProblemDetails
 * (RFC 7807) devolvido pela API e a falha de transporte, quando não há resposta nenhuma — e as expõe
 * com a mesma forma, para nenhuma tela precisar inspecionar `Response` nem `TypeError`.
 *
 * `headline` e `nextStep` são a leitura em português, derivadas do `kind`; `detail`, `apiTitle` e
 * `problemType` preservam o que a API de fato disse, sem tradução inventada.
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
