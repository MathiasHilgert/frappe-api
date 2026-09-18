package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import org.springframework.core.annotation.AnnotatedElementUtils;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.RoutingTarget;

/** Maps an {@code @Externalized} event to its NATS subject and rejects events that break the contract. */
final class ExternalizedEventRouter {

    private ExternalizedEventRouter() {}

    /**
     * Routes the event to {@code frappe.<module>.<event-kebab>.v<eventVersion>}.
     *
     * @param event an event selected for externalization
     * @return the routing target holding the subject
     * @throws InvalidExternalizedEventException if the event lacks the envelope or declares its own target
     */
    static RoutingTarget route(Object event) {
        if (!(event instanceof DomainEvent domainEvent)) {
            throw new InvalidExternalizedEventException(event.getClass().getName()
                    + " is @Externalized but does not implement " + DomainEvent.class.getName());
        }
        var subject = NatsSubjects.of(domainEvent);
        var declaredTarget = declaredTarget(event);
        if (!declaredTarget.isEmpty()) {
            throw new InvalidExternalizedEventException(event.getClass().getName() + " declares @Externalized(\""
                    + declaredTarget + "\"), but the subject is derived (" + subject
                    + "); remove the annotation value");
        }
        return RoutingTarget.forTarget(subject).withoutKey();
    }

    private static String declaredTarget(Object event) {
        var annotation = AnnotatedElementUtils.findMergedAnnotation(event.getClass(), Externalized.class);
        return annotation == null ? "" : annotation.value();
    }
}
