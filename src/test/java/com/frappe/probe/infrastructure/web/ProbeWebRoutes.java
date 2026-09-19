package com.frappe.probe.infrastructure.web;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.Problem;
import com.frappe.platform.web.ProblemMapper;
import com.frappe.platform.web.RequestRefusedException;
import com.frappe.probe.application.RecordProbe;
import java.util.UUID;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * The probe module's web adapter, as a real module writes it: a route that calls a use case and turns its failure into
 * a {@link RequestRefusedException}, and the module's {@link ProblemMapper}. Importing this registers them.
 */
@TestConfiguration(proxyBeanMethods = false)
public class ProbeWebRoutes {

    /** The route's path, with the {@code /v1} prefix. */
    public static final String PROBES_PATH = "/v1/test/probes";

    /** How many probes the (fictional) limit allows; a param of the mapped problem. */
    public static final int PROBE_LIMIT = 3;

    /** A failure type no mapper handles. */
    public enum UnmappedError {
        SURPRISE
    }

    /** A failure type whose mapper names a key no catalog has. */
    public enum MisconfiguredError {
        NO_TEXT
    }

    @Bean
    ProblemMapper<RecordProbe.ProbeError> probeProblems() {
        return new ProblemMapper<>() {
            @Override
            public Class<RecordProbe.ProbeError> failureType() {
                return RecordProbe.ProbeError.class;
            }

            @Override
            public Problem problemOf(RecordProbe.ProbeError failure) {
                return switch (failure) {
                    case REJECTED ->
                        Problem.of(409, "probe-rejected", "probe.recording.rejected")
                                .with("limit", PROBE_LIMIT);
                };
            }
        };
    }

    @Bean
    ProblemMapper<MisconfiguredError> misconfiguredProblems() {
        return new ProblemMapper<>() {
            @Override
            public Class<MisconfiguredError> failureType() {
                return MisconfiguredError.class;
            }

            @Override
            public Problem problemOf(MisconfiguredError failure) {
                return Problem.of(422, "probe-without-text", "probe.recording.no-such-key");
            }
        };
    }

    /** Records a probe through the use case and answers its id, or its failure as a problem. */
    @RestController
    @Access(Posture.PUBLIC)
    static class RecordProbeRoute {

        private final RecordProbe recordProbe;

        RecordProbeRoute(RecordProbe recordProbe) {
            this.recordProbe = recordProbe;
        }

        @PostMapping("/test/probes")
        String record(@RequestParam RecordProbe.Ending ending) {
            var probeId = recordProbe
                    .record(UUID.randomUUID(), UUID.randomUUID(), ending)
                    .orElseThrow(RequestRefusedException::new);
            return probeId.toString();
        }
    }

    /** Refuses with a failure no mapper handles, or with one whose text is missing. */
    @RestController
    @Access(Posture.PUBLIC)
    static class MisconfiguredRoute {

        @PostMapping("/test/probes/misconfigured")
        String refuse(@RequestParam boolean mapped) {
            throw new RequestRefusedException(mapped ? MisconfiguredError.NO_TEXT : UnmappedError.SURPRISE);
        }
    }
}
