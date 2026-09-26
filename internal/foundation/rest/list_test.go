package rest_test

import (
	"encoding/json"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

type dish struct {
	Object string `json:"object"`
	ID     string `json:"id"`
}

func TestNewListSerializesTheEnvelope(t *testing.T) {
	cases := []struct {
		name string
		list rest.List[dish]
		want string
	}{
		{
			name: "last page",
			list: rest.NewList("/v1/dishes", []dish{{Object: "dish", ID: "dish_1"}}, ""),
			want: `{"object":"list","url":"/v1/dishes","data":[{"object":"dish","id":"dish_1"}],"has_more":false,"next_cursor":null}`,
		},
		{
			name: "more pages",
			list: rest.NewList("/v1/dishes", []dish{{Object: "dish", ID: "dish_1"}}, "abc"),
			want: `{"object":"list","url":"/v1/dishes","data":[{"object":"dish","id":"dish_1"}],"has_more":true,"next_cursor":"abc"}`,
		},
		{
			name: "empty is an array, never null",
			list: rest.NewList[dish]("/v1/dishes", nil, ""),
			want: `{"object":"list","url":"/v1/dishes","data":[],"has_more":false,"next_cursor":null}`,
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			encoded, err := json.Marshal(testCase.list)
			if err != nil {
				t.Fatal(err)
			}
			if string(encoded) != testCase.want {
				t.Fatalf("got %s\nwant %s", encoded, testCase.want)
			}
		})
	}
}
