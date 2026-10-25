namespace SpotifyDataAnalysis.SharedKernel.Exceptions;

/// <summary>
/// O serviço existe mas está temporariamente indisponível (arranque em andamento, dependência offline, etc.).
/// Mapeia para HTTP 503 Service Unavailable — os clientes devem fazer retry após o intervalo indicado pelo
/// header <c>Retry-After</c> que o middleware deve adicionar.
/// </summary>
public sealed class ServiceUnavailableException : Exception
{
    /// <summary>
    /// Segundos sugeridos para aguardar antes do próximo retry. O middleware lê este valor ao montar o header
    /// <c>Retry-After</c>. <c>null</c> omite o header.
    /// </summary>
    public int? RetryAfterSeconds { get; }

    public ServiceUnavailableException(string message, int? retryAfterSeconds = null) : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
