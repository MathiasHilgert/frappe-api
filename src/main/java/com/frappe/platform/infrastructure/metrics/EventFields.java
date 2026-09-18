package com.frappe.platform.infrastructure.metrics;

import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.RecordComponent;
import java.time.Duration;
import java.util.Arrays;
import java.util.Currency;
import java.util.Locale;
import java.util.Optional;
import java.util.Set;

/** Reads the fields of domain event records that annotations name, and classifies their types. */
final class EventFields {

    private static final Set<Class<?>> NUMBERS =
            Set.of(int.class, long.class, double.class, float.class, short.class, byte.class);
    private static final String MINOR_UNITS = "minorUnits";
    private static final String CURRENCY = "currency";

    private EventFields() {}

    /**
     * Finds a record component by name.
     *
     * @param eventType the event record
     * @param name the field name
     * @return the component, if the record has it
     */
    static Optional<RecordComponent> find(Class<?> eventType, String name) {
        return Arrays.stream(eventType.getRecordComponents())
                .filter(component -> component.getName().equals(name))
                .findFirst()
                .map(EventFields::accessible);
    }

    /**
     * Whether a field type has bounded values and may become a tag.
     *
     * @param type the field type
     * @return {@code true} for enums and booleans
     */
    static boolean isBounded(Class<?> type) {
        return type.isEnum() || type == boolean.class || type == Boolean.class;
    }

    /**
     * Whether a field type is a plain number.
     *
     * @param type the field type
     * @return {@code true} for numeric primitives and {@link Number} subclasses
     */
    static boolean isNumber(Class<?> type) {
        return NUMBERS.contains(type) || Number.class.isAssignableFrom(type);
    }

    /**
     * Whether a field type is money: a record with {@code long minorUnits} and {@code Currency currency}, the shape of
     * the kernel's {@code Money}.
     *
     * @param type the field type
     * @return {@code true} for money
     */
    static boolean isMoney(Class<?> type) {
        return type.isRecord()
                && find(type, MINOR_UNITS)
                        .filter(it -> it.getType() == long.class)
                        .isPresent()
                && find(type, CURRENCY)
                        .filter(it -> it.getType() == Currency.class)
                        .isPresent();
    }

    /**
     * Whether a field type is a duration.
     *
     * @param type the field type
     * @return {@code true} for {@link Duration}
     */
    static boolean isDuration(Class<?> type) {
        return type == Duration.class;
    }

    /**
     * Reads a tag value: enum names in lowercase, booleans as {@code true}/{@code false}.
     *
     * @param event the event
     * @param component an enum or boolean component
     * @return the tag value
     * @throws MetricRecordingException if the field is null or cannot be read
     */
    static String tagValue(Object event, RecordComponent component) {
        return switch (read(event, component)) {
            case Enum<?> constant -> constant.name().toLowerCase(Locale.ROOT);
            case Object flag -> flag.toString();
        };
    }

    /**
     * Reads a measured value in its base unit.
     *
     * @param event the event
     * @param component a number, duration or money component
     * @return the measurement
     * @throws MetricRecordingException if the field is null or cannot be read
     */
    static BusinessMetric.Measurement measurement(Object event, RecordComponent component) {
        return switch (read(event, component)) {
            case Number number -> new BusinessMetric.Measurement(number.doubleValue(), null);
            case Duration duration -> new BusinessMetric.Measurement(duration.toNanos() / 1e9, null);
            case Record money -> moneyMeasurement(money);
            case Object other ->
                throw new MetricRecordingException("Field '" + component.getName() + "' of "
                        + event.getClass().getSimpleName() + " holds an unsupported " + other.getClass());
        };
    }

    private static BusinessMetric.Measurement moneyMeasurement(Record money) {
        var type = money.getClass();
        var minorUnits = (Long) read(money, find(type, MINOR_UNITS).orElseThrow());
        var currency = (Currency) read(money, find(type, CURRENCY).orElseThrow());
        return new BusinessMetric.Measurement(minorUnits, currency.getCurrencyCode());
    }

    private static Object read(Object source, RecordComponent component) {
        try {
            var value = component.getAccessor().invoke(source);
            if (value == null) {
                throw new MetricRecordingException("Field '" + component.getName() + "' of "
                        + source.getClass().getSimpleName() + " is null");
            }
            return value;
        } catch (IllegalAccessException | InvocationTargetException e) {
            throw new MetricRecordingException(
                    "Cannot read field '" + component.getName() + "' of "
                            + source.getClass().getSimpleName(),
                    e);
        }
    }

    // Module-private events and test fixtures are not public; the accessor is still part of the record's contract.
    private static RecordComponent accessible(RecordComponent component) {
        component.getAccessor().setAccessible(true);
        return component;
    }
}
