using Frappe.Notifications.Application.Messages;
using Microsoft.Extensions.Hosting;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// Composes the sample of every email kind in every language when the host starts, so a missing text or argument, or a link in a Security
/// kind, stops the host there and not at the first send (S7, S8). It also reads the texts of the channel itself (the unsubscribe sentence) in
/// every language, and refuses a kind that two text sources both write, which would make the winner depend on registration order.
/// </summary>
public sealed class EmailKindsCheck(
    NotificationCatalog catalog, EmailComposer composer, IcuNotificationTexts texts, IEnumerable<INotificationTextSource> sources) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var sourceList = sources.ToArray();
        foreach (var language in NotificationLanguages.All)
        {
            var duplicates = sourceList.SelectMany(source => source.KindsOf(language)).GroupBy(kind => kind, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicates.Length > 0)
            {
                var names = string.Join(", ", duplicates.Select(kind => $"'{kind}'"));
                throw new InvalidOperationException($"Notification texts for {names} come from more than one text source in {language.Value}.");
            }

            var channelTexts = texts.For(EmailChannel.ChannelTexts, language);
            if (channelTexts.Language != language)
            {
                throw new InvalidOperationException($"The texts of '{EmailChannel.ChannelTexts}' are missing in {language.Value}.");
            }

            _ = (channelTexts["unsubscribe_text"], channelTexts["unsubscribe_label"]);
        }

        foreach (var kind in catalog.Kinds.Where(composer.CanCompose))
        {
            foreach (var language in NotificationLanguages.All)
            {
                try
                {
                    _ = composer.Compose(kind, kind.Sample, language);
                }
                catch (SecurityEmailWithLinkException exception)
                {
                    throw new InvalidOperationException(exception.Message, exception);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
