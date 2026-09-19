package com.frappe.platform.web;

/**
 * Maps one module's business failure type to the {@link Problem} its routes answer with: one bean per failure type, in
 * the module's {@code infrastructure.web} package, so each failure has one place that decides its status, problem type
 * and message key. A route turns a failed {@code Result} into a {@link RequestRefusedException}; the platform finds the
 * mapper by the failure's type and answers the localized problem.
 *
 * {@snippet :
 * @Component
 * class TabProblems implements ProblemMapper<TabError> {
 *     public Class<TabError> failureType() { return TabError.class; }
 *
 *     public Problem problemOf(TabError error) {
 *         return switch (error) {
 *             case ALREADY_CLOSED -> Problem.of(409, "tab-already-closed", "order.tab.already-closed");
 *             case NOT_FOUND -> Problem.of(404, "tab-not-found", "order.tab.not-found");
 *         };
 *     }
 * }
 * }
 *
 * <p>Two mappers for the same failure type fail startup. A failure no mapper handles is a bug: the client gets the
 * generic internal-error problem and the failure is logged.
 *
 * @param <E> the failure type, usually the error enum or sealed interface of a module's use cases
 */
public interface ProblemMapper<E> {

    /**
     * The failure type this mapper handles; failures are matched by {@link Class#isInstance}.
     *
     * @return the failure type
     */
    Class<E> failureType();

    /**
     * The problem a failure of this type is answered with.
     *
     * @param failure the failure
     * @return its problem
     */
    Problem problemOf(E failure);
}
