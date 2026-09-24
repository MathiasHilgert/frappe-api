package configuration

import (
	"errors"
	"fmt"
	"reflect"
	"strings"

	"github.com/go-playground/validator/v10"
)

// validate is the shared validator instance used by Validate.
var validate = validator.New(validator.WithRequiredStructEnabled())

// Validate checks configuration against its declared validation rules and
// returns a single aggregated error naming the environment variable and
// the violated rule for every field that fails, or nil if configuration is
// valid.
func Validate(configuration Configuration) error {
	err := validate.Struct(configuration)
	if err == nil {
		return nil
	}

	var validationErrors validator.ValidationErrors
	if !errors.As(err, &validationErrors) {
		return err
	}

	configurationType := reflect.TypeOf(configuration)

	messages := make([]string, 0, len(validationErrors))
	for _, fieldError := range validationErrors {
		envVar := envVarName(configurationType, namespacePath(fieldError.Namespace()))
		messages = append(messages, fmt.Sprintf("%s: failed validation %q (value: %q)", envVar, fieldError.Tag(), fieldError.Value()))
	}

	return fmt.Errorf("invalid configuration:\n  %s", strings.Join(messages, "\n  "))
}

// namespacePath splits a validator.FieldError Namespace (for example
// "Configuration.Application.Environment") into its field-name components
// after the root type name.
func namespacePath(namespace string) []string {
	parts := strings.Split(namespace, ".")
	if len(parts) <= 1 {
		return nil
	}
	return parts[1:]
}

// envVarName resolves the environment variable name that corresponds to
// the given struct-field path by walking structType's env and envPrefix
// tags, mirroring how github.com/caarlos0/env maps env vars onto fields.
func envVarName(structType reflect.Type, path []string) string {
	if len(path) == 0 || structType.Kind() != reflect.Struct {
		return strings.Join(path, ".")
	}

	field, found := structType.FieldByName(path[0])
	if !found {
		return strings.Join(path, ".")
	}

	if len(path) == 1 {
		envTag := field.Tag.Get("env")
		name, _, _ := strings.Cut(envTag, ",")
		if name == "" {
			return field.Name
		}
		return name
	}

	prefix := field.Tag.Get("envPrefix")
	return prefix + envVarName(field.Type, path[1:])
}
