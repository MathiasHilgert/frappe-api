package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.Operation;
import io.swagger.v3.oas.models.responses.ApiResponse;
import io.swagger.v3.oas.models.responses.ApiResponses;
import io.swagger.v3.oas.models.security.SecurityRequirement;
import io.swagger.v3.oas.models.security.SecurityScheme;
import java.util.regex.Pattern;
import org.springdoc.core.customizers.OpenApiCustomizer;
import org.springdoc.core.customizers.OperationCustomizer;
import org.springframework.core.annotation.AnnotatedElementUtils;
import org.springframework.web.method.HandlerMethod;

/**
 * Documents each route's posture in the OpenAPI spec: every authenticated operation requires the bearer scheme and
 * documents 401, and every business-scoped operation documents 404.
 */
final class PostureDocumentation implements OperationCustomizer, OpenApiCustomizer {

    /** Name of the bearer security scheme in the spec. */
    static final String BEARER_SCHEME = "bearer";

    /** Paths of business-scoped routes, which answer 404 for a business the caller may not enter. */
    private static final Pattern BUSINESS_SCOPED = Pattern.compile("^/v1/businesses/\\{[^/{}]+}(/.*)?$");

    /** Creates the customizer. */
    PostureDocumentation() {}

    @Override
    public void customise(OpenAPI openApi) {
        openApi.schemaRequirement(
                BEARER_SCHEME,
                new SecurityScheme()
                        .type(SecurityScheme.Type.HTTP)
                        .scheme("bearer")
                        .description("An opaque session token, sent as 'Authorization: Bearer <token>' only."));
        if (openApi.getPaths() == null) {
            return;
        }
        openApi.getPaths().forEach((path, item) -> {
            if (BUSINESS_SCOPED.matcher(path).matches()) {
                item.readOperations().forEach(PostureDocumentation::documentBusinessNotFound);
            }
        });
    }

    private static void documentBusinessNotFound(Operation operation) {
        if (operation.getResponses() == null) {
            operation.setResponses(new ApiResponses());
        }
        operation
                .getResponses()
                .addApiResponse(
                        "404",
                        new ApiResponse().description("The business does not exist, or the caller may not enter it"));
    }

    @Override
    public Operation customize(Operation operation, HandlerMethod handlerMethod) {
        var access = AnnotatedElementUtils.findMergedAnnotation(handlerMethod.getBeanType(), Access.class);
        if (access == null || access.value() == Posture.PUBLIC) {
            return operation;
        }
        if (operation.getResponses() == null) {
            operation.setResponses(new ApiResponses());
        }
        operation.addSecurityItem(new SecurityRequirement().addList(BEARER_SCHEME));
        operation
                .getResponses()
                .addApiResponse(
                        "401", new ApiResponse().description("No bearer token, or one that resolves to no session"));
        return operation;
    }
}
