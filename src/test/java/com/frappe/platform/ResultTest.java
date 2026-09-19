package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import com.frappe.platform.Result.Failure;
import com.frappe.platform.Result.Success;
import org.junit.jupiter.api.Test;

class ResultTest {

    enum TabError {
        ALREADY_CLOSED,
        NOT_FOUND
    }

    @Test
    void mappingASuccessTransformsItsValue() {
        // Given
        Result<Integer, TabError> result = Result.success(2);

        // When
        var mapped = result.map(value -> value * 10);

        // Then
        assertThat(mapped).isEqualTo(new Success<Integer, TabError>(20));
    }

    @Test
    void mappingAFailureKeepsTheErrorAndNeverCallsTheMapper() {
        // Given
        Result<Integer, TabError> result = Result.failure(TabError.ALREADY_CLOSED);

        // When
        var mapped = result.map(ResultTest::neverCalled);

        // Then
        assertThat(mapped).isEqualTo(new Failure<Integer, TabError>(TabError.ALREADY_CLOSED));
    }

    @Test
    void flatMappingASuccessReturnsTheNextResult() {
        // Given
        Result<Integer, TabError> result = Result.success(2);

        // When
        var chained = result.flatMap(value -> Result.<String, TabError>failure(TabError.NOT_FOUND));

        // Then
        assertThat(chained).isEqualTo(new Failure<String, TabError>(TabError.NOT_FOUND));
    }

    @Test
    void flatMappingAFailureShortCircuits() {
        // Given
        Result<Integer, TabError> result = Result.failure(TabError.ALREADY_CLOSED);

        // When
        Result<String, TabError> chained = result.flatMap(ResultTest::neverCalled);

        // Then
        assertThat(chained).isEqualTo(new Failure<String, TabError>(TabError.ALREADY_CLOSED));
    }

    @Test
    void mappingTheFailureTransformsOnlyTheError() {
        // Given
        Result<Integer, TabError> failure = Result.failure(TabError.NOT_FOUND);
        Result<Integer, TabError> success = Result.success(1);

        // When
        var mappedFailure = failure.mapFailure(TabError::name);
        var mappedSuccess = success.mapFailure(ResultTest::neverCalled);

        // Then
        assertThat(mappedFailure).isEqualTo(new Failure<Integer, String>("NOT_FOUND"));
        assertThat(mappedSuccess).isEqualTo(new Success<Integer, String>(1));
    }

    @Test
    void foldingAppliesTheFunctionOfTheBranch() {
        // Given
        Result<Integer, TabError> success = Result.success(7);
        Result<Integer, TabError> failure = Result.failure(TabError.ALREADY_CLOSED);

        // When
        var fromSuccess = success.fold(value -> "value " + value, error -> "error " + error);
        var fromFailure = failure.fold(value -> "value " + value, error -> "error " + error);

        // Then
        assertThat(fromSuccess).isEqualTo("value 7");
        assertThat(fromFailure).isEqualTo("error ALREADY_CLOSED");
    }

    @Test
    void aResultNeverHoldsNull() {
        // When / Then
        assertThatNullPointerException().isThrownBy(() -> Result.success(null));
        assertThatNullPointerException().isThrownBy(() -> Result.failure(null));
        assertThatNullPointerException()
                .isThrownBy(() -> Result.<Integer, TabError>success(1).map(value -> null));
    }

    @Test
    void patternMatchingReadsTheOutcome() {
        // Given
        Result<Integer, TabError> result = Result.failure(TabError.NOT_FOUND);

        // When
        var description =
                switch (result) {
                    case Success<Integer, TabError>(var value) -> "closed " + value;
                    case Failure<Integer, TabError>(var error) -> "refused " + error;
                };

        // Then
        assertThat(description).isEqualTo("refused NOT_FOUND");
    }

    private static <T, U> U neverCalled(T input) {
        throw new AssertionError("mapper must not be called for " + input);
    }
}
