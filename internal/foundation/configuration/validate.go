package configuration

import (
	"errors"
	"reflect"
	"strings"

	"github.com/go-playground/validator/v10"
)

// validate is the shared validator instance used by Validate.
var validate = validator.New(validator.WithRequiredStructEnabled())

// Violation describes a single environment variable that failed a single
// validation rule.
type Violation struct {
	// Variable is the environment variable name that failed validation.
	Variable string
	// Rule is the name of the violated validation rule.
	Rule string
}

// ValidationError aggregates every Violation found while validating a
// Configuration. It never includes the offending value, so it is safe to
// log or return to a caller even when the value is sensitive.
type ValidationError struct {
	// Violations holds one entry per failed validation rule.
	Violations []Violation
}

// Error implements the error interface, listing every violation without
// exposing the values that failed validation.
func (validationError *ValidationError) Error() string {
	messages := make([]string, 0, len(validationError.Violations))
	for _, violation := range validationError.Violations {
		messages = append(messages, violation.Variable+`: failed validation "`+violation.Rule+`"`)
	}
	return "invalid configuration:\n  " + strings.Join(messages, "\n  ")
}

// Validate checks configuration against its declared validation rules and
// returns a *ValidationError naming the environment variable and the
// violated rule for every field that fails, or nil if configuration is
// valid. The returned error never includes the offending value.
func Validate(configuration Configuration) error {
	validationError := ValidateStruct(configuration)

	var crossFieldViolations []Violation
	if minimumHookTimeout := configuration.HTTP.ShutdownDrainDelay + configuration.HTTP.ShutdownTimeout; configuration.Application.HookTimeout < minimumHookTimeout {
		crossFieldViolations = append(crossFieldViolations, Violation{
			Variable: "APPLICATION_HOOK_TIMEOUT",
			Rule:     "gte_http_shutdown_drain_delay_plus_shutdown_timeout",
		})
	}
	crossFieldViolations = append(crossFieldViolations, validateCORS(configuration.HTTP)...)
	crossFieldViolations = append(crossFieldViolations, validateRateLimit(configuration.RateLimit, configuration.Valkey)...)
	crossFieldViolations = append(crossFieldViolations, validateOutbox(configuration.Outbox, configuration.Database, configuration.Events)...)
	crossFieldViolations = append(crossFieldViolations, validateEvents(configuration.Events, configuration.NATS)...)
	crossFieldViolations = append(crossFieldViolations, validateInbox(configuration.Inbox, configuration.Events)...)

	if len(crossFieldViolations) > 0 {
		if validationError == nil {
			validationError = &ValidationError{}
		}
		validationError.Violations = append(validationError.Violations, crossFieldViolations...)
	}

	if validationError != nil {
		return validationError
	}
	return nil
}

// ValidateStruct runs the same validation and env/envPrefix-tag variable
// resolution as Validate, but against any tagged struct value rather than
// only Configuration. It exists so the variable-resolution behavior of
// resolveVariableName (pointer struct fields, prefix-less nested structs,
// and slices of structs) can be exercised directly with test fixtures.
func ValidateStruct(value any) *ValidationError {
	err := validate.Struct(value)
	if err == nil {
		return nil
	}

	var validationErrors validator.ValidationErrors
	if !errors.As(err, &validationErrors) {
		return &ValidationError{Violations: []Violation{{Variable: "unknown", Rule: err.Error()}}}
	}

	valueType := reflect.TypeOf(value)

	violations := make([]Violation, 0, len(validationErrors))
	for _, fieldError := range validationErrors {
		variable := resolveVariableName(valueType, namespacePath(fieldError.Namespace()))
		violations = append(violations, Violation{Variable: variable, Rule: fieldError.Tag()})
	}

	return &ValidationError{Violations: violations}
}

// namespacePath splits a validator.FieldError Namespace (for example
// "Configuration.Application.Environment") into its field-name components
// after the root type name, stripping any slice-index segment such as
// "Servers[0]" down to "Servers".
func namespacePath(namespace string) []string {
	parts := strings.Split(namespace, ".")
	if len(parts) <= 1 {
		return nil
	}
	return stripIndexSegments(parts[1:])
}

// stripIndexSegments removes a trailing "[index]" suffix from every path
// segment, so a slice element such as "Servers[0]" resolves against the
// slice's own struct field "Servers".
func stripIndexSegments(segments []string) []string {
	stripped := make([]string, 0, len(segments))
	for _, segment := range segments {
		if bracket := strings.IndexByte(segment, '['); bracket != -1 {
			segment = segment[:bracket]
		}
		stripped = append(stripped, segment)
	}
	return stripped
}

// resolveVariableName resolves the environment variable name that
// corresponds to the given struct-field path by walking structType's env
// and envPrefix tags, mirroring how github.com/caarlos0/env maps env vars
// onto fields. It dereferences pointer struct fields and slice element
// types encountered along the path.
func resolveVariableName(structType reflect.Type, path []string) string {
	structType = dereference(structType)
	if len(path) == 0 || structType.Kind() != reflect.Struct {
		return strings.Join(path, ".")
	}

	field, found := structType.FieldByName(path[0])
	if !found {
		return strings.Join(path, ".")
	}

	if len(path) == 1 {
		variableTag := field.Tag.Get("env")
		name, _, _ := strings.Cut(variableTag, ",")
		if name == "" {
			return field.Name
		}
		return name
	}

	prefix := field.Tag.Get("envPrefix")
	return prefix + resolveVariableName(elementType(field.Type), path[1:])
}

// dereference unwraps a pointer type down to the type it points to,
// leaving any other type unchanged.
func dereference(fieldType reflect.Type) reflect.Type {
	if fieldType.Kind() == reflect.Pointer {
		return fieldType.Elem()
	}
	return fieldType
}

// elementType unwraps a slice type down to its (dereferenced) element
// type, leaving any other type unchanged after dereferencing.
func elementType(fieldType reflect.Type) reflect.Type {
	fieldType = dereference(fieldType)
	if fieldType.Kind() == reflect.Slice {
		return dereference(fieldType.Elem())
	}
	return fieldType
}
