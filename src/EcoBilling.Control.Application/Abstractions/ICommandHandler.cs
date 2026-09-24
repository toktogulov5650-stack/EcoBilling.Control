using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>Executes a <typeparamref name="TCommand"/> and reports the outcome as a <see cref="Result{TValue}"/>.</summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
