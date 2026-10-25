// O gênero só é um "porquê" quando pesou no ranking: o filtro de mesmo gênero moldou a
// lista (mesmo com genreBoost 0) ou o boost foi positivo. Decide pelo modo EFETIVO: num
// fallback (pedido SameGenreOnly, efetivo Off) a API ainda devolve sharedGenre, mas ele não influenciou a ordem.
export function genreInfluencedRanking(effectiveGenreMode: string, genreBoost: number): boolean {
  return effectiveGenreMode.toLowerCase() === 'samegenreonly' || genreBoost > 0
}
