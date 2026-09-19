package com.frappe.platform.infrastructure.bus;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.Command;
import com.frappe.platform.CommandBus;
import com.frappe.platform.CommandHandler;
import com.frappe.platform.Query;
import com.frappe.platform.QueryBus;
import com.frappe.platform.QueryHandler;
import com.frappe.platform.Result;
import fixtures.bus.OutsideModuleCommand;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.transaction.annotation.Transactional;

/** Startup discovery of handlers and routing of messages, without a database. */
class HandlerDiscoveryTest {

    private final ApplicationContextRunner contextRunner =
            new ApplicationContextRunner().withUserConfiguration(BusConfiguration.class);

    enum TabError {
        ALREADY_OPEN
    }

    record OpenTab(String table) implements Command<Result<String, TabError>> {}

    record FindTab(String table) implements Query<String> {}

    record CountTabs() implements Query<Integer> {}

    record CloseTab(String table) implements Command<Result<String, TabError>> {}

    static class OpenTabHandler implements CommandHandler<OpenTab, Result<String, TabError>> {

        static final Result<String, TabError> OPENED = Result.success("tab-1");

        final AtomicInteger calls = new AtomicInteger();

        @Override
        @Transactional
        public Result<String, TabError> handle(OpenTab command) {
            calls.incrementAndGet();
            return OPENED;
        }
    }

    @Transactional
    static class SecondOpenTabHandler implements CommandHandler<OpenTab, Result<String, TabError>> {

        @Override
        public Result<String, TabError> handle(OpenTab command) {
            return Result.failure(TabError.ALREADY_OPEN);
        }
    }

    static class NonTransactionalCloseTabHandler implements CommandHandler<CloseTab, Result<String, TabError>> {

        @Override
        public Result<String, TabError> handle(CloseTab command) {
            return Result.success(command.table());
        }
    }

    static class FindTabHandler implements QueryHandler<FindTab, String> {

        @Override
        public String handle(FindTab query) {
            return "tab at " + query.table();
        }
    }

    static class GenericQueryHandler<Q extends Query<String>> implements QueryHandler<Q, String> {

        @Override
        public String handle(Q query) {
            return "generic";
        }
    }

    @Transactional
    static class OutsideModuleCommandHandler implements CommandHandler<OutsideModuleCommand, String> {

        @Override
        public String handle(OutsideModuleCommand command) {
            return "handled";
        }
    }

    @Test
    void dispatchingACommandRunsItsSingleHandlerOnceAndReturnsItsResultUnchanged() {
        contextRunner.withBean(OpenTabHandler.class).run(context -> {
            // Given
            var bus = context.getBean(CommandBus.class);
            var handler = context.getBean(OpenTabHandler.class);

            // When
            var result = bus.dispatch(new OpenTab("7"));

            // Then
            assertThat(result).isSameAs(OpenTabHandler.OPENED);
            assertThat(handler.calls).hasValue(1);
        });
    }

    @Test
    void askingAQueryReturnsWhatItsHandlerReturns() {
        contextRunner.withBean(FindTabHandler.class).run(context -> {
            // When
            var answer = context.getBean(QueryBus.class).ask(new FindTab("7"));

            // Then
            assertThat(answer).isEqualTo("tab at 7");
        });
    }

    @Test
    void twoHandlersForOneTypeFailStartupNamingBothBeans() {
        contextRunner
                .withBean("openTabHandler", OpenTabHandler.class)
                .withBean("secondOpenTabHandler", SecondOpenTabHandler.class)
                .run(context -> assertThat(context)
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidHandlersException.class)
                        .hasMessageContaining(OpenTab.class.getName())
                        .hasMessageContaining("'openTabHandler'")
                        .hasMessageContaining("'secondOpenTabHandler'"));
    }

    @Test
    void askingAQueryWithoutAHandlerThrowsMissingHandlerExceptionNamingTheType() {
        contextRunner.withBean(FindTabHandler.class).run(context -> {
            // Given
            var bus = context.getBean(QueryBus.class);

            // When / Then
            assertThatThrownBy(() -> bus.ask(new CountTabs()))
                    .isInstanceOf(MissingHandlerException.class)
                    .hasMessageContaining(CountTabs.class.getName());
        });
    }

    @Test
    void dispatchingACommandWithoutAHandlerThrowsMissingHandlerExceptionNamingTheType() {
        contextRunner.run(context -> {
            // Given
            var bus = context.getBean(CommandBus.class);

            // When / Then
            assertThatThrownBy(() -> bus.dispatch(new OpenTab("7")))
                    .isInstanceOf(MissingHandlerException.class)
                    .hasMessageContaining(OpenTab.class.getName());
        });
    }

    @Test
    void aCommandHandlerWithoutATransactionFailsStartup() {
        contextRunner
                .withBean("closeTabHandler", NonTransactionalCloseTabHandler.class)
                .run(context -> assertThat(context)
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidHandlersException.class)
                        .hasMessageContaining("'closeTabHandler'")
                        .hasMessageContaining("@Transactional"));
    }

    @Test
    void aHandlerWhoseMessageTypeCannotBeResolvedFailsStartup() {
        contextRunner
                .withBean("genericQueryHandler", GenericQueryHandler.class)
                .run(context -> assertThat(context)
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidHandlersException.class)
                        .hasMessageContaining("'genericQueryHandler'")
                        .hasMessageContaining("QueryHandler<YourQuery, YourResult>"));
    }

    @Test
    void aMessageOutsideAModulePackageFailsStartup() {
        contextRunner
                .withBean("outsideModuleCommandHandler", OutsideModuleCommandHandler.class)
                .run(context -> assertThat(context)
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidHandlersException.class)
                        .hasMessageContaining(OutsideModuleCommand.class.getName())
                        .hasMessageContaining("com.frappe.<module>"));
    }
}
