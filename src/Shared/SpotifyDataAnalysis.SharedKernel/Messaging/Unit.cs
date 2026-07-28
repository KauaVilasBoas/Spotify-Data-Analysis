namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>Void equivalent for generic constraints. Used as TResult in commands that return no value.</summary>
public readonly struct Unit
{
    public static readonly Unit Value = default;
}
