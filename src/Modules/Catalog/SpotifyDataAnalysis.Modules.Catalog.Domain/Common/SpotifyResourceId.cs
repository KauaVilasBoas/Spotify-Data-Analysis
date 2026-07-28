using SpotifyDataAnalysis.SharedKernel.Domain;
using SpotifyDataAnalysis.SharedKernel.Guards;

namespace SpotifyDataAnalysis.Modules.Catalog.Domain.Common;

/// <summary>
/// Base dos identificadores de recursos do Spotify (faixa, artista, álbum, playlist). Todos compartilham a
/// mesma forma — uma string opaca não-vazia — e a mesma igualdade estrutural, então a validação e o
/// <c>ToString</c> vivem aqui e cada subclasse só expõe a sua factory tipada.
///
/// A tipagem por subclasse é intencional: um <c>SpotifyArtistId</c> NUNCA é igual a um
/// <c>SpotifyTrackId</c> de mesmo texto (a base <see cref="ValueObject"/> compara o tipo concreto antes dos
/// componentes), o que impede trocar um id pelo outro numa assinatura de método.
/// </summary>
public abstract class SpotifyResourceId : ValueObject
{
    protected SpotifyResourceId(string value) => Value = value;

    /// <summary>O identificador cru como a API do Spotify o expõe (ex.: <c>4uLU6hMCjMI75M1A2tKUQC</c>).</summary>
    public string Value { get; }

    /// <summary>
    /// Valida e normaliza o id cru vindo da API/dataset. Chamado pelas factories das subclasses para que a
    /// regra de "não-vazio, sem espaços nas pontas" exista num único lugar.
    /// </summary>
    protected static string Normalize(string? value, string parameterName)
        => Guard.AgainstNullOrWhiteSpace(value, parameterName).Trim();

    protected sealed override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public sealed override string ToString() => Value;
}
