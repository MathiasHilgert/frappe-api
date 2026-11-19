using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Observability;

/// <summary>
/// Runs a command in its module's unit of work (the <see cref="IUnitOfWork"/> keyed by <see cref="HandlerModule"/>):
/// one commit when the Result succeeds; a rollback, which also forgets what the handler staged, when it fails, throws, or
/// the commit itself throws. The handler's Result and exceptions pass through unchanged, except an
/// <see cref="ICommitRejection"/> (the storage refusing the changes for an expected reason, such as someone else changing
/// the same data first): the unit rolls back and the command is answered <c>Failure(rejection.Answer)</c> (CQRS-05).
/// A command of a module that registers no unit of work fails naming the module: running it would lose every write in
/// silence.
/// </summary>
internal sealed class TransactionalCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IServiceProvider services) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private static readonly string Module = HandlerModule.Of(typeof(TCommand));

    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var unitOfWork = services.GetKeyedService<IUnitOfWork>(Module)
            ?? throw new InvalidOperationException(
                $"Command {typeof(TCommand).Name} belongs to module '{Module}', which registers no unit of work (AddUnitOfWork): "
                + "its changes would never be saved.");

        await unitOfWork.BeginAsync(cancellationToken);
        Result<TResult> result;
        try
        {
            result = await inner.HandleAsync(command, cancellationToken);
            if (result.IsSuccess)
            {
                await unitOfWork.CommitAsync(cancellationToken);
                return result;
            }
        }
        catch (Exception exception) when (exception is ICommitRejection rejection)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return rejection.Answer;
        }
        catch
        {
            // Not the caller's token: a cancelled command still has to give its changes back.
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        await unitOfWork.RollbackAsync(CancellationToken.None);
        return result;
    }
}
