package httpserver

import "testing"

func TestIfNoneMatchUsesWeakComparison(t *testing.T) {
	cases := []struct {
		name        string
		ifNoneMatch string
		etag        string
		want        bool
	}{
		{name: "empty header", ifNoneMatch: "", etag: `"a"`, want: false},
		{name: "exact", ifNoneMatch: `"a"`, etag: `"a"`, want: true},
		{name: "different", ifNoneMatch: `"b"`, etag: `"a"`, want: false},
		{name: "weak header strong tag", ifNoneMatch: `W/"a"`, etag: `"a"`, want: true},
		{name: "strong header weak tag", ifNoneMatch: `"a"`, etag: `W/"a"`, want: true},
		{name: "star", ifNoneMatch: "*", etag: `"a"`, want: true},
		{name: "star without representation", ifNoneMatch: "*", etag: "", want: false},
		{name: "list match", ifNoneMatch: `"x", W/"y" ,"a"`, etag: `"a"`, want: true},
		{name: "list miss", ifNoneMatch: `"x", "y"`, etag: `"a"`, want: false},
		{name: "comma inside tag", ifNoneMatch: `"a,b"`, etag: `"a,b"`, want: true},
		{name: "unquoted garbage", ifNoneMatch: `a`, etag: `"a"`, want: false},
		{name: "unterminated", ifNoneMatch: `"a`, etag: `"a"`, want: false},
		{name: "prefix only", ifNoneMatch: `"ab"`, etag: `"a"`, want: false},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := ifNoneMatchMatches(testCase.ifNoneMatch, testCase.etag); got != testCase.want {
				t.Fatalf("ifNoneMatchMatches(%q, %q) = %v, want %v", testCase.ifNoneMatch, testCase.etag, got, testCase.want)
			}
		})
	}
}

func TestNoStorePolicyDirective(t *testing.T) {
	if got := NoStore().CacheControl(); got != "no-store" {
		t.Fatalf("NoStore = %q", got)
	}
}
