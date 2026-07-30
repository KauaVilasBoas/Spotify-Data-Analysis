using System.Security.Cryptography;
using System.Text;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// Deriva identificadores estáveis para artistas e álbuns a partir dos NOMES do dataset Kaggle, que não traz
/// ids do Spotify.
///
/// <para>O id carrega o prefixo <see cref="Prefix"/> justamente para ser óbvio, em qualquer log ou resposta da
/// API, que <b>não é um id do Spotify</b> — e para que uma futura ingestão real possa detectar e reconciliar
/// os sintéticos por nome normalizado, a única chave em comum entre as duas origens.</para>
///
/// <para>O hash é criptográfico (SHA-256) e não <c>string.GetHashCode()</c>: o hash de string do .NET é
/// aleatorizado por processo, então ids gerados em execuções diferentes não bateriam e cada re-execução do
/// seed criaria artistas duplicados.</para>
/// </summary>
internal static class CsvDerivedIdentity
{
    /// <summary>Prefixo que marca o id como derivado do CSV, não vindo do Spotify.</summary>
    internal const string Prefix = "csv:";

    /// <summary>
    /// Separador das partes na forma canônica: o caractere de controle <c>unit separator</c> (U+001F), que não
    /// aparece em nome de artista ou álbum. Sem ele, <c>("ab", "c")</c> e <c>("a", "bc")</c> gerariam o mesmo id.
    /// </summary>
    private const char PartSeparator = (char)0x1F;

    /// <summary>
    /// Forma canônica usada para identidade: sem espaços nas pontas e em minúsculas. Variações de acento e de
    /// grafia continuam sendo artistas distintos — normalizar mais agressivamente fundiria nomes legitimamente
    /// diferentes.
    /// </summary>
    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Id determinístico para as partes informadas. A mesma entrada devolve o mesmo id em qualquer execução,
    /// máquina ou processo, o que torna o seed idempotente.
    /// </summary>
    public static string From(params string[] parts)
    {
        string canonical = string.Join(PartSeparator, parts.Select(Normalize));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return Prefix + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
