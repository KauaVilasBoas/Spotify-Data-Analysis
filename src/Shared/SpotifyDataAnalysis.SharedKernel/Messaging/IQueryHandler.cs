namespace SpotifyDataAnalysis.SharedKernel.Messaging;

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult> { }
