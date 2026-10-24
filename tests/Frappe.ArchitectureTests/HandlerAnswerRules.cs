using Frappe.Application.Abstractions.Messaging;
using Frappe.SharedKernel.Validation;

namespace Frappe.ArchitectureTests;

/// <summary>The check that every handler answers what its message's marker declares.</summary>
internal static class HandlerAnswerRules
{
    /// <summary>
    /// Handlers whose answer is not what their message's marker declares: <c>ICommand</c> answers <c>Result</c>,
    /// <c>ICommand&lt;T&gt;</c> and <c>IQuery&lt;T&gt;</c> answer <c>Result&lt;T&gt;</c>, awaited through
    /// <c>Task</c>/<c>ValueTask</c> and first in a tuple when the rest cascades. A background command is the reverse:
    /// nobody reads its answer, so a <c>Result</c> there would cascade as an unrouted message.
    /// </summary>
    public static List<string> HandlersNotMatchingMarker(CqrsRules.Layer application) =>
        CqrsRules.Handlers(application)
            .Where(h => CqrsRules.IsCommand(h.Message) || CqrsRules.QueryResult(h.Message) is not null)
            .Where(h => !AnswersAsDeclared(h.Message, Answer(h.Method.ReturnType)))
            .Select(h => $"{h.Handler.FullName}.{h.Method.Name}({h.Message.Name})")
            .ToList();

    private static bool AnswersAsDeclared(Type message, Type answer)
    {
        if (CqrsRules.IsBackgroundCommand(message))
            return !IsResult(answer);
        var declared = CqrsRules.QueryResult(message) ?? CommandResponse(message);
        return declared is null
            ? answer == typeof(Result)
            : answer.IsGenericType && answer.GetGenericTypeDefinition() == typeof(Result<>) && answer.GetGenericArguments()[0] == declared;
    }

    private static Type Answer(Type returnType)
    {
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() is var awaited
            && (awaited == typeof(Task<>) || awaited == typeof(ValueTask<>)))
        {
            returnType = returnType.GetGenericArguments()[0];
        }

        return returnType.IsGenericType && returnType.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true
            ? returnType.GetGenericArguments()[0]
            : returnType;
    }

    private static bool IsResult(Type type) => type == typeof(Result) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>));

    private static Type? CommandResponse(Type message) =>
        message.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))
            ?.GetGenericArguments()[0];

}
