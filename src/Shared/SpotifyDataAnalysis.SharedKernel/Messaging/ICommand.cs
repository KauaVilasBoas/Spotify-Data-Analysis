namespace SpotifyDataAnalysis.SharedKernel.Messaging;

/// <summary>Commands change state and never return read data.</summary>
public interface ICommand : IRequest<Unit> { }

public interface ICommand<TResult> : IRequest<TResult> { }
