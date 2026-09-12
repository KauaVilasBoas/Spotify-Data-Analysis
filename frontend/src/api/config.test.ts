import { describe, it, expect } from 'vitest'
import { resolveBaseUrl } from './config'

describe('resolveBaseUrl', () => {
  it('URL absoluta válida é normalizada e barra final removida', () => {
    expect(resolveBaseUrl('http://localhost:5140/')).toBe('http://localhost:5140')
    expect(resolveBaseUrl('https://api.example.com')).toBe('https://api.example.com')
    expect(resolveBaseUrl('https://api.example.com/')).toBe('https://api.example.com')
  })

  it('ausente, vazio ou só espaços é válido e resulta em mesma origem', () => {
    expect(resolveBaseUrl(undefined)).toBe('')
    expect(resolveBaseUrl('')).toBe('')
    expect(resolveBaseUrl('   ')).toBe('')
  })

  it('URL montada com base vazia para /api/tracks é /api/tracks', () => {
    const base = resolveBaseUrl(undefined)
    expect(`${base}/api/tracks`).toBe('/api/tracks')
  })

  it('caminho relativo com barra é válido', () => {
    expect(resolveBaseUrl('/api')).toBe('/api')
    expect(resolveBaseUrl('/api/')).toBe('/api')
    // barra sozinha normaliza para mesma origem
    expect(resolveBaseUrl('/')).toBe('')
  })

  it('localhost:5140 sem esquema é inválido e o valor aparece na mensagem', () => {
    expect(() => resolveBaseUrl('localhost:5140')).toThrow('localhost:5140')
  })

  it('valor com protocolo arbitrário é inválido e o valor aparece na mensagem', () => {
    expect(() => resolveBaseUrl('ftp://host')).toThrow('ftp://host')
    expect(() => resolveBaseUrl('localhost:')).toThrow('localhost:')
  })

  it('protocol-relative //evil.com é rejeitado', () => {
    expect(() => resolveBaseUrl('//evil.com')).toThrow('//evil.com')
  })

  it('//-sozinho é rejeitado', () => {
    expect(() => resolveBaseUrl('//')).toThrow('//')
  })

  it('https:evil.com (sem barras duplas) é rejeitado', () => {
    expect(() => resolveBaseUrl('https:evil.com')).toThrow('https:evil.com')
  })

  it('esquema em maiúscula HTTP:// é aceito', () => {
    expect(resolveBaseUrl('HTTP://api.example.com')).toBe('HTTP://api.example.com')
  })

  it('/api e /api/ continuam aceitos (regressão)', () => {
    expect(resolveBaseUrl('/api')).toBe('/api')
    expect(resolveBaseUrl('/api/')).toBe('/api')
  })
})
