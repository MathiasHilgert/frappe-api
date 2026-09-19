package com.frappe.platform.infrastructure.tracing;

import io.micrometer.observation.Observation;
import io.micrometer.tracing.Link;
import io.micrometer.tracing.Span;
import io.micrometer.tracing.Tracer;
import io.micrometer.tracing.handler.TracingObservationHandler;

/**
 * Creates the span of a {@link LinkedMessageContext}: a PRODUCER or CONSUMER span that is a child of the current span
 * (if any) and links to the creation context of the message. Registered before Spring Boot's tracing handlers, which
 * would otherwise create an unlinked INTERNAL span for it.
 */
final class LinkedMessageTracingHandler implements TracingObservationHandler<LinkedMessageContext> {

    /** Ahead of Boot's receiver (1000), sender (2000) and default handlers; only this context type is affected. */
    static final int ORDER = 0;

    private final Tracer tracer;

    /**
     * Creates the handler.
     *
     * @param tracer creates the spans
     */
    LinkedMessageTracingHandler(Tracer tracer) {
        this.tracer = tracer;
    }

    @Override
    public void onStart(LinkedMessageContext context) {
        var builder = tracer.spanBuilder()
                .name(getSpanName(context))
                .kind(Span.Kind.valueOf(context.getKind().name()));
        // Without an explicit parent the builder starts a new trace; the current span stays the parent.
        var parent = getParentSpan(context);
        if (parent != null) {
            builder = builder.setParent(parent.context());
        }
        var creationContext = context.getCreationContext();
        if (creationContext.isPresent()) {
            builder = builder.addLink(linkTo(creationContext.get()));
        }
        getTracingContext(context).setSpan(builder.start());
    }

    @Override
    public void onStop(LinkedMessageContext context) {
        var span = getRequiredSpan(context);
        tagSpan(context, span);
        span.name(getSpanName(context));
        endSpan(context, span);
    }

    @Override
    public boolean supportsContext(Observation.Context context) {
        return context instanceof LinkedMessageContext;
    }

    @Override
    public Tracer getTracer() {
        return tracer;
    }

    private Link linkTo(W3cTraceContext creationContext) {
        return new Link(tracer.traceContextBuilder()
                .traceId(creationContext.traceId())
                .spanId(creationContext.spanId())
                .sampled(creationContext.sampled())
                .build());
    }
}
