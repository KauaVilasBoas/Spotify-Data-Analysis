// Em content: score = cosine + genreBoost, a soma fecha.
// Em blend: score = (1−w)·contentNorm + w·jaccard, normalizado. NÃO é cosine + boost.
export function scoreLabel(isBlend: boolean): string {
  return isBlend ? 'Blend score' : 'Hybrid score'
}

// Prefixo da linha de componentes de áudio em blend. Sem "+", para não insinuar
// que cosine e genre compõem o Blend score.
export function blendScoreDecompositionLabel(): string {
  return 'audio'
}
