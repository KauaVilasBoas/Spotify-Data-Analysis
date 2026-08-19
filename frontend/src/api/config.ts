const RAW_BASE_URL = import.meta.env.VITE_API_BASE_URL
const RAW_TIMEOUT = import.meta.env.VITE_API_TIMEOUT_MS

const DEFAULT_TIMEOUT_MS = 20_000

function normalizeBaseUrl(value: string | undefined): string {
  const trimmed = value?.trim() ?? ''

  if (trimmed.length === 0) {
    return ''
  }

  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
}

function normalizeTimeout(value: string | undefined): number {
  const parsed = Number.parseInt(value ?? '', 10)

  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_TIMEOUT_MS
}

export const apiConfig = {
  baseUrl: normalizeBaseUrl(RAW_BASE_URL),
  timeoutMs: normalizeTimeout(RAW_TIMEOUT),
  get isConfigured(): boolean {
    return this.baseUrl.length > 0
  },
} as const
