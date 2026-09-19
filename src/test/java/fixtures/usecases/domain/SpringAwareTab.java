package fixtures.usecases.domain;

import org.springframework.util.Assert;

/** A domain type that depends on Spring, which the domain must never do. */
public record SpringAwareTab(String table) {

    public SpringAwareTab {
        Assert.hasText(table, "table must not be empty");
    }
}
