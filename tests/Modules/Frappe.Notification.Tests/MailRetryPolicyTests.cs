using Frappe.Notification.Application;
using Frappe.Notification.Infrastructure;
using Shouldly;
using Wolverine;
using Wolverine.ErrorHandling;

namespace Frappe.Notification.Tests;

public sealed class MailRetryPolicyTests
{
    [Fact]
    public void ConfigureMessaging_DeadLettersRejectedMail_AndRetriesFailedDeliveryWithBackoff()
    {
        var options = new WolverineOptions();

        new NotificationModule().ConfigureMessaging(options);

        var rules = options.Policies.Failures.ToArray();
        rules.ShouldContain(rule => rule.Match.Matches(new MailRejectedException(System.Net.HttpStatusCode.UnprocessableEntity)));
        rules.ShouldContain(rule => rule.Match.Matches(new MailDeliveryFailedException("down", null)));
        rules.ShouldNotContain(rule => rule.Match.Matches(new InvalidOperationException()));
    }
}
