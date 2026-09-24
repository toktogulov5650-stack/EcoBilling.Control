using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>Executes a <typeparamref name="TQuery"/> and reports the outcome as a <see cref="Result{TValue}"/>.</summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
