using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// Registers a faked use case behind the use-case gate, as <c>AddHandlers</c> does for a real one, so a host that validates its handler chains
/// at startup (S7) starts with the fake in place of the handler.
/// </summary>
internal static class GatedFakes
{
    public static IServiceCollection AddFakeCommand<TCommand, TResult>(this IServiceCollection services, ICommandHandler<TCommand, TResult> fake, Action<ICaller>? seen = null)
        where TCommand : ICommand<TResult> =>
        services.AddScoped<ICommandHandler<TCommand, TResult>>(provider =>
            new GatedFakeCommand<TCommand, TResult>(fake, provider.GetRequiredService<IUseCaseGate>(), seen, provider));

    public static IServiceCollection AddFakeQuery<TQuery, TResult>(this IServiceCollection services, IQueryHandler<TQuery, TResult> fake, Action<ICaller>? seen = null)
        where TQuery : IQuery<TResult> =>
        services.AddScoped<IQueryHandler<TQuery, TResult>>(provider =>
            new GatedFakeQuery<TQuery, TResult>(fake, provider.GetRequiredService<IUseCaseGate>(), seen, provider));

    /// <summary>A faked command behind the gate.</summary>
    private sealed class GatedFakeCommand<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IUseCaseGate gate, Action<ICaller>? seen, IServiceProvider provider)
        : ICommandHandler<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
        {
            if (await gate.AdmitAsync(command, cancellationToken) is { } refusal)
            {
                return refusal;
            }

            seen?.Invoke(provider.GetRequiredService<ICaller>());
            var succeeded = false;
            try
            {
                var result = await inner.HandleAsync(command, cancellationToken);
                succeeded = result.IsSuccess;
                return result;
            }
            finally
            {
                await gate.CompleteAsync(command, succeeded, cancellationToken);
            }
        }
    }

    /// <summary>A faked query behind the gate.</summary>
    private sealed class GatedFakeQuery<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, IUseCaseGate gate, Action<ICaller>? seen, IServiceProvider provider) : IQueryHandler<TQuery, TResult>
        where TQuery : IQuery<TResult>
    {
        public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken)
        {
            if (await gate.AdmitAsync(query, cancellationToken) is { } refusal)
            {
                return refusal;
            }

            seen?.Invoke(provider.GetRequiredService<ICaller>());
            var succeeded = false;
            try
            {
                var result = await inner.HandleAsync(query, cancellationToken);
                succeeded = result.IsSuccess;
                return result;
            }
            finally
            {
                await gate.CompleteAsync(query, succeeded, cancellationToken);
            }
        }
    }
}
