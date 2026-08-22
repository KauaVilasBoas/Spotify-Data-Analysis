using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using SpotifyDataAnalysis.Api.Filters;

namespace SpotifyDataAnalysis.Api.Tests.Filters;

/// <summary>
/// E6.3 — a borda HTTP recusa o que não entende. Estes testes exercitam o
/// <see cref="RejectUnknownQueryParametersFilter"/> montando um <see cref="ActionExecutingContext"/> com a mesma
/// forma que o MVC entrega em runtime: os parâmetros declarados na action (com seu <see cref="BindingSource"/>),
/// os tokens de rota e a query string real. O foco é o modo de falha original — parâmetro desconhecido virava
/// 200 com o catálogo inteiro — e a garantia de que nenhuma chamada legítima do front regride.
/// </summary>
public sealed class RejectUnknownQueryParametersFilterTests
{
    private readonly RejectUnknownQueryParametersFilter _filter = new();

    // ---- Reprodução do bug: parâmetro desconhecido reprova com 400 -------------------------------------------

    [Fact]
    public void UnknownQueryParameter_ThrowsValidationException_NamingTheRejectedParameter()
    {
        // /api/tracks?searchTerm=x — 'searchTerm' não existe; o correto é 'search'.
        ActionExecutingContext context = TracksListContext(
            query: new() { ["searchTerm"] = "rock" });

        ValidationException exception =
            Assert.Throws<ValidationException>(() => _filter.OnActionExecuting(context));

        Assert.Contains(exception.Errors, failure => failure.PropertyName == "searchTerm");
    }

    [Fact]
    public void UnknownQueryParameter_CloseToADeclaredOne_SuggestsTheCorrectName()
    {
        ActionExecutingContext context = TracksListContext(
            query: new() { ["searchTerm"] = "rock" });

        ValidationException exception =
            Assert.Throws<ValidationException>(() => _filter.OnActionExecuting(context));

        Assert.Contains("search", Assert.Single(exception.Errors).ErrorMessage);
    }

    [Fact]
    public void MultipleUnknownParameters_AreAllReported()
    {
        ActionExecutingContext context = TracksListContext(
            query: new() { ["foo"] = "1", ["bar"] = "2" });

        ValidationException exception =
            Assert.Throws<ValidationException>(() => _filter.OnActionExecuting(context));

        Assert.Contains(exception.Errors, f => f.PropertyName == "foo");
        Assert.Contains(exception.Errors, f => f.PropertyName == "bar");
    }

    [Fact]
    public void OneUnknownAlongsideValidOnes_RejectsOnlyTheUnknown()
    {
        ActionExecutingContext context = TracksListContext(
            query: new() { ["search"] = "rock", ["page"] = "2", ["typo"] = "x" });

        ValidationException exception =
            Assert.Throws<ValidationException>(() => _filter.OnActionExecuting(context));

        Assert.Equal("typo", Assert.Single(exception.Errors).PropertyName);
    }

    // ---- Sem regressão: as chamadas reais do front continuam passando ----------------------------------------

    [Fact]
    public void AllDeclaredTrackParameters_PassThrough()
    {
        ActionExecutingContext context = TracksListContext(
            query: new()
            {
                ["search"] = "rock",
                ["page"] = "1",
                ["pageSize"] = "20",
                ["sort"] = "PopularityDesc",
            });

        // Nenhuma exceção: todos os parâmetros são declarados pela action.
        _filter.OnActionExecuting(context);
    }

    [Fact]
    public void EmptyQueryString_PassesThrough()
    {
        ActionExecutingContext context = TracksListContext(query: new());

        _filter.OnActionExecuting(context);
    }

    [Fact]
    public void CaseInsensitiveParameterNames_PassThrough()
    {
        // O binder do MVC é case-insensitive; o filtro não pode ser mais estrito que ele.
        ActionExecutingContext context = TracksListContext(
            query: new() { ["Search"] = "rock", ["PAGE"] = "1" });

        _filter.OnActionExecuting(context);
    }

    // ---- Transversalidade: vale para os endpoints de Analytics do mesmo jeito -------------------------------

    [Fact]
    public void Analytics_KnownParameters_PassThrough()
    {
        ActionExecutingContext context = InsightsPopularityContext(
            query: new() { ["page"] = "1", ["pageSize"] = "20", ["genre"] = "rock" });

        _filter.OnActionExecuting(context);
    }

    [Fact]
    public void Analytics_UnknownParameter_IsRejected()
    {
        ActionExecutingContext context = InsightsPopularityContext(
            query: new() { ["gener"] = "rock" });

        ValidationException exception =
            Assert.Throws<ValidationException>(() => _filter.OnActionExecuting(context));

        Assert.Equal("gener", Assert.Single(exception.Errors).PropertyName);
    }

    // ---- Parâmetro de rota repetido na querystring não é "desconhecido" --------------------------------------

    [Fact]
    public void RouteTokenName_IsNotTreatedAsUnknown()
    {
        // distributions/{feature}?buckets=10&includeImputed=true — 'feature' é rota; buckets/includeImputed query.
        ActionExecutingContext context = DistributionContext(
            query: new() { ["buckets"] = "10", ["includeImputed"] = "true" });

        _filter.OnActionExecuting(context);
    }

    // ---- Swagger/ferramentas: parâmetros anexados por ferramenta não quebram --------------------------------

    [Theory]
    [InlineData("api-version")]
    [InlineData("format")]
    [InlineData("_")]
    public void ToolSuppliedParameters_AreTolerated(string toolParameter)
    {
        ActionExecutingContext context = TracksListContext(
            query: new() { ["search"] = "rock", [toolParameter] = "whatever" });

        _filter.OnActionExecuting(context);
    }

    // =========================================================================================================
    // Fixtures — reproduzem a forma que o MVC entrega ao filtro.
    // =========================================================================================================

    private static ActionExecutingContext TracksListContext(Dictionary<string, string> query) =>
        BuildContext(
            query,
            routeTokens: [],
            parameters:
            [
                QueryParameter("search", typeof(string)),
                QueryParameter("page", typeof(int)),
                QueryParameter("pageSize", typeof(int)),
                QueryParameter("sort", typeof(string)),
                SpecialParameter("cancellationToken", typeof(CancellationToken)),
            ]);

    private static ActionExecutingContext InsightsPopularityContext(Dictionary<string, string> query) =>
        BuildContext(
            query,
            routeTokens: [],
            parameters:
            [
                QueryParameter("page", typeof(int)),
                QueryParameter("pageSize", typeof(int)),
                QueryParameter("genre", typeof(string)),
                SpecialParameter("cancellationToken", typeof(CancellationToken)),
            ]);

    private static ActionExecutingContext DistributionContext(Dictionary<string, string> query) =>
        BuildContext(
            query,
            routeTokens: ["feature"],
            parameters:
            [
                // 'feature' é resolvido pela rota; sem BindingSource explícito, como no controller real.
                UnsourcedParameter("feature", typeof(string)),
                QueryParameter("buckets", typeof(int)),
                QueryParameter("includeImputed", typeof(bool)),
                SpecialParameter("cancellationToken", typeof(CancellationToken)),
            ]);

    private static ActionExecutingContext BuildContext(
        Dictionary<string, string> query,
        string[] routeTokens,
        ParameterDescriptor[] parameters)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Query = new QueryCollection(
            query.ToDictionary(kv => kv.Key, kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));

        var actionDescriptor = new ControllerActionDescriptor
        {
            Parameters = parameters,
            RouteValues = routeTokens.ToDictionary(token => token, _ => (string?)"value"),
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);

        return new ActionExecutingContext(
            actionContext,
            filters: [],
            actionArguments: new Dictionary<string, object?>(),
            controller: new object());
    }

    private static ControllerParameterDescriptor QueryParameter(string name, Type type) => new()
    {
        Name = name,
        ParameterType = type,
        BindingInfo = new BindingInfo { BindingSource = BindingSource.Query },
    };

    private static ControllerParameterDescriptor UnsourcedParameter(string name, Type type) => new()
    {
        Name = name,
        ParameterType = type,
        BindingInfo = null,
    };

    private static ControllerParameterDescriptor SpecialParameter(string name, Type type) => new()
    {
        Name = name,
        ParameterType = type,
        BindingInfo = new BindingInfo { BindingSource = BindingSource.Special },
    };
}
