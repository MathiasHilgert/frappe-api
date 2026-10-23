using Frappe.Identity.Contracts.Owners;
using Frappe.Notification.Application;
using Frappe.Notification.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests;

public sealed class NotificationTests(ApiFixture fixture)
{
    public static TheoryData<object, string, string> CodeEvents => new()
    {
        { new OwnerVerificationCodeIssued(Guid.NewGuid(), "verify@example.test", "111111"), "verify@example.test", "111111 is your Frappé verification code" },
        { new OwnerPasswordResetCodeIssued(Guid.NewGuid(), "reset@example.test", "222222"), "reset@example.test", "222222 is your Frappé password reset code" },
        { new OwnerEmailChangeCodeIssued(Guid.NewGuid(), "new@example.test", "333333"), "new@example.test", "333333 confirms your new Frappé email" },
    };

    [Theory]
    [MemberData(nameof(CodeEvents))]
    public async Task PublishedCodeEvent_IsMailedToItsRecipient(object issued, string to, string subject)
    {
        var mailer = new CapturingMailer();
        await using var host = await fixture.CreateHostAsync(services =>
            services.Replace(ServiceDescriptor.Singleton<IMailer>(mailer)));

        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(30)).PublishMessageAndWaitAsync(issued);

        var mail = mailer.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe(to);
        mail.Subject.ShouldBe(subject);
        mail.Text.ShouldContain(subject[..6]);
    }

    [Fact]
    public void Development_WithoutAResendKey_LogsMailInsteadOfSendingIt()
    {
        using var scope = fixture.Host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMailer>().GetType().Name.ShouldBe("LoggingMailer");
    }

    [Fact]
    public async Task Development_LogsThePublishedMail()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await fixture.CreateHostAsync(services => services.AddSingleton<ILoggerProvider>(logs));

        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(30))
            .PublishMessageAndWaitAsync(new OwnerVerificationCodeIssued(Guid.NewGuid(), "log@example.test", "444444"));

        var line = logs.Messages.Where(m => m.StartsWith("Mail to log@example.test", StringComparison.Ordinal)).ShouldHaveSingleItem();
        line.ShouldContain("444444 is your Frappé verification code");
        TestContext.Current.SendDiagnosticMessage(line);
    }

    [Fact]
    public async Task Production_UsesResend()
    {
        await using var host = await fixture.CreateHostAsync(environment: "Production");
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMailer>().GetType().Name.ShouldBe("ResendMailer");
    }

    private sealed class CapturingMailer : IMailer
    {
        private readonly List<Mail> _sent = [];

        public IReadOnlyList<Mail> Sent
        {
            get
            {
                lock (_sent)
                    return [.. _sent];
            }
        }

        public Task SendAsync(Mail mail, CancellationToken ct)
        {
            lock (_sent)
                _sent.Add(mail);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                    return [.. _messages];
            }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
                _messages.Add(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}
