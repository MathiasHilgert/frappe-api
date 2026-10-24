using Alba;
using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;
using Frappe.Notification.Application;
using Frappe.Notification.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests;

public sealed class NotificationTests(ApiFixture fixture, NotificationTests.MailerHostFixture mailerHost)
    : IClassFixture<NotificationTests.MailerHostFixture>
{
    private static readonly DateTimeOffset Expires = DateTimeOffset.UtcNow.AddMinutes(15);

    /// <summary>One host with a capturing mailer for the whole class: booting a host per case cost about a second each.</summary>
    public sealed class MailerHostFixture(ApiFixture fixture) : IAsyncLifetime
    {
        internal CapturingMailer Mailer { get; } = new();

        public IAlbaHost Host { get; private set; } = null!;

        public async ValueTask InitializeAsync() =>
            Host = await fixture.CreateHostAsync(services => services.Replace(ServiceDescriptor.Singleton<IMailer>(Mailer)));

        // A failed start leaves no host: disposing must not hide that error behind a null reference.
        public async ValueTask DisposeAsync()
        {
            if (Host is not null)
                await Host.DisposeAsync();
        }
    }

    public static TheoryData<object, string, string> CodeEvents => new()
    {
        { new OwnerVerificationCodeIssued(Guid.NewGuid(), "Ana", "verify@example.test", "111111", Expires, "es-AR"), "verify@example.test", "Tu código de Frappé: 111111" },
        { new PasswordResetCodeIssued(Guid.NewGuid(), Guid.NewGuid(), "reset@example.test", "Ana", "222222", Expires, "pt-BR"), "reset@example.test", "Código para trocar sua senha: 222222" },
        { new EmailChangeCodeIssued(Guid.NewGuid(), Guid.NewGuid(), "Ana", "new@example.test", "333333", Expires, "en"), "new@example.test", "Code to confirm your new email: 333333" },
        // Written before events carried a locale: still mailed, in the default language.
        { new OwnerVerificationCodeIssued(Guid.NewGuid(), "Ana", "old@example.test", "555555", Expires), "old@example.test", "Your Frappé code: 555555" },
    };

    [Theory]
    [MemberData(nameof(CodeEvents))]
    public async Task PublishedCodeEvent_IsMailedToItsRecipient(object issued, string to, string subject)
    {
        var (host, mailer) = (mailerHost.Host, mailerHost.Mailer);
        mailer.Clear();

        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(30)).PublishMessageAndWaitAsync(Sealed(host, issued));

        var mail = mailer.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe(to);
        mail.Subject.ShouldBe(subject);
        mail.Text.ShouldContain(subject[^6..]);
        mail.Html.ShouldNotBeNull().ShouldContain(subject[^1..]);
        mail.InlineImages.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task PublishedCodeEvent_CarriesTheCodeSealed_AndTheMailerOpensIt()
    {
        var (host, mailer) = (mailerHost.Host, mailerHost.Mailer);
        mailer.Clear();
        var issued = (OwnerVerificationCodeIssued)Sealed(host,
            new OwnerVerificationCodeIssued(Guid.NewGuid(), "Ana", "sealed@example.test", "777777", Expires, "en"));

        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(30)).PublishMessageAndWaitAsync(issued);

        issued.SealedCode.ShouldNotContain("777777");
        mailer.Sent.ShouldHaveSingleItem().Text.ShouldContain("777777");
    }


    /// <summary>Theory data holds plain codes for readability; identity only ever publishes them sealed.</summary>
    private static object Sealed(IAlbaHost host, object issued)
    {
        var seal = host.Services.GetRequiredService<IOwnerCodeSeal>();
        return issued switch
        {
            OwnerVerificationCodeIssued m => m with { SealedCode = seal.Seal(m.SealedCode) },
            PasswordResetCodeIssued m => m with { SealedCode = seal.Seal(m.SealedCode) },
            EmailChangeCodeIssued m => m with { SealedCode = seal.Seal(m.SealedCode) },
            _ => throw new ArgumentOutOfRangeException(nameof(issued)),
        };
    }
    [Fact]
    public void Development_WithoutAResendKey_LogsMailInsteadOfSendingIt()
    {
        using var scope = fixture.Host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMailer>().GetType().Name.ShouldBe("LoggingMailer");
    }

    [Fact]
    public async Task Development_LogsThePublishedMail_WithoutTheCodeOrTheRecipient()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await fixture.CreateHostAsync(services => services.AddSingleton<ILoggerProvider>(logs));

        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(30))
            .PublishMessageAndWaitAsync(Sealed(host, new OwnerVerificationCodeIssued(Guid.NewGuid(), "Ana", "log@example.test", "444444", Expires)));

        var line = logs.Messages.Where(m => m.StartsWith("Mail to *@example.test", StringComparison.Ordinal)).ShouldHaveSingleItem();
        line.ShouldContain("text+html");
        logs.Messages.ShouldAllBe(m => !m.Contains("444444", StringComparison.Ordinal) && !m.Contains("log@example.test", StringComparison.Ordinal));
        TestContext.Current.SendDiagnosticMessage(line);
    }

    [Fact]
    public async Task Production_UsesResend()
    {
        await using var host = await fixture.CreateHostAsync(environment: "Production");
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMailer>().GetType().Name.ShouldBe("ResendMailer");
    }

    internal sealed class CapturingMailer : IMailer
    {
        private readonly List<Mail> _sent = [];

        public void Clear()
        {
            lock (_sent)
                _sent.Clear();
        }

        public IReadOnlyList<Mail> Sent
        {
            get
            {
                lock (_sent)
                    return [.. _sent];
            }
        }

        public Task SendAsync(Mail mail, CancellationToken cancellationToken)
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
