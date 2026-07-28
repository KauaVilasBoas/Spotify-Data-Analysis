namespace SpotifyDataAnalysis.SharedKernel.Domain;

/// <summary>
/// Objeto de valor com igualdade estrutural por componentes.
/// Subclasses devem implementar <see cref="GetEqualityComponents"/> retornando
/// os valores que definem a identidade do VO.
/// </summary>
public abstract class ValueObject
{
    /// <summary>
    /// Retorna os componentes que definem a igualdade deste value object.
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;

        return GetEqualityComponents()
            .SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    public override int GetHashCode()
        => GetEqualityComponents()
            .Aggregate(0, (hash, component) => HashCode.Combine(hash, component));

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(ValueObject? left, ValueObject? right)
        => !(left == right);
}
