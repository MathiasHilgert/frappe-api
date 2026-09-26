package rest_test

import (
	"encoding/json"
	"reflect"
	"testing"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

type owner struct {
	Object string `json:"object" enum:"owner"`
	ID     string `json:"id"`
}

type pet struct {
	Owner       rest.Expandable[owner]         `json:"owner"`
	Veterinary  rest.NullableExpandable[owner] `json:"veterinary"`
	Object      string                         `json:"object" enum:"pet"`
	Description string                         `json:"description"`
}

func TestExpandableSerializesTheIDOrTheResource(t *testing.T) {
	cases := []struct {
		name  string
		value pet
		want  string
	}{
		{
			name:  "ids by default, null when absent",
			value: pet{Object: "pet", Owner: rest.ExpandableID[owner]("owner_1")},
			want:  `{"owner":"owner_1","veterinary":null,"object":"pet","description":""}`,
		},
		{
			name: "expanded resources",
			value: pet{
				Object:     "pet",
				Owner:      rest.ExpandedResource("owner_1", owner{Object: "owner", ID: "owner_1"}),
				Veterinary: rest.NullableExpandableID[owner]("owner_2"),
			},
			want: `{"owner":{"object":"owner","id":"owner_1"},"veterinary":"owner_2","object":"pet","description":""}`,
		},
		{
			name: "nullable expanded",
			value: pet{
				Object:     "pet",
				Owner:      rest.ExpandableID[owner]("owner_1"),
				Veterinary: rest.NullableExpandedResource("owner_2", owner{Object: "owner", ID: "owner_2"}),
			},
			want: `{"owner":"owner_1","veterinary":{"object":"owner","id":"owner_2"},"object":"pet","description":""}`,
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			encoded, err := json.Marshal(testCase.value)
			if err != nil {
				t.Fatalf("marshal: %v", err)
			}
			if string(encoded) != testCase.want {
				t.Fatalf("json = %s, want %s", encoded, testCase.want)
			}
		})
	}
}

func TestExpandableIDReportsTheIDEvenWhenExpanded(t *testing.T) {
	expanded := rest.ExpandedResource("owner_1", owner{ID: "owner_1"})
	if expanded.ID != "owner_1" || expanded.Resource == nil {
		t.Fatalf("expanded = %+v", expanded)
	}
	if empty := (rest.NullableExpandable[owner]{}); !empty.IsNull() {
		t.Fatalf("zero NullableExpandable must be null")
	}
}

func TestExpandableSchemaIsOneOfTheIDAndTheResource(t *testing.T) {
	registry := huma.NewMapRegistry("#/components/schemas/", huma.DefaultSchemaNamer)
	schema := registry.Schema(reflect.TypeFor[pet](), false, "")

	ownerProperty := schema.Properties["owner"]
	if len(ownerProperty.OneOf) != 2 {
		t.Fatalf("owner oneOf = %+v, want the id string and the resource", ownerProperty.OneOf)
	}
	if ownerProperty.OneOf[0].Type != huma.TypeString || ownerProperty.OneOf[1].Ref != "#/components/schemas/Owner" {
		t.Fatalf("owner oneOf = %+v, %+v", ownerProperty.OneOf[0], ownerProperty.OneOf[1])
	}

	veterinaryProperty := schema.Properties["veterinary"]
	if len(veterinaryProperty.OneOf) != 3 || veterinaryProperty.OneOf[2].Type != "null" {
		t.Fatalf("veterinary oneOf = %+v, want id, resource and null", veterinaryProperty.OneOf)
	}
	if _, err := json.Marshal(schema); err != nil {
		t.Fatalf("marshal schema: %v", err)
	}
}
