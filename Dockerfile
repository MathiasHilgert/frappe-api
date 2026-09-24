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
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    CGO_ENABLED=0 GOOS=$TARGETOS GOARCH=$TARGETARCH \
    go build -trimpath -ldflags="-s -w" -o /out/app "$MAIN_PKG"

# Runtime stage: distroless instead of scratch because it ships CA certs,
# tzdata and a nonroot user without extra steps.
FROM gcr.io/distroless/static-debian12:nonroot

LABEL org.opencontainers.image.source="https://github.com/MathiasHilgert/frappe-api" \
      org.opencontainers.image.licenses="Proprietary" \
      org.opencontainers.image.vendor="Nulled Software"

COPY --from=builder /out/app /app

USER nonroot:nonroot
EXPOSE 8080
ENTRYPOINT ["/app"]
