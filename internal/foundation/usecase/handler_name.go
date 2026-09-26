package usecase

import (
	"path"
	"reflect"
	"strings"
	"unicode"
)

// handlerName derives a handler's telemetry name from its Go type:
// "<module>.<kind>.<snake_case type without Handler>", for example
// GetCountryHandler in internal/modules/geo/application/query is
// "geo.query.get_country". A type outside a module's application layer is
// "<package>.<snake_case type>".
type handlerName struct{}

// modulesSegment precedes the module name in a module package path.
const modulesSegment = "/internal/modules/"

// ofValue names the dynamic type of handler (a pointer names its element).
func (name handlerName) ofValue(handler any) string {
	handlerType := reflect.TypeOf(handler)
	for handlerType != nil && handlerType.Kind() == reflect.Pointer {
		handlerType = handlerType.Elem()
	}
	if handlerType == nil {
		return "unknown"
	}
	return name.of(handlerType.PkgPath(), handlerType.Name())
}

func (name handlerName) of(packagePath, typeName string) string {
	if bracket := strings.IndexByte(typeName, '['); bracket >= 0 {
		typeName = typeName[:bracket]
	}
	operation := name.snakeCase(strings.TrimSuffix(typeName, "Handler"))
	prefix := path.Base(packagePath)
	if _, modulePath, found := strings.Cut(packagePath, modulesSegment); found {
		module, _, _ := strings.Cut(modulePath, "/")
		prefix = module + "." + prefix
	}
	return prefix + "." + operation
}

// snakeCase turns a Go identifier into snake_case, keeping acronyms
// together: GetHTTPStatus is get_http_status.
func (handlerName) snakeCase(identifier string) string {
	runes := []rune(identifier)
	var builder strings.Builder
	for index, character := range runes {
		if index > 0 && unicode.IsUpper(character) {
			previousLower := unicode.IsLower(runes[index-1])
			nextLower := index+1 < len(runes) && unicode.IsLower(runes[index+1])
			if previousLower || (unicode.IsUpper(runes[index-1]) && nextLower) {
				builder.WriteByte('_')
			}
		}
		builder.WriteRune(unicode.ToLower(character))
	}
	return builder.String()
}
