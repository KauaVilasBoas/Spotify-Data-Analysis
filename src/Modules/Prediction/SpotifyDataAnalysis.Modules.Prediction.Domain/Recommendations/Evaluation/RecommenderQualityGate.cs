using System.Globalization;
using SpotifyDataAnalysis.SharedKernel.Exceptions;

namespace SpotifyDataAnalysis.Modules.Prediction.Domain.Recommendations.Evaluation;

/// <summary>
/// O gate de qualidade do recomendador (E4.4): reprova o motor quando os proxies degradam. Os limiares foram
/// cravados DEPOIS da primeira medição sobre o catálogo real (DP-2) — a lição do E3.2, cujo gate de R² foi posto
/// antes de medir e passou por margem de 0,0019.
///
/// <para><b>O gate de coerência é de DOIS LADOS, e essa é a decisão de design que importa.</b> Um gate só de piso
/// ("coerência ≥ X") seria satisfeito subindo o peso do boost até ele virar filtro duro — premiaria exatamente a
/// falha que este card diagnosticou. Por isso a coerência é cobrada em três frentes: um piso absoluto, um GANHO
/// mínimo sobre o cosine puro (o boost precisa estar fazendo diferença) e um TETO de saturação (o boost não pode
/// zerar a diversidade de gênero do top-N). Peso de menos falha o ganho; peso demais falha a saturação.</para>
///
/// <para><b>O proxy 3 é o único guarda não circular.</b> Coerência de gênero medida sobre um ranking que boosta
/// gênero mede em parte o próprio boost; a proximidade de duplicatas não usa gênero para nada. Se a normalização
/// ou o cosseno quebrarem, é ele que cai primeiro — por isso o gate o cobra em recall, alcance e no cosseno médio
/// dos irmãos reencontrados.</para>
/// </summary>
public sealed class RecommenderQualityGate
{
    // CADA LIMIAR ABAIXO DIZ A CONFIGURAÇÃO QUE O VALIDA (E4.8). Um número sem a configuração que o produziu foi o
    // defeito que deixou este gate defender, por dois épicos, um sistema que o endpoint já não entregava: os valores
    // do E4.4 foram medidos ANTES de o dedup virar default. A re-medição de 2026-09-12 manteve os SETE limiares
    // originais inalterados — todos continuam válidos na configuração de hoje, com folga —, então o que mudou aqui
    // foi a documentação do valor medido e do contexto, não a régua.
    //
    // O E4.9 (2026-09-13) acrescentou DOIS limiares novos (completude do resultado) e re-mediu os sete anteriores com
    // o over-fetch adaptativo ligado: nenhum se moveu. Limiar novo nasce de medição; limiar antigo não se afrouxa
    // para acomodar mudança.
    //
    // E O E4.12 ACRESCENTOU O EIXO QUE FALTAVA: a AMOSTRA. Dizer a configuração de um limiar não basta quando duas
    // amostras diferentes convivem na mesma avaliação — os sete primeiros limiares foram calibrados sobre as 300
    // sementes sorteadas e os dois de completude sobre os 1.895 membros dos grupos de 11+. Daí em diante cada limiar
    // diz a configuração E a população que o valida, e as guardas cobram as duas.

    /// <summary>
    /// Piso absoluto da coerência sob o boost default. Medido em <c>boost 0.050 | dedupe=on | content</c>, amostra
    /// fixa de 300 sementes, top-10: <b>0,5563 ± 0,0208</b> (E4.4, sem dedup: 0,5833). O limiar deixa margem folgada
    /// porque o alvo é detectar COLAPSO (o espaço deixar de agrupar por gênero), não oscilação normal de amostra.
    /// </summary>
    public const double MinimumGenreCoherence = 0.40;

    /// <summary>
    /// Ganho mínimo da coerência do boost sobre o piso do cosine puro. As duas pontas são medidas com o MESMO
    /// <c>dedupe</c>, senão a razão mistura dois efeitos. Medido em <c>dedupe=on</c>: 0,5563 / 0,1140 = <b>4,88x</b>
    /// (E4.4, ambos sem dedup: 4,33x). O limiar de 2x reprova um boost que virou decorativo.
    /// </summary>
    public const double MinimumCoherenceLiftOverCosineOnly = 2.0;

    /// <summary>
    /// Teto da saturação: fração de sementes cujo top-N inteiro é de um único gênero. Medido em
    /// <c>boost 0.050 | dedupe=on | content</c>: <b>0,2800</b> (E4.4, sem dedup: 0,3100). Acima de 0,50 o "boost" já
    /// decide a lista para a MAIORIA das sementes, o que o torna indistinguível do filtro duro e apaga o eixo de
    /// relaxamento da DP-C. Com os números medidos, um peso de 0,08 já reprovaria aqui.
    /// </summary>
    public const double MaximumGenreSaturation = 0.50;

    /// <summary>
    /// Recall mínimo de duplicatas normalizado pelo teto alcançável. Medido em <c>off | dedupe=off | content</c>
    /// sobre os 300 grupos SEM o teto de 8 membros (908 sementes): <b>0,6464</b> (E4.4, com o teto de 8: 0,6108).
    /// </summary>
    public const double MinimumDuplicateRecall = 0.45;

    /// <summary>
    /// Fração mínima de sementes com ao menos um irmão no top-K. Mesma configuração do recall: <b>0,7037</b>
    /// (E4.4: 0,6745).
    /// </summary>
    public const double MinimumDuplicateHitRate = 0.50;

    /// <summary>
    /// Cosseno médio mínimo dos irmãos reencontrados. Mesma configuração do recall: <b>0,999028</b> (E4.4: 0,998244)
    /// — duplicatas são praticamente idênticas no espaço de features, então este número só se move se a normalização
    /// ou o cosseno quebrarem. É o detector mais sensível do gate, e por isso o limiar é alto.
    /// </summary>
    public const double MinimumSiblingCosine = 0.98;

    /// <summary>
    /// Teto de sementes cujo top-N ainda traz DUAS versões da mesma obra depois do dedup (E4.8, DP-2). Alvo zero:
    /// eliminar essa repetição é literalmente a promessa do E4.7, e uma sobra aqui denuncia que a chave
    /// "artista|título" reconstruída no Prediction divergiu da <c>match_key</c> do Catalog — as duas normalizações
    /// são reimplementações independentes, e este é o único ponto onde essa divergência aparece como número.
    ///
    /// <para>Medido em <c>off | dedupe=on | content</c> sobre os 111 grupos de 11+ faixas (1.895 sementes):
    /// <b>0</b>. Sem dedup, na mesma amostra, são <b>1.768 de 1.895</b> — o limiar em zero não é aspiracional, é o
    /// valor medido, e a distância entre os dois números é a prova de valor do E4.7 que faltava.</para>
    /// </summary>
    public const int MaximumSeedsWithRedundantSiblings = 0;

    // O BURACO QUE O E4.8 DEIXOU REGISTRADO E O E4.9 FECHOU.
    //
    // A medição do E4.8 encontrou um defeito que o E4.4 não tinha como ver: com dedup ligado, uma semente que
    // pertence a um grupo grande de quase-duplicatas recebia MENOS recomendações do que pediu, porque as candidatas
    // do over-fetch FIXO (3× o limite) eram todas irmãs e colapsavam num único item. Confirmado contra o endpoint em
    // execução: em `genreMode=boost&dedupe=true&limit=10`, 679 das 1.895 faixas dos grupos de 11+ recebiam menos de
    // 10 itens e 227 recebiam exatamente UM. O E4.8 não gateou o número porque não podia consertá-lo, e cravar o
    // limiar no valor medido seria aceitar o defeito como linha de base.
    //
    // O E4.9 tornou o over-fetch ADAPTATIVO e o número virou zero nas duas contagens. Agora ele é limiar — os dois
    // logo abaixo.

    /// <summary>
    /// Teto da fração de sementes que recebe MENOS recomendações do que pediu (E4.9, DP-1). Medido em
    /// <c>boost 0.050 | dedupe=on | content</c> sobre as 1.895 sementes dos grupos de 11+ quase-duplicatas:
    /// <b>0,0000</b> (679 de 1.895, ou 0,3583, antes do over-fetch adaptativo).
    ///
    /// <para><b>Por que 5% e não zero, com o medido em zero:</b> 5% é o alvo decidido na DP-1, e a distância entre ele
    /// e o valor medido é folga deliberada. Um limiar cravado no zero medido transformaria em build vermelho qualquer
    /// semente patológica FUTURA (um grupo de quase-duplicatas maior que a janela de três rodadas) sem que houvesse
    /// conserto possível dentro do teto de rodadas — e o teto existe porque rodada extra custa CPU por requisição.</para>
    /// </summary>
    public const double MaximumShortResultRate = 0.05;

    /// <summary>
    /// Teto de sementes que recebem EXATAMENTE uma recomendação (E4.9, DP-1). Alvo zero, e aqui o zero é literal:
    /// medido <b>0</b> na mesma configuração e amostra (227 antes). Uma lista de nove itens é curta; uma lista de um
    /// item não lê como escolha de projeto, lê como recomendador quebrado — por isso é contado à parte do teto acima.
    /// </summary>
    public const int MaximumSeedsWithSingleResult = 0;

    /// <summary>
    /// A população que calibrou os DOIS limiares acima (E4.12) — e, portanto, a única em que eles são régua e não
    /// formalidade. É constante pública porque o smoke e o relatório precisam citá-la para medir no lugar certo, em vez
    /// de descobrirem pela exceção.
    /// </summary>
    public const RecommenderEvaluationPopulation CalibrationPopulationOfResultSize =
        RecommenderEvaluationPopulation.LargeNearDuplicateGroupMembers;

    /// <summary>
    /// Avalia os proxies contra os limiares. Recebe as DUAS medições de coerência (cosine puro e boost) porque o
    /// ganho só existe como comparação pareada — pedir só a medição boostada tornaria o gate incapaz de distinguir
    /// "o boost funciona" de "o catálogo é homogêneo".
    ///
    /// <para><b>E o proxy 3 vem DUAS vezes (E4.8, DP-2), porque são duas perguntas diferentes.</b> Recall, alcance e
    /// cosseno dos irmãos medem o MOTOR DE SIMILARIDADE, e o instrumento exige as duplicatas visíveis: são cobrados
    /// exclusivamente sobre a medição <c>dedupe=false</c>. Já "o top-N não repete a mesma obra" só existe como
    /// pergunta depois do dedup, e é cobrado sobre a medição <c>dedupe=true</c>. Cobrar o recall antigo sobre um
    /// top-N deduplicado seria medir outra coisa com o limiar de ontem.</para>
    /// </summary>
    /// <param name="cosineOnly">Medição com o gênero desligado — o piso da comparação.</param>
    /// <param name="boosted">Medição com o boost no peso default de produção.</param>
    /// <param name="similarityEngineDuplicates">Proxy 3 com <c>dedupe=false</c>: o motor de similaridade nu.</param>
    /// <param name="dedupedDuplicates">Proxy 3 com <c>dedupe=true</c>: o top-N que o endpoint realmente devolve.</param>
    /// <param name="resultSize">
    /// Proxy 4 (E4.9) — a distribuição do tamanho do resultado, medida na MESMA configuração e no MESMO top-N de
    /// <paramref name="boosted"/>, e sobre a <see cref="CalibrationPopulationOfResultSize"/>. As duas exigências não
    /// são formalidade nem são a mesma coisa: gatear a qualidade de uma configuração e a completude de outra devolveria
    /// um "aprovado" sobre duas grandezas que ninguém comparou, e medir a completude numa população onde a lista curta
    /// não acontece devolveria um "aprovado" que o defeito de volta não reprovaria.
    /// </param>
    /// <exception cref="DomainException">
    /// Quando a configuração OU A POPULAÇÃO de alguma medição não é a que o limiar correspondente valida — gate mal
    /// ligado produz um veredito confiante e errado, que é pior que gate nenhum.
    /// </exception>
    public RecommenderQualityVerdict Evaluate(
        RecommenderQualityMeasurement cosineOnly,
        RecommenderQualityMeasurement boosted,
        DuplicateProximityProxy similarityEngineDuplicates,
        DuplicateProximityProxy dedupedDuplicates,
        RecommendationSizeProxy resultSize)
    {
        ArgumentNullException.ThrowIfNull(cosineOnly);
        ArgumentNullException.ThrowIfNull(boosted);
        ArgumentNullException.ThrowIfNull(similarityEngineDuplicates);
        ArgumentNullException.ThrowIfNull(dedupedDuplicates);
        ArgumentNullException.ThrowIfNull(resultSize);

        RequireConfiguration(
            !cosineOnly.Setting.IsBlended && !boosted.Setting.IsBlended,
            "O gate só reprova o build em strategy=content (E4.8, DP-1): o blend é opt-in e o peso ainda não tem " +
            "default validado. Meça o blend e reporte, mas não o passe ao gate.");

        RequireConfiguration(
            cosineOnly.Setting.Dedupe == boosted.Setting.Dedupe,
            "O ganho da coerência é uma comparação PAREADA: as medições de cosine puro e de boost precisam ter o " +
            $"mesmo dedupe (recebido {cosineOnly.Setting.Dedupe} e {boosted.Setting.Dedupe}).");

        // O pareamento é da AMOSTRA também, não só do dedupe (E4.12). O ganho é uma razão entre duas médias: tirar o
        // numerador de uma população e o denominador de outra produz um número que não descreve sistema nenhum. E a
        // população não declarada é recusada em vez de comparada — duas medições `Unspecified` "coincidem" e essa
        // coincidência não afirma nada.
        RequireConfiguration(
            cosineOnly.SeedPopulation != RecommenderEvaluationPopulation.Unspecified
            && boosted.SeedPopulation != RecommenderEvaluationPopulation.Unspecified,
            "Medição sem população declarada não tem identidade: as amostras de cosine puro e de boost precisam " +
            $"declarar de que população vieram (recebido {cosineOnly.SeedPopulation} e {boosted.SeedPopulation}).");

        RequireConfiguration(
            cosineOnly.SeedPopulation == boosted.SeedPopulation,
            "O ganho da coerência é uma comparação PAREADA também na AMOSTRA: as medições de cosine puro e de boost " +
            $"precisam vir da MESMA população (recebido {cosineOnly.SeedPopulation} e {boosted.SeedPopulation}).");

        RequireConfiguration(
            !similarityEngineDuplicates.Setting.Dedupe,
            "Recall, alcance e cosseno de duplicatas medem o MOTOR de similaridade e exigem as duplicatas visíveis: " +
            "essa medição tem de vir com dedupe=false (E4.8, DP-2).");

        RequireConfiguration(
            dedupedDuplicates.Setting.Dedupe,
            "O indicador de repetição no top-N só faz sentido depois do dedup: essa medição tem de vir com " +
            "dedupe=true (E4.8, DP-2).");

        RequireConfiguration(
            resultSize.Setting == boosted.Setting && resultSize.TopN == boosted.TopN,
            "A distribuição de tamanho do resultado (E4.9) tem de ser medida na MESMA configuração e no mesmo top-N " +
            $"da medição gateada: recebido '{resultSize.Setting.Label}' top-{resultSize.TopN} contra " +
            $"'{boosted.Setting.Label}' top-{boosted.TopN}.");

        // A guarda que faltava (E4.12), e é ABSOLUTA, não relativa: exigir só que esta medição coincida com a da
        // coerência não resolveria nada, porque as duas podem coincidir na amostra sorteada e continuar erradas. Os
        // limiares deste proxy foram calibrados sobre os MEMBROS DOS GRUPOS GRANDES de quase-duplicatas, onde a lista
        // curta valia 0,3583 antes do over-fetch adaptativo; numa amostra sorteada ela é ~0 e o teto de 5% passa por
        // vacuidade — ou seja, reverter o over-fetch adaptativo deixaria este gate VERDE. O limiar só significa alguma
        // coisa sobre a população que o calibrou, e é essa população que a guarda nomeia.
        RequireConfiguration(
            resultSize.SeedPopulation == CalibrationPopulationOfResultSize,
            "A distribuição de tamanho do resultado (E4.9) só significa alguma coisa sobre a população que calibrou " +
            $"os limiares dela — {CalibrationPopulationOfResultSize}, onde a lista curta é fenômeno real. Recebido " +
            $"{resultSize.SeedPopulation} ({resultSize.SeedsEvaluated} sementes): numa população em que a lista curta " +
            "é ~0 por natureza, o teto seria satisfeito por vacuidade e reverter o over-fetch adaptativo não " +
            "reprovaria o build.");

        var failures = new List<string>();

        if (boosted.SelfExclusion.Violations > 0)
            failures.Add(Describe(
                "autoexclusão", boosted.SelfExclusion.Violations, 0, "violação(ões) — o valor aceitável é zero"));

        if (cosineOnly.SelfExclusion.Violations > 0)
            failures.Add(Describe(
                "autoexclusão (cosine puro)", cosineOnly.SelfExclusion.Violations, 0,
                "violação(ões) — o valor aceitável é zero"));

        if (boosted.GenreCoherence.MeanCoherence < MinimumGenreCoherence)
            failures.Add(Describe(
                "coerência de gênero", boosted.GenreCoherence.MeanCoherence, MinimumGenreCoherence, "abaixo do piso"));

        double lift = cosineOnly.GenreCoherence.MeanCoherence <= 0
            ? double.PositiveInfinity
            : boosted.GenreCoherence.MeanCoherence / cosineOnly.GenreCoherence.MeanCoherence;

        if (lift < MinimumCoherenceLiftOverCosineOnly)
            failures.Add(Describe(
                "ganho da coerência sobre o cosine puro", lift, MinimumCoherenceLiftOverCosineOnly,
                "o boost deixou de desempatar"));

        if (boosted.GenreCoherence.SaturationRate > MaximumGenreSaturation)
            failures.Add(Describe(
                "saturação de gênero", boosted.GenreCoherence.SaturationRate, MaximumGenreSaturation,
                "acima do teto — o boost está agindo como filtro disfarçado"));

        if (similarityEngineDuplicates.MeanRecall < MinimumDuplicateRecall)
            failures.Add(Describe(
                "recall de duplicatas", similarityEngineDuplicates.MeanRecall, MinimumDuplicateRecall,
                "abaixo do piso"));

        if (similarityEngineDuplicates.HitRate < MinimumDuplicateHitRate)
            failures.Add(Describe(
                "alcance de duplicatas", similarityEngineDuplicates.HitRate, MinimumDuplicateHitRate,
                "abaixo do piso"));

        if (similarityEngineDuplicates.MeanSiblingCosine < MinimumSiblingCosine)
            failures.Add(Describe(
                "cosseno médio das duplicatas", similarityEngineDuplicates.MeanSiblingCosine, MinimumSiblingCosine,
                "abaixo do piso — sinal de quebra na normalização ou no cosseno"));

        if (dedupedDuplicates.SeedsWithRedundantSiblings > MaximumSeedsWithRedundantSiblings)
            failures.Add(Describe(
                "repetição no top-N após o dedup",
                dedupedDuplicates.SeedsWithRedundantSiblings,
                MaximumSeedsWithRedundantSiblings,
                $"sementes (de {dedupedDuplicates.SeedsEvaluated}) ainda receberam duas versões da mesma obra"));

        if (resultSize.ShortResultRate > MaximumShortResultRate)
            failures.Add(Describe(
                "resultado abaixo do top-N pedido",
                resultSize.ShortResultRate,
                MaximumShortResultRate,
                $"acima do teto — {resultSize.SeedsBelowTopN} de {resultSize.SeedsEvaluated} sementes recebem menos " +
                $"de {resultSize.TopN} recomendações"));

        if (resultSize.SeedsWithSingleResult > MaximumSeedsWithSingleResult)
            failures.Add(Describe(
                "resultado com uma única recomendação",
                resultSize.SeedsWithSingleResult,
                MaximumSeedsWithSingleResult,
                $"sementes (de {resultSize.SeedsEvaluated}) receberam um item só"));

        return new RecommenderQualityVerdict(failures.Count == 0, failures);
    }

    private static void RequireConfiguration(bool satisfied, string message)
    {
        if (!satisfied)
            throw new DomainException(message);
    }

    private static string Describe(string proxy, double measured, double threshold, string reason) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}: medido {1:0.0000}, limiar {2:0.0000} — {3}.",
            proxy, measured, threshold, reason);

    private static string Describe(string proxy, int measured, int threshold, string reason) =>
        string.Format(
            CultureInfo.InvariantCulture, "{0}: medido {1}, limiar {2} — {3}.", proxy, measured, threshold, reason);
}

/// <summary>
/// O veredito do gate: aprovado ou reprovado, com a lista do que falhou. As falhas viajam como texto pronto porque
/// o consumidor é humano — a mensagem de um teste vermelho ou a linha de um relatório —, e um código de erro
/// obrigaria quem lê a traduzir de volta o número que já estava medido.
/// </summary>
/// <param name="IsApproved">Se nenhum proxy violou seu limiar.</param>
/// <param name="Failures">Uma descrição por proxy reprovado, com valor medido e limiar.</param>
public sealed record RecommenderQualityVerdict(bool IsApproved, IReadOnlyList<string> Failures);
