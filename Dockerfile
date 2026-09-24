# syntax=docker/dockerfile:1

ARG GO_VERSION=1.27.1

# Build stage: compile a static binary.
FROM --platform=$BUILDPLATFORM golang:${GO_VERSION}-alpine AS builder
WORKDIR /src

# Download modules first so this layer is cached until go.mod/go.sum change.
# go.sum is optional until the first dependency is added.
COPY go.mod go.sum* ./
RUN --mount=type=cache,target=/go/pkg/mod \
    go mod download

COPY . .

# Path of the main package to build.
ARG MAIN_PKG=./cmd/api
ARG TARGETOS
ARG TARGETARCH

# Build-time identity stamped into internal/foundation/build via -X, read
# back by telemetry's resource and the frappe.application.info gauge.
ARG VERSION=development
ARG COMMIT=unknown
ARG BUILD_PKG=github.com/MathiasHilgert/frappe-api/internal/foundation/build

RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    CGO_ENABLED=0 GOOS=$TARGETOS GOARCH=$TARGETARCH \
    go build -trimpath \
      -ldflags="-s -w -X ${BUILD_PKG}.Version=${VERSION} -X ${BUILD_PKG}.Commit=${COMMIT}" \
      -o /out/app "$MAIN_PKG"

# cmd/migrate is built into the same image, as a second small binary,
# rather than as a separate image target: it shares every build layer
# above (module download, source, toolchain) with cmd/api, so building it
# too costs one more `go build` and a couple of extra megabytes, not a
# second image to version and push. It is a separate process from cmd/api
# regardless of which image ships it (see cmd/migrate/main.go for why
# migrations must not run at API startup).
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    CGO_ENABLED=0 GOOS=$TARGETOS GOARCH=$TARGETARCH \
    go build -trimpath -ldflags="-s -w" -o /out/migrate ./cmd/migrate

# Runtime stage: distroless instead of scratch because it ships CA certs,
# tzdata and a nonroot user without extra steps.
FROM gcr.io/distroless/static-debian12:nonroot

LABEL org.opencontainers.image.source="https://github.com/MathiasHilgert/frappe-api" \
      org.opencontainers.image.licenses="Proprietary" \
      org.opencontainers.image.vendor="Nulled Software"

COPY --from=builder /out/app /app
COPY --from=builder /out/migrate /migrate

USER nonroot:nonroot
EXPOSE 8080
ENTRYPOINT ["/app"]
