import { describe, it, expect } from 'vitest'
import { scoreLabel, blendScoreDecompositionLabel } from './score-labels'

describe('scoreLabel', () => {
  it('content: rótulo é "Hybrid score"', () => {
    expect(scoreLabel(false)).toBe('Hybrid score')
  })

  it('blend: rótulo é "Blend score", nunca "Hybrid score"', () => {
    expect(scoreLabel(true)).toBe('Blend score')
    expect(scoreLabel(true)).not.toBe('Hybrid score')
  })
})

describe('blendScoreDecompositionLabel', () => {
  it('blend: prefixo da linha de áudio não insinua que soma resulta no score', () => {
    const label = blendScoreDecompositionLabel()
    expect(label).not.toBe('')
    // Não pode conter operador de soma aritmética que insinue composição para o score final
    expect(label).not.toContain('+')
  })
})
