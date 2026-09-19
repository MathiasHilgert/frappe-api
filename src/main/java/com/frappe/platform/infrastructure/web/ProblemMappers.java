package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Problem;
import com.frappe.platform.web.ProblemMapper;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;

/**
 * The registry of every module's {@link ProblemMapper}: finds the one mapper a business failure belongs to. Checked at
 * startup: two mappers whose failure types are the same, or one a subtype of the other, could both answer a failure, so
 * the application refuses to start and names both types.
 */
final class ProblemMappers {

    private final List<ProblemMapper<?>> mappers;

    /**
     * Checks and registers the mappers.
     *
     * @param mappers every mapper bean
     * @throws IllegalStateException when two mappers overlap
     */
    ProblemMappers(List<? extends ProblemMapper<?>> mappers) {
        var problems = new ArrayList<String>();
        for (var i = 0; i < mappers.size(); i++) {
            for (var j = i + 1; j < mappers.size(); j++) {
                var first = mappers.get(i).failureType();
                var second = mappers.get(j).failureType();
                if (first.isAssignableFrom(second) || second.isAssignableFrom(first)) {
                    problems.add(first.getName() + " and " + second.getName());
                }
            }
        }
        if (!problems.isEmpty()) {
            throw new IllegalStateException("Overlapping problem mappers for " + String.join("; ", problems)
                    + ": keep one ProblemMapper per failure type, so each failure has exactly one problem");
        }
        this.mappers = List.copyOf(mappers);
    }

    /**
     * The problem a failure is mapped to.
     *
     * @param failure a business failure a use case returned
     * @return its problem, or empty when no mapper handles its type (a bug)
     */
    Optional<Problem> problemOf(Object failure) {
        return mappers.stream()
                .filter(mapper -> mapper.failureType().isInstance(failure))
                .findFirst()
                .map(mapper -> mapped(mapper, failure));
    }

    private static <E> Problem mapped(ProblemMapper<E> mapper, Object failure) {
        return mapper.problemOf(mapper.failureType().cast(failure));
    }
}
