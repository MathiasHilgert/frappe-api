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
	tracerProvider *sdktrace.TracerProvider
	meterProvider  *sdkmetric.MeterProvider
	loggerProvider *sdklog.LoggerProvider
	previousLogger *slog.Logger
}

// Up builds and installs the OpenTelemetry SDK as global providers,
// ready to be wired as an application.Dependency[SDK]'s Up function. It
// builds a resource from settings, creates OTLP-over-HTTP exporters for
// traces, metrics and logs, installs them as the global tracer, meter
// and logger providers together with a tracecontext-and-baggage
// propagator, starts Go runtime metric collection, and installs a
// fan-out default slog logger that writes every record both to the
// console (stdout, as JSON) and into the OTLP logging pipeline so log
// records carry trace_id. Both destinations are gated by the same
// settings.LoggingLevel, so console output is never silenced and never
// lost if the collector is unreachable.
//
// When settings.Enabled is false, Up does nothing and the global
// providers stay the no-op implementations OpenTelemetry installs by
// default.
func Up(ctx context.Context, settings Settings) (SDK, error) {
	if !settings.Enabled {
		return SDK{}, nil
	}

	detectedResource, err := resource.Merge(
		resource.Default(),
		resource.NewSchemaless(
			semconv.ServiceName(settings.ServiceName),
			semconv.ServiceVersion(settings.ServiceVersion),
			semconv.DeploymentEnvironmentName(settings.DeploymentEnvironment),
		),
	)
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

	otel.SetTracerProvider(tracerProvider)
	otel.SetMeterProvider(meterProvider)
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(
		propagation.TraceContext{},
		propagation.Baggage{},
	))

	if err := runtimemetrics.Start(runtimemetrics.WithMeterProvider(meterProvider)); err != nil {
		return SDK{}, err
	}

	// Process metrics (process CPU time, process memory usage) from the
	// maintained go.opentelemetry.io/contrib/instrumentation/host package.
	// Its host-level system.* metrics are dropped by the view above. It has
	// no open-file-descriptor metric, so that one is not emitted.
	if err := host.Start(host.WithMeterProvider(meterProvider)); err != nil {
		return SDK{}, err
	}

	level := parseLoggingLevel(settings.LoggingLevel)

	consoleHandler := slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelDebug})
	otelHandler := otelslog.NewHandler(
		settings.ServiceName,
		otelslog.WithLoggerProvider(loggerProvider),
	)
	fanoutLogger := slog.New(newFanoutHandler(level, consoleHandler, otelHandler))

	previousLogger := slog.Default()
	slog.SetDefault(fanoutLogger)

	return SDK{
		tracerProvider: tracerProvider,
		meterProvider:  meterProvider,
		loggerProvider: loggerProvider,
		previousLogger: previousLogger,
	}, nil
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

	return errors.Join(errs...)
}
