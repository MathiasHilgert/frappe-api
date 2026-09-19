package com.frappe.platform.infrastructure.usecase;

/** The two kinds of use case, with the value of the {@code use_case.kind} tag. */
enum UseCaseKind {

    /** Changes state ({@code @CommandUseCase}). */
    COMMAND("command"),

    /** Reads state ({@code @QueryUseCase}). */
    QUERY("query");

    private final String tagValue;

    UseCaseKind(String tagValue) {
        this.tagValue = tagValue;
    }

    /**
     * The value of the {@code use_case.kind} tag.
     *
     * @return {@code command} or {@code query}
     */
    String tagValue() {
        return tagValue;
    }
}
