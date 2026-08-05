namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations;

/// <summary>
/// Uma vizinha com o "porquê rico" do E4.2, agora híbrido (E4.3): além da identidade e do score, a DECOMPOSIÇÃO do
/// score em suas duas fontes — o cosseno de áudio (esmiuçado por feature em <see cref="Contributions"/>) e o
/// <see cref="GenreBonus"/> de gênero. <see cref="Similarity"/> é o score HÍBRIDO que ordena; some
/// <see cref="CosineSimilarity"/> + <see cref="GenreBonus"/> e você o reconstrói, então a explicação nunca
/// contradiz o ranking (é o mesmo número, decomposto). Por dentro, <see cref="CosineSimilarity"/> é ainda a soma
/// das <see cref="Contributions"/> por feature — a dupla decomposição fecha por construção.
///
/// <para>Sem o <see cref="GenreBonus"/> na explicação, o boost ficaria invisível e o ranking pareceria contradizer
/// o cosseno (uma faixa de cosseno menor subindo "sem motivo") — o risco de explicabilidade do card. Com ele, o
/// consumidor mostra "subiu porque compartilha o gênero da semente".</para>
/// </summary>
/// <param name="TrackId">Identidade da faixa vizinha, por valor.</param>
/// <param name="Similarity">Score HÍBRIDO que ordena: <see cref="CosineSimilarity"/> + <see cref="GenreBonus"/>.</param>
/// <param name="CosineSimilarity">Só o cosseno de áudio, em [−1, 1] — a soma de <see cref="Contributions"/>.</param>
/// <param name="GenreBonus">O bônus somado por compartilhar o gênero da semente (0 quando não compartilha/não pesa).</param>
/// <param name="SharesSeedGenre">Se esta vizinha compartilha o gênero UTILIZÁVEL da semente (o que motivou o bônus).</param>
/// <param name="IsImputed">Se as features da vizinha foram imputadas, não medidas (DP-F).</param>
/// <param name="Contributions">A parcela de cada feature no cosseno, na ordem canônica; com os valores originais.</param>
public sealed record ExplainedTrackSimilarity(
    string TrackId,
    double Similarity,
    double CosineSimilarity,
    double GenreBonus,
    bool SharesSeedGenre,
    bool IsImputed,
    IReadOnlyList<FeatureContribution> Contributions);
