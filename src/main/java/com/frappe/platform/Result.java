package com.frappe.platform;

import java.util.Objects;
import java.util.function.Function;

/**
 * The outcome of an operation that can fail for an expected business reason: either a {@link Success} holding the
 * value or a {@link Failure} holding the error. Expected failures (a closed tab, an unknown user) are values of this
 * type, never exceptions; exceptions stay reserved for bugs and infrastructure faults. The platform observes a
 * use case returning a {@code Failure} as outcome {@code failure} (and rolls back the transaction it started), an
 * exception as outcome {@code error}.
 *
 * <p>Neither branch ever holds {@code null}. Read a result with {@link #fold} or by pattern matching:
 *
 * {@snippet :
 * switch (result) {
 *     case Result.Success<TabId, TabError>(var id) -> created(id);
 *     case Result.Failure<TabId, TabError>(var error) -> refused(error);
 * }
 * }
 *
 * @param <T> the value type of a success
 * @param <E> the error type of a failure, typically an enum or sealed interface of the module
 */
public sealed interface Result<T, E> permits Result.Success, Result.Failure {

    /**
     * Creates a successful result.
     *
     * @param value the value, not {@code null}
     * @param <T> the value type
     * @param <E> the error type
     * @return a {@link Success} holding the value
     * @throws NullPointerException if the value is {@code null}
     */
    static <T, E> Result<T, E> success(T value) {
        return new Success<>(value);
    }

    /**
     * Creates a failed result.
     *
     * @param error the expected failure, not {@code null}
     * @param <T> the value type
     * @param <E> the error type
     * @return a {@link Failure} holding the error
     * @throws NullPointerException if the error is {@code null}
     */
    static <T, E> Result<T, E> failure(E error) {
        return new Failure<>(error);
    }

    /**
     * Transforms the value of a success; a failure is returned as is and the mapper is not called.
     *
     * @param mapper computes the new value, never returning {@code null}
     * @param <U> the new value type
     * @return the mapped result
     * @throws NullPointerException if the mapper returns {@code null}
     */
    <U> Result<U, E> map(Function<? super T, ? extends U> mapper);

    /**
     * Chains an operation that can fail itself; a failure short-circuits and the mapper is not called.
     *
     * @param mapper computes the next result from the value
     * @param <U> the value type of the next result
     * @return the next result, or this failure
     */
    <U> Result<U, E> flatMap(Function<? super T, ? extends Result<U, E>> mapper);

    /**
     * Transforms the error of a failure, e.g. into the error type of the calling layer; a success is returned as is.
     *
     * @param mapper computes the new error, never returning {@code null}
     * @param <F> the new error type
     * @return the result with the mapped error
     * @throws NullPointerException if the mapper returns {@code null}
     */
    <F> Result<T, F> mapFailure(Function<? super E, ? extends F> mapper);

    /**
     * Reduces the result to one value by applying the function of its branch.
     *
     * @param onSuccess applied to the value of a success
     * @param onFailure applied to the error of a failure
     * @param <R> the reduced type
     * @return what the applied function returned
     */
    <R> R fold(Function<? super T, ? extends R> onSuccess, Function<? super E, ? extends R> onFailure);

    /**
     * A successful result.
     *
     * @param value the value, never {@code null}
     * @param <T> the value type
     * @param <E> the error type
     */
    record Success<T, E>(T value) implements Result<T, E> {

        /**
         * Validates the value.
         *
         * @param value the value, not {@code null}
         * @throws NullPointerException if the value is {@code null}
         */
        public Success {
            Objects.requireNonNull(value, "value must not be null");
        }

        @Override
        public <U> Result<U, E> map(Function<? super T, ? extends U> mapper) {
            return new Success<>(mapper.apply(value));
        }

        @Override
        public <U> Result<U, E> flatMap(Function<? super T, ? extends Result<U, E>> mapper) {
            return mapper.apply(value);
        }

        @Override
        public <F> Result<T, F> mapFailure(Function<? super E, ? extends F> mapper) {
            return new Success<>(value);
        }

        @Override
        public <R> R fold(Function<? super T, ? extends R> onSuccess, Function<? super E, ? extends R> onFailure) {
            return onSuccess.apply(value);
        }
    }

    /**
     * A result that failed for an expected business reason.
     *
     * @param error the reason, never {@code null}
     * @param <T> the value type
     * @param <E> the error type
     */
    record Failure<T, E>(E error) implements Result<T, E> {

        /**
         * Validates the error.
         *
         * @param error the reason, not {@code null}
         * @throws NullPointerException if the error is {@code null}
         */
        public Failure {
            Objects.requireNonNull(error, "error must not be null");
        }

        @Override
        public <U> Result<U, E> map(Function<? super T, ? extends U> mapper) {
            return new Failure<>(error);
        }

        @Override
        public <U> Result<U, E> flatMap(Function<? super T, ? extends Result<U, E>> mapper) {
            return new Failure<>(error);
        }

        @Override
        public <F> Result<T, F> mapFailure(Function<? super E, ? extends F> mapper) {
            return new Failure<>(mapper.apply(error));
        }

        @Override
        public <R> R fold(Function<? super T, ? extends R> onSuccess, Function<? super E, ? extends R> onFailure) {
            return onFailure.apply(error);
        }
    }
}
