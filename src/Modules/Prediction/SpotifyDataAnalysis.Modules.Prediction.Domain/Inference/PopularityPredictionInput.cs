using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Inference;

/// <summary>
/// A origem do insumo de uma predição de popularidade (E3.5). São dois modos mutuamente exclusivos, e a
/// exclusão é a invariante deste tipo — não uma verificação repetida em cada handler.
/// </summary>
public enum PopularityPredictionMode
{
    /// <summary>Prever pela faixa do catálogo: as features vêm do schema <c>catalog</c> lido por SQL.</summary>
    ByTrackId,

    /// <summary>Prever por um bloco de features informado à mão: nenhuma consulta ao catálogo.</summary>
    ByFeatures
}

/// <summary>
/// O insumo de uma predição, capturando a decisão da DP-1 no tipo: <b>ou</b> um <c>trackId</c> do catálogo
/// <b>ou</b> um bloco de features informado, nunca os dois, nunca nenhum.
///
/// <para>A exclusão mútua é garantida pela impossibilidade de construir um estado inválido: as duas fábricas
/// (<see cref="ForTrack"/> e <see cref="ForFeatures"/>) são os únicos caminhos, e "informou os dois" ou "não
/// informou nada" é rejeitado na fronteira do request antes de chegar aqui — mas mesmo assim o tipo não expõe
/// um construtor que aceite os dois campos, para que nenhum código futuro consiga forjar o estado ambíguo.</para>
/// </summary>
public sealed class PopularityPredictionInput
{
    private PopularityPredictionInput(
        PopularityPredictionMode mode, string? trackId, AudioFeatureInput? features)
    {
        Mode = mode;
        TrackId = trackId;
        Features = features;
    }

    /// <summary>Qual das duas origens este insumo usa.</summary>
    public PopularityPredictionMode Mode { get; }

    /// <summary>A faixa do catálogo, presente somente no modo <see cref="PopularityPredictionMode.ByTrackId"/>.</summary>
    public string? TrackId { get; }

    /// <summary>O bloco informado, presente somente no modo <see cref="PopularityPredictionMode.ByFeatures"/>.</summary>
    public AudioFeatureInput? Features { get; }

    /// <summary>Modo <c>trackId</c>: prever pela faixa do catálogo.</summary>
    /// <exception cref="DomainException">Quando o <paramref name="trackId"/> é vazio.</exception>
    public static PopularityPredictionInput ForTrack(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            throw new DomainException("O 'trackId' não pode ser vazio no modo de predição por faixa.");

        return new PopularityPredictionInput(
            PopularityPredictionMode.ByTrackId, trackId.Trim(), features: null);
    }

    /// <summary>Modo features: prever por um bloco informado à mão.</summary>
    /// <exception cref="DomainException">Quando o bloco é nulo.</exception>
    public static PopularityPredictionInput ForFeatures(AudioFeatureInput features)
    {
        if (features is null)
            throw new DomainException("O bloco de features não pode ser nulo no modo de predição por features.");

        return new PopularityPredictionInput(
            PopularityPredictionMode.ByFeatures, trackId: null, features);
    }

    /// <summary>
    /// Decide o modo a partir dos dois campos opcionais do request, aplicando a invariante XOR. É o único ponto
    /// que traduz "o que o cliente mandou" em um insumo válido, e falha alto quando a mensagem é ambígua.
    /// </summary>
    /// <exception cref="DomainException">Quando os dois modos vêm informados, ou nenhum.</exception>
    public static PopularityPredictionInput FromRequest(string? trackId, AudioFeatureInput? features)
    {
        bool hasTrackId = !string.IsNullOrWhiteSpace(trackId);
        bool hasFeatures = features is not null;

        if (hasTrackId && hasFeatures)
            throw new DomainException(
                "Informe apenas um modo: 'trackId' OU um bloco de features, nunca os dois.");

        if (!hasTrackId && !hasFeatures)
            throw new DomainException(
                "Informe um modo de predição: 'trackId' de uma faixa do catálogo OU um bloco de features.");

        return hasTrackId ? ForTrack(trackId!) : ForFeatures(features!);
    }
}
