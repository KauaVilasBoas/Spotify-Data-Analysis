// O gênero só é um "porquê" quando pesou no ranking: o filtro de mesmo gênero moldou a
// lista (mesmo com genreBoost 0) ou o boost foi positivo. Com genreMode=off a API ainda
// devolve sharedGenre (ramo descritivo), mas ele não influenciou a ordem.
export function genreInfluencedRanking(requestedGenreMode: string, genreBoost: number): boolean {
  return requestedGenreMode.toLowerCase() === 'samegenreonly' || genreBoost > 0
}
