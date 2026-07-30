namespace SpotifyDataAnalysis.Modules.Analytics.Domain;

/// <summary>
/// Marcador de assembly do Domain do Analytics — âncora de tipo estável para os testes de arquitetura
/// (<c>AssemblyRegistry</c>). O read-side de EDA (E2) não tem agregados próprios: lê o schema <c>catalog</c>
/// via Dapper. Se surgir domínio puro de analytics (ex.: enums de recorte), ele mora aqui.
/// </summary>
public sealed class AnalyticsDomainAssemblyReference;
