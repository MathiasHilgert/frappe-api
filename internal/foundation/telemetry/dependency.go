package telemetry

import (
	"context"
	"errors"
	"log/slog"
	"os"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/contrib/instrumentation/host"
	runtimemetrics "go.opentelemetry.io/contrib/instrumentation/runtime"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploghttp"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetrichttp"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracehttp"
	otellog "go.opentelemetry.io/otel/log"
	otellogglobal "go.opentelemetry.io/otel/log/global"
	"go.opentelemetry.io/otel/propagation"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.30.0"
)

// DependencyName identifies the telemetry dependency for logging and
// error reporting when the composition root registers it as an
// application.Dependency[SDK]. This package stays free of the
// application package itself (foundation packages must not depend on
// each other); the composition root wires Up and Down into an
// application.Dependency.
const DependencyName = "telemetry"

// Settings is the subset of application-wide configuration the telemetry
// SDK needs to build its resource and decide whether to export.
type Settings struct {
	// ServiceName becomes the resource's service.name attribute.
	ServiceName string
	// ServiceVersion becomes the resource's service.version attribute.
	ServiceVersion string
	// DeploymentEnvironment becomes the resource's
	// deployment.environment.name attribute.
	DeploymentEnvironment string
	// LoggingLevel is the minimum severity the installed default logger
	// records, mirroring configuration.Logging.Level's validated values
	// ("debug", "info", "warn", "error"). It applies to both the console
	// and the OTLP log export, so LOGGING_LEVEL is honored the same way
	// whether or not telemetry export is enabled. An empty or
	// unrecognized value defaults to "info".
	LoggingLevel string
	// Enabled controls whether the telemetry SDK installs real,
	// SDK-backed global providers. When false, Up leaves the no-op global
	// providers in place.
	Enabled bool
}

// SDK holds the providers installed by Up, so Down can flush and shut
// them down again. A zero-value SDK, as produced when telemetry is
// disabled, has nothing to shut down.
type SDK struct {
	tracerProvider            *sdktrace.TracerProvider
	meterProvider             *sdkmetric.MeterProvider
	loggerProvider            *sdklog.LoggerProvider
	previousLogger            *slog.Logger
	previousLogGlobalProvider otellog.LoggerProvider
}

// startInstrumentation starts every OpenTelemetry contrib instrumentation
// package that observes through meterProvider. It is a package-level
// variable, rather than a direct call, so tests can substitute a failing
// implementation and prove Up cleans up correctly when a step after the
// providers are built fails. defaultStartInstrumentation is restored by
// every test that overrides it.
var startInstrumentation = defaultStartInstrumentation

// defaultStartInstrumentation starts Go runtime metrics and host
// (process) metrics collection against meterProvider.
func defaultStartInstrumentation(meterProvider *sdkmetric.MeterProvider) error {
	if err := runtimemetrics.Start(runtimemetrics.WithMeterProvider(meterProvider)); err != nil {
		return err
	}

	// Process metrics (process CPU time, process memory usage) from the
	// maintained go.opentelemetry.io/contrib/instrumentation/host package.
	// Its host-level system.* metrics are dropped by the view Up
	// configures on meterProvider. It has no open-file-descriptor metric,
	// so that one is not emitted.
	return host.Start(host.WithMeterProvider(meterProvider))
}

// Up builds the OpenTelemetry SDK and, only once every step below has
// succeeded, installs it as the global providers, ready to be wired as an
// application.Dependency[SDK]'s Up function. It builds a resource from
// settings, creates OTLP-over-HTTP exporters for traces, metrics and
// logs, builds the tracer, meter and logger providers, and starts Go
// runtime and host instrumentation against the meter provider. Only after
// all of that succeeds does it install the tracer, meter and log
// providers as the OpenTelemetry globals (through otel.SetTracerProvider,
// otel.SetMeterProvider and go.opentelemetry.io/otel/log/global's
// SetLoggerProvider, so both the OTel Logs API and the bridged slog
// default logger are covered), together with a tracecontext-and-baggage
// propagator and a fan-out default slog logger that writes every record
// both to the console (stdout, as JSON) and into the OTLP logging
// pipeline so log records carry trace_id. Both destinations are gated by
// the same settings.LoggingLevel, so console output is never silenced and
// never lost if the collector is unreachable.
//
// If any step after the providers are built fails, Up shuts down every
// provider it already built (joining any shutdown errors with the
// original failure through errors.Join) and returns the zero SDK: no
// provider is left reachable through the OTel globals or slog.Default,
// and nothing orphaned needs a later Down call.
//
// When settings.Enabled is false, Up does nothing and the global
// providers stay the no-op implementations OpenTelemetry installs by
// default.
func Up(ctx context.Context, settings Settings) (SDK, error) {
	if !settings.Enabled {
		return SDK{}, nil
	}

	detectedResource, err := buildResource(settings)
	if err != nil {
		return SDK{}, err
	}

	traceExporter, err := otlptracehttp.New(ctx)
	if err != nil {
		return SDK{}, err
	}

	metricExporter, err := otlpmetrichttp.New(ctx)
	if err != nil {
		return SDK{}, err
	}

	logExporter, err := otlploghttp.New(ctx)
	if err != nil {
		return SDK{}, err
	}

	tracerProvider := sdktrace.NewTracerProvider(
		sdktrace.WithResource(detectedResource),
		sdktrace.WithBatcher(traceExporter),
	)

	// No WithExemplarFilter option is passed: the SDK's default is
	// exemplar.TraceBasedFilter (go.opentelemetry.io/otel/sdk/metric's
	// config.go), which only offers a measurement as an exemplar when it
	// was recorded inside a sampled span. That is exactly what carries
	// trace_id onto histogram data points, letting a metric spike be
	// linked back to the trace that produced it.
	meterProvider := sdkmetric.NewMeterProvider(
		sdkmetric.WithResource(detectedResource),
		sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExporter)),
		// Drop host-level metrics (system.*) emitted by the host
		// instrumentation: in containers they describe the node, duplicate
		// infrastructure monitoring and add billable series. Only process.*
		// metrics are kept.
		sdkmetric.WithView(sdkmetric.NewView(
			sdkmetric.Instrument{Name: "system.*"},
			sdkmetric.Stream{Aggregation: sdkmetric.AggregationDrop{}},
		)),
	)

	loggerProvider := sdklog.NewLoggerProvider(
		sdklog.WithResource(detectedResource),
		sdklog.WithProcessor(sdklog.NewBatchProcessor(logExporter)),
	)

	if err := startInstrumentation(meterProvider); err != nil {
		return SDK{}, shutdownPartial(ctx, tracerProvider, meterProvider, loggerProvider, err)
	}

	// Every step above succeeded: only now is anything installed as a
	// global, so a failure never leaves an orphaned provider behind one of
	// the OTel globals or slog.Default.
	previousLogGlobalProvider := otellogglobal.GetLoggerProvider()
	previousLogger := slog.Default()

	otel.SetTracerProvider(tracerProvider)
	otel.SetMeterProvider(meterProvider)
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(
		propagation.TraceContext{},
		propagation.Baggage{},
	))
	otellogglobal.SetLoggerProvider(loggerProvider)

	level := parseLoggingLevel(settings.LoggingLevel)

	consoleHandler := slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelDebug})
	otelHandler := otelslog.NewHandler(
		settings.ServiceName,
		otelslog.WithLoggerProvider(loggerProvider),
	)
	fanoutLogger := slog.New(newFanoutHandler(level, consoleHandler, otelHandler))
	slog.SetDefault(fanoutLogger)

	return SDK{
		tracerProvider:            tracerProvider,
		meterProvider:             meterProvider,
		loggerProvider:            loggerProvider,
		previousLogger:            previousLogger,
		previousLogGlobalProvider: previousLogGlobalProvider,
	}, nil
}

// shutdownPartial shuts down every provider Up already built when a later
// step fails, before any of them was installed as a global. It joins
// every shutdown error with cause and returns that joined error.
func shutdownPartial(ctx context.Context, tracerProvider *sdktrace.TracerProvider, meterProvider *sdkmetric.MeterProvider, loggerProvider *sdklog.LoggerProvider, cause error) error {
	errs := []error{cause}
	if tracerProvider != nil {
		errs = append(errs, tracerProvider.Shutdown(ctx))
	}
	if meterProvider != nil {
		errs = append(errs, meterProvider.Shutdown(ctx))
	}
	if loggerProvider != nil {
		errs = append(errs, loggerProvider.Shutdown(ctx))
	}
	return errors.Join(errs...)
}

// Down flushes and shuts down every provider started by Up, ready to be
// wired as an application.Dependency[SDK]'s Down function. It joins any
// errors encountered, and is a no-op when value is the zero SDK, which is
// what Up returns when telemetry is disabled.
func Down(ctx context.Context, value SDK) error {
	var errs []error

	if value.tracerProvider != nil {
		errs = append(errs, value.tracerProvider.ForceFlush(ctx))
		errs = append(errs, value.tracerProvider.Shutdown(ctx))
	}
	if value.meterProvider != nil {
		errs = append(errs, value.meterProvider.ForceFlush(ctx))
		errs = append(errs, value.meterProvider.Shutdown(ctx))
	}
	if value.loggerProvider != nil {
		errs = append(errs, value.loggerProvider.ForceFlush(ctx))
		errs = append(errs, value.loggerProvider.Shutdown(ctx))
	}

	if value.previousLogger != nil {
		slog.SetDefault(value.previousLogger)
	}

	if value.previousLogGlobalProvider != nil {
		otellogglobal.SetLoggerProvider(value.previousLogGlobalProvider)
	}

	return errors.Join(errs...)
}

// buildResource builds the OpenTelemetry resource shared by every
// provider Up installs, merging the process/runtime attributes
// resource.Default() detects with this service's identity: its name,
// version (settings.ServiceVersion, always build.Version in production —
// see internal/dependencies.telemetrySettingsFrom) and deployment
// environment.
func buildResource(settings Settings) (*resource.Resource, error) {
	return resource.Merge(
		resource.Default(),
		resource.NewSchemaless(
			semconv.ServiceName(settings.ServiceName),
			semconv.ServiceVersion(settings.ServiceVersion),
			semconv.DeploymentEnvironmentName(settings.DeploymentEnvironment),
		),
	)
}
