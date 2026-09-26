package rest

import (
	"encoding/json"
	"reflect"

	"github.com/danielgtaylor/huma/v2"
)

// Expandable is a related resource property (see Expansions): the related
// resource's id by default, or the resource itself when the client asked
// for it with expand[]. Clients tell them apart by the resource's
// "object" property. In OpenAPI it is oneOf the id string and T's schema.
//
//	Country rest.Expandable[Country] `json:"country" doc:"Country id, or the country when expanded."`
//
// Use NullableExpandable for a relation that may be absent.
//
//nolint:govet // fieldalignment: ID first reads naturally; the struct is never stored in bulk.
type Expandable[T any] struct {
	// ID is the related resource's id, always set.
	ID string
	// Resource is the expanded resource, or nil when not expanded.
	Resource *T
}

// ExpandableID returns the unexpanded relation to id.
func ExpandableID[T any](id string) Expandable[T] {
	return Expandable[T]{ID: id}
}

// ExpandedResource returns the relation to id, expanded into resource.
func ExpandedResource[T any](id string, resource T) Expandable[T] {
	return Expandable[T]{ID: id, Resource: &resource}
}

// MarshalJSON writes the resource when expanded, else the id string.
func (expandable Expandable[T]) MarshalJSON() ([]byte, error) {
	if expandable.Resource != nil {
		return json.Marshal(expandable.Resource)
	}
	return json.Marshal(expandable.ID)
}

// Schema declares the property as oneOf the id string and T's schema.
func (Expandable[T]) Schema(registry huma.Registry) *huma.Schema {
	return &huma.Schema{OneOf: []*huma.Schema{
		{Type: huma.TypeString, Description: "Id of the related resource."},
		registry.Schema(reflect.TypeFor[T](), true, ""),
	}}
}

// NullableExpandable is an Expandable relation that may be absent: null,
// the related resource's id, or the expanded resource. The zero value is
// null.
type NullableExpandable[T any] struct {
	relation Expandable[T]
	present  bool
}

// NullableExpandableID returns the unexpanded relation to id.
func NullableExpandableID[T any](id string) NullableExpandable[T] {
	return NullableExpandable[T]{relation: ExpandableID[T](id), present: true}
}

// NullableExpandedResource returns the relation to id, expanded into
// resource.
func NullableExpandedResource[T any](id string, resource T) NullableExpandable[T] {
	return NullableExpandable[T]{relation: ExpandedResource(id, resource), present: true}
}

// IsNull reports whether the relation is absent.
func (expandable NullableExpandable[T]) IsNull() bool {
	return !expandable.present
}

// MarshalJSON writes null when absent, else like Expandable.
func (expandable NullableExpandable[T]) MarshalJSON() ([]byte, error) {
	if !expandable.present {
		return []byte("null"), nil
	}
	return expandable.relation.MarshalJSON()
}

// Schema declares the property as oneOf the id string, T's schema and
// null.
func (NullableExpandable[T]) Schema(registry huma.Registry) *huma.Schema {
	schema := Expandable[T]{}.Schema(registry)
	schema.OneOf = append(schema.OneOf, &huma.Schema{Type: "null"})
	return schema
}
