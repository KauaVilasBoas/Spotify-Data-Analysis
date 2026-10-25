import { describe, it, expect } from 'vitest'
import { genreInfluencedRanking } from './shared-genre'

describe('genreInfluencedRanking', () => {
  it('off com boost 0: não influenciou', () => {
    expect(genreInfluencedRanking('off', 0)).toBe(false)
    expect(genreInfluencedRanking('Off', 0)).toBe(false)
  })
  it('boost com genreBoost > 0: influenciou', () => {
    expect(genreInfluencedRanking('boost', 0.05)).toBe(true)
  })
  it('boost com genreBoost 0: não influenciou', () => {
    expect(genreInfluencedRanking('boost', 0)).toBe(false)
  })
  it('sameGenreOnly influencia mesmo com boost 0, em qualquer caixa', () => {
    expect(genreInfluencedRanking('sameGenreOnly', 0)).toBe(true)
    expect(genreInfluencedRanking('SameGenreOnly', 0)).toBe(true)
  })
  it('fallback (pedido SameGenreOnly, efetivo off) com boost 0: não influenciou', () => {
    expect(genreInfluencedRanking('off', 0)).toBe(false)
  })
})
