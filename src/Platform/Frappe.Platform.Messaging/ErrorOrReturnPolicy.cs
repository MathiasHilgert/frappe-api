using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace Frappe.Platform.Messaging;

// Wolverine cascades any return value as a new message by default, so an ErrorOr would be published.
// Every chain whose handler returns ErrorOr<T> settles it through ErrorOrOutcome instead.
internal sealed class ErrorOrReturnPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (
            HandlerChain chain in chains
                .SelectMany(static chain => chain.ByEndpoint.Prepend(chain))
                .Distinct()
        )
        {
            if (chain.Handlers.Exists(static call => ReturnsErrorOr(call.Method.ReturnType)))
            {
                chain.ReturnVariableActionSource = new ErrorOrActionSource();
            }
        }
    }

    // Task<ErrorOr<T>> only: every Kernel handler interface returns it, and discovery takes only types that
    // implement one; a ValueTask signature would need a Kernel change first, and then a case here.
    private static bool ReturnsErrorOr(Type type) =>
        type.IsGenericType
        && (
            type.GetGenericTypeDefinition() == typeof(ErrorOr<>)
            || (
                type.GetGenericTypeDefinition() == typeof(Task<>)
                && ReturnsErrorOr(type.GenericTypeArguments[0])
            )
        );

    private sealed class ErrorOrActionSource : IReturnVariableActionSource
    {
        public IReturnVariableAction Build(IChain chain, Variable variable) => new Action(variable);
    }

    private sealed class Action(Variable result) : IReturnVariableAction
    {
        public string Description => "Settle the ErrorOr result; never cascade it";

        public IEnumerable<Type> Dependencies() => [];

        public IEnumerable<Frame> Frames() => [new SettleFrame(result)];
    }

    // Generated: when the result is an error, ErrorOrOutcome has discarded the work, so the method returns
    // before the transaction frames that follow would commit it.
    private sealed class SettleFrame : AsyncFrame
    {
        private readonly Variable _result;
        private Variable? _outcome;
        private Variable? _context;

        public SettleFrame(Variable result)
        {
            _result = result;
            uses.Add(result);
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _outcome = chain.FindVariable(typeof(ErrorOrOutcome));
            yield return _outcome;
            _context = chain.FindVariable(typeof(MessageContext));
            yield return _context;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.Write(
                $"BLOCK:if (await {_outcome!.Usage}.{nameof(ErrorOrOutcome.SettleAsync)}({_context!.Usage}, {_result.Usage}))"
            );
            writer.Write("return;");
            writer.FinishBlock();
            Next?.GenerateCode(method, writer);
        }
    }
}
