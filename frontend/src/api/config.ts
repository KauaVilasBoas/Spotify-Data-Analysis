const RAW_TIMEOUT = import.meta.env.VITE_API_TIMEOUT_MS

const DEFAULT_TIMEOUT_MS = 20_000

/**
 * Resolves and validates the raw value of VITE_API_BASE_URL.
 *
 * Validity rules:
 * - absent, empty, or whitespace-only → same origin (returns '').
 * - starts with '/' → relative path (trailing slash stripped; '/' alone becomes '').
 * - protocol http: or https: → absolute URL (trailing slash stripped).
 * - any other value → throws Error with the received value quoted in the message.
 *
 * Exported separately from the module so it can be tested without stubbing import.meta.env.
 * Called lazily inside getResource (not at module level) so the error surfaces through
 * the SPA error path instead of causing a blank screen.
 */
export function resolveBaseUrl(raw: string | undefined): string {
  const trimmed = raw?.trim() ?? ''

  if (trimmed.length === 0) {
    return ''
  }

  if (trimmed.startsWith('/')) {
    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
  }

  // Check protocol explicitly — new URL('localhost:5140') parses with protocol
  // 'localhost:', so checking whether URL is valid is not sufficient.
  const invalid = `Invalid VITE_API_BASE_URL: "${trimmed}". Use http://, https://, a relative path starting with /, or leave it empty for same-origin.`
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    throw new Error(invalid)
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error(invalid)
  }

  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
}

function normalizeTimeout(value: string | undefined): number {
  const parsed = Number.parseInt(value ?? '', 10)

  return Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_TIMEOUT_MS
}

export const apiConfig = {
  /** Raw VITE_API_BASE_URL value — call resolveBaseUrl(apiConfig.rawBaseUrl) inside getResource. */
  rawBaseUrl: import.meta.env.VITE_API_BASE_URL as string | undefined,
  timeoutMs: normalizeTimeout(RAW_TIMEOUT),
} as const
