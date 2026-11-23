using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Hosting;

/// <summary>Fails host startup when a consumer step is registered for a consumer the host never declared.</summary>
internal sealed class ConsumerStepValidator : IValidateOptions<ConsumerStepOptions>
{
    public ValidateOptionsResult Validate(string? name, ConsumerStepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var declared = options.Consumers.ToHashSet();
        var failures = options.Steps
            .Where(step => !declared.Contains(new ConsumerDeclaration(step.Consumer, step.Event)))
            .Select(step =>
                $"Step {step.Step.Name} for {step.Event.Name} is registered for consumer '{step.Consumer}', "
                + $"but no consumer named '{step.Consumer}' consumes {step.Event.Name} (AddIntegrationEventConsumer), "
                + "so it would never run.")
            .ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
