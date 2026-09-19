package com.frappe.platform.infrastructure.web;

import com.frappe.platform.SecretStoreUnavailableException;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.ResolvedSession;
import com.frappe.platform.web.SessionKind;
import com.frappe.platform.web.SessionResolver;
import io.lettuce.core.ClientOptions;
import io.lettuce.core.RedisClient;
import io.lettuce.core.RedisConnectionException;
import io.lettuce.core.RedisURI;
import io.lettuce.core.SocketOptions;
import io.nats.client.Nats;
import io.nats.client.Options;
import jakarta.servlet.Filter;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;
import java.time.Duration;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.web.servlet.FilterRegistrationBean;
import org.springframework.context.annotation.Bean;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.client.RestClient;

/** Routes for the problem detail tests; importing this registers its member route classes. */
@TestConfiguration(proxyBeanMethods = false)
public class ProblemRoutes {

    /** The one bearer token the test session resolver resolves. */
    public static final String TOKEN = "problem-token";

    /** The authenticated route's path, with the {@code /v1} prefix. */
    public static final String SELF_PATH = "/v1/test/problems/self";

    /** The failing route's path, with the {@code /v1} prefix; append the failure kind. */
    public static final String FAILURES_PATH = "/v1/test/problems/failures/";

    /** The path a failing filter throws for, before any controller runs. */
    public static final String FAILING_FILTER_PATH = FAILURES_PATH + "filter";

    /** The validated route's path, with the {@code /v1} prefix. */
    public static final String ORDERS_PATH = "/v1/test/problems/orders";

    @Bean
    SessionResolver problemSessionResolver() {
        var session = new ResolvedSession(UUID.randomUUID(), UUID.randomUUID(), SessionKind.PERSON);
        return token -> TOKEN.equals(token) ? Optional.of(session) : Optional.empty();
    }

    @Bean
    FilterRegistrationBean<Filter> failingFilter() {
        Filter filter = (request, response, chain) -> {
            throw new IllegalStateException("Session store at valkey://10.0.0.5:6379 rejected token eyJhbGciOi");
        };
        var registration = new FilterRegistrationBean<>(filter);
        registration.addUrlPatterns(FAILING_FILTER_PATH);
        return registration;
    }

    /** Fails the way an infrastructure dependency does, with the real client libraries. */
    @RestController
    @Access(Posture.PUBLIC)
    static class FailureRoute {

        private final JdbcClient database;

        FailureRoute(JdbcClient database) {
            this.database = database;
        }

        @GetMapping("/test/problems/failures/{kind}")
        String fail(@PathVariable String kind) throws Exception {
            return switch (kind) {
                case "database-down" ->
                    JdbcClient.create(new DriverManagerDataSource(
                                    "jdbc:postgresql://127.0.0.1:1/frappe", "frappe_app", "app-password"))
                            .sql("select secret from platform.customer_card")
                            .query(String.class)
                            .single();
                case "sql-error" ->
                    database.sql("select card_number from platform.customer_card where id = 7")
                            .query(String.class)
                            .single();
                case "provider" ->
                    RestClient.create()
                            .get()
                            .uri("http://127.0.0.1:1/v1/charges?api_key=sk_live_fake_test_value")
                            .retrieve()
                            .body(String.class);
                case "nats" -> {
                    try (var connection = Nats.connect(new Options.Builder()
                            .server("nats://127.0.0.1:1")
                            .connectionTimeout(Duration.ofMillis(500))
                            .noReconnect()
                            .build())) {
                        yield connection.getServerInfo().getServerId();
                    }
                }
                case "valkey" -> {
                    var client = RedisClient.create(RedisURI.create("redis://127.0.0.1:1"));
                    client.setOptions(ClientOptions.builder()
                            .socketOptions(SocketOptions.builder()
                                    .connectTimeout(Duration.ofMillis(500))
                                    .build())
                            .build());
                    try (var connection = client.connect()) {
                        yield connection.sync().get("secret");
                    } catch (RedisConnectionException e) {
                        throw new SecretStoreUnavailableException("Valkey at redis://127.0.0.1:1 is unreachable", e);
                    } finally {
                        client.shutdown(Duration.ZERO, Duration.ofSeconds(1));
                    }
                }
                default -> throw new IllegalArgumentException("Unknown failure kind " + kind);
            };
        }
    }

    /** Answers the caller. */
    @RestController
    @Access(Posture.AUTHENTICATED)
    static class SelfRoute {

        @GetMapping("/test/problems/self")
        String self(ResolvedSession caller) {
            return caller.principalId().toString();
        }
    }

    /** A request body with constraints on its fields, a nested list and its elements. */
    record OrderRequest(
            @NotBlank String name,
            @Size(min = 2, max = 5) String code,
            @NotNull @Valid List<Line> lines) {}

    /** One line of an order. */
    record Line(@Min(1) int quantity) {}

    /** Accepts a valid order. */
    @RestController
    @Access(Posture.PUBLIC)
    static class OrdersRoute {

        @PostMapping("/test/problems/orders")
        String place(@Valid @RequestBody OrderRequest order) {
            return "placed " + order.name();
        }
    }

    /** Only answers GET, so other methods are not allowed. */
    @RestController
    @Access(Posture.PUBLIC)
    static class ReadOnlyRoute {

        @GetMapping("/test/problems/read-only")
        String read() {
            return "read";
        }
    }
}
