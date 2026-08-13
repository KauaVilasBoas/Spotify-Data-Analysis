using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using CsvHelper.Configuration;

namespace SpotifyDataAnalysis.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// Uma linha crua do dataset "Spotify Playlists" (Pichl et al.): o par playlist-faixa como o CSV o expõe —
/// dono, artista, título e o nome da playlist. É o insumo do <see cref="PichlPlaylistSeeder"/>; a playlist
/// nasce do agrupamento de várias destas.
/// </summary>
/// <param name="UserId">Hash do dono da playlist — a primeira metade da identidade da playlist.</param>
/// <param name="ArtistName">Nome do artista da faixa (a fonte só traz nome, nunca id do Spotify).</param>
/// <param name="TrackName">Título da faixa.</param>
/// <param name="PlaylistName">Nome da playlist que contém a faixa — a segunda metade da identidade.</param>
internal sealed record PichlPlaylistRow(
    string UserId, string? ArtistName, string? TrackName, string PlaylistName);

/// <summary>
/// Leitor (CsvHelper) do CSV do Pichl (E4.5), em <b>streaming</b>: o arquivo tem ~1,18 GB e ~12,9M linhas, então
/// materializar tudo em memória está fora de questão — cada linha vira uma <see cref="PichlPlaylistRow"/> de
/// forma preguiçosa.
///
/// <para><b>Dialeto do arquivo (do README do dataset), e por que ele importa:</b> separador vírgula, cada campo
/// <b>delimitado por aspas duplas</b>, e o <b>escape de aspa interna é a barra invertida <c>\</c></b> — NÃO a
/// aspa duplicada do RFC 4180 puro. O modo continua sendo <see cref="CsvMode.RFC4180"/> (as aspas delimitam o
/// campo e são removidas do valor), mas <see cref="CsvConfiguration.Escape"/> é trocado para <c>\</c>: assim
/// <c>\"</c> vira uma aspa literal dentro do campo, em vez de encerrar a citação e desalinhar o parser da linha
/// em diante. Usar <see cref="CsvMode.Escape"/> aqui seria errado — nesse modo as aspas deixam de delimitar e
/// viriam LITERALMENTE dentro do valor (<c>"u1"</c> em vez de <c>u1</c>).</para>
///
/// <para><b>Leitura por índice, não por nome de coluna:</b> o header do Pichl traz um espaço após cada vírgula
/// (<c>"user_id", "artistname", ...</c>) que as linhas de dados não têm. Casar por nome exigiria depender do trim
/// do header; ler pelas posições 0..3 é imune a esse detalhe e ao fato de o header não precisar bater com o
/// nome de nenhum tipo.</para>
/// </summary>
internal static class PichlPlaylistCsvReader
{
    private const int UserIdColumn = 0;
    private const int ArtistNameColumn = 1;
    private const int TrackNameColumn = 2;
    private const int PlaylistNameColumn = 3;

    /// <summary>A configuração que traduz o dialeto do Pichl (escape por barra invertida, header presente).</summary>
    private static CsvConfiguration Configuration => new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        Mode = CsvMode.RFC4180,
        Escape = '\\',
        // Linhas defeituosas (campos a mais/menos, aspas soltas) são ignoradas em vez de derrubar a carga: num
        // dataset de 12,9M linhas de origem social, um punhado de linhas malformadas é esperado, e abortar a
        // população inteira por causa delas seria frágil.
        BadDataFound = null,
        MissingFieldFound = null,
    };

    public static async IAsyncEnumerable<PichlPlaylistRow> ReadAsync(
        string csvFilePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(csvFilePath);
        using var reader = new StreamReader(stream);

        await foreach (PichlPlaylistRow row in ParseAsync(reader, cancellationToken))
            yield return row;
    }

    /// <summary>Parse a partir de um <see cref="TextReader"/> — testável sem tocar no disco.</summary>
    internal static async IAsyncEnumerable<PichlPlaylistRow> ParseAsync(
        TextReader textReader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var csv = new CsvReader(textReader, Configuration);

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? userId = Field(csv, UserIdColumn);
            string? playlistName = Field(csv, PlaylistNameColumn);

            // Sem dono ou sem nome de playlist não há identidade estável — a linha não pode virar (nem entrar em)
            // uma playlist e é descartada silenciosamente.
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(playlistName))
                continue;

            yield return new PichlPlaylistRow(
                userId,
                Field(csv, ArtistNameColumn),
                Field(csv, TrackNameColumn),
                playlistName);
        }
    }

    private static string? Field(CsvReader csv, int index)
    {
        string? value = csv.TryGetField(index, out string? field) ? field : null;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
