using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SpotifyDataAnalysis.Api.Filters;

/// <summary>
/// Borda HTTP transversal (E6.3): recusa a query string que traz um parâmetro que a action <b>não declara</b>,
/// em vez de descartá-lo em silêncio. Um filtro digitado errado (<c>?searchTerm=x</c> quando o correto é
/// <c>?search=x</c>) passava sem filtrar e devolvia o catálogo inteiro com <c>200</c>; agora vira
/// <c>400 ProblemDetails</c> nomeando o parâmetro rejeitado.
///
/// <para><b>Por que um filtro global no Host, e não código por controller:</b> a checagem é de borda HTTP e
/// vale para TODO endpoint MVC paginado — corrigir só <c>/api/tracks</c> deixaria a mesma armadilha nos demais.
/// Registrado uma única vez em <c>AddControllers(options.Filters)</c>, ele enxerga os parâmetros declarados na
/// <see cref="ActionDescriptor"/> sem que os módulos referenciem nada do Host (a fronteira Api ↛ Domain de
/// módulo continua intacta).</para>
///
/// <para><b>Reuso do contrato de erro:</b> em vez de escrever a resposta aqui, lança
/// <see cref="ValidationException"/> (FluentValidation) — exatamente o que o <c>ExceptionHandlingMiddleware</c>
/// já mapeia para <c>400</c> <c>validation-error</c> com <c>traceId</c> e um mapa <c>errors</c> por campo, que o
/// cliente do front já sabe ler. Nenhum formato novo é inventado.</para>
///
/// <para><b>Custo no caminho quente:</b> quando a query está correta, o filtro só compara dois conjuntos de
/// nomes; a exceção só nasce no caminho de erro (raro), nunca como controle de fluxo do caminho feliz. Quando a
/// requisição não traz query string alguma, retorna de imediato.</para>
/// </summary>
public sealed class RejectUnknownQueryParametersFilter : IActionFilter
{
    /// <summary>
    /// Parâmetros que ferramentas anexam por conta própria e que NÃO são erro do chamador. Sem esta allowlist,
    /// o "Try it out" do Swagger UI e afins passariam a tomar <c>400</c> por um cache-buster ou por
    /// <c>api-version</c> — regressão de demo, não rigor. Comparação sem diferenciar maiúsculas.
    /// </summary>
    private static readonly HashSet<string> ToolSuppliedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "api-version", // versionamento anexado por clientes/proxies
        "format",      // negociação de conteúdo por querystring de algumas UIs
        "_",           // cache-buster típico (jQuery/Swagger)
    };

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        IQueryCollection query = context.HttpContext.Request.Query;
        if (query.Count == 0)
            return;

        if (context.ActionDescriptor is not ControllerActionDescriptor actionDescriptor)
            return;

        HashSet<string> declaredParameters = CollectDeclaredQueryParameterNames(actionDescriptor);

        List<ValidationFailure>? failures = null;

        foreach (string key in query.Keys)
        {
            if (declaredParameters.Contains(key) || ToolSuppliedParameters.Contains(key))
                continue;

            (failures ??= []).Add(new ValidationFailure(
                key,
                BuildRejectionMessage(key, declaredParameters)));
        }

        if (failures is not null)
            throw new ValidationException(failures);
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Nada a fazer após a execução: a checagem é estritamente de entrada.
    }

    /// <summary>
    /// Nomes que a action aceita vindos da query string. Inclui (a) cada parâmetro cujo binding source é a
    /// query — explícito via <c>[FromQuery]</c> ou implícito para tipo simples — e (b) os tokens da rota, que
    /// nunca chegam como query mas cujo nome é legítimo. Parâmetros claramente de outra fonte (rota, corpo,
    /// cabeçalho, serviços) e o <c>CancellationToken</c> não entram como "esperados de query", mas também não
    /// disparam rejeição porque não aparecem na querystring. O objetivo é conservador: jamais reprovar um nome
    /// que o controller de fato declara.
    /// </summary>
    private static HashSet<string> CollectDeclaredQueryParameterNames(ControllerActionDescriptor actionDescriptor)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (ParameterDescriptor parameter in actionDescriptor.Parameters)
        {
            BindingSource? source = parameter.BindingInfo?.BindingSource;

            // Fontes que comprovadamente NÃO são a query string. Um parâmetro assim (ex.: [FromBody],
            // [FromRoute], [FromHeader], [FromServices], CancellationToken) não deve alargar o conjunto de
            // nomes aceitos na query.
            bool isNonQuerySource =
                source == BindingSource.Body ||
                source == BindingSource.Header ||
                source == BindingSource.Services ||
                source == BindingSource.Form ||
                source == BindingSource.Path ||
                source == BindingSource.Special;

            if (isNonQuerySource)
                continue;

            // Query explícita, ou binding source indefinido (tipo simples resolvido em runtime como rota OU
            // query): em ambos os casos o nome é um parâmetro legítimo do endpoint e não pode virar 400.
            names.Add(parameter.Name);
        }

        // Tokens de rota (ex.: {feature}, {id}) chegam pela rota, não pela query; ainda assim seus nomes são
        // aceitáveis e não devem ser tratados como desconhecidos caso um cliente os repita na querystring.
        foreach (string routeKey in actionDescriptor.RouteValues.Keys)
            names.Add(routeKey);

        return names;
    }

    /// <summary>
    /// Mensagem da rejeição. Quando algum parâmetro declarado é "próximo" do desconhecido (mesma família de
    /// nome, típico de erro de digitação como <c>searchTerm</c> por <c>search</c>), sugere o correto; caso
    /// contrário, apenas informa que o parâmetro não é reconhecido.
    /// </summary>
    private static string BuildRejectionMessage(string unknownParameter, HashSet<string> declaredParameters)
    {
        string? suggestion = FindClosestParameter(unknownParameter, declaredParameters);

        return suggestion is null
            ? $"Parâmetro de query '{unknownParameter}' não é reconhecido por este endpoint."
            : $"Parâmetro de query '{unknownParameter}' não é reconhecido por este endpoint. " +
              $"Você quis dizer '{suggestion}'?";
    }

    /// <summary>
    /// Heurística barata para sugerir o parâmetro correto num erro de digitação: escolhe o parâmetro declarado
    /// que compartilha o maior prefixo com o desconhecido, exigindo pelo menos três caracteres em comum para
    /// não sugerir ruído. Sem dependência de distância de edição — o objetivo é ajudar no caso óbvio
    /// (<c>searchTerm</c> → <c>search</c>), não resolver todo typo.
    /// </summary>
    private static string? FindClosestParameter(string unknownParameter, HashSet<string> declaredParameters)
    {
        string? best = null;
        int bestPrefixLength = 0;

        foreach (string candidate in declaredParameters)
        {
            int prefixLength = CommonPrefixLength(unknownParameter, candidate);
            if (prefixLength > bestPrefixLength)
            {
                bestPrefixLength = prefixLength;
                best = candidate;
            }
        }

        return bestPrefixLength >= 3 ? best : null;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        int max = Math.Min(left.Length, right.Length);
        int i = 0;
        while (i < max && char.ToLowerInvariant(left[i]) == char.ToLowerInvariant(right[i]))
            i++;

        return i;
    }
}
