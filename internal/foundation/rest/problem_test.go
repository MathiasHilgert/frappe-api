package rest_test

import (
	"context"
	"errors"
	"net/http"
	"testing"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

// localized returns a context carrying locale and the embedded catalogs,
// as the /v1 localization middleware does.
func localized(t *testing.T, locale string) context.Context {
	t.Helper()
	catalog, err := i18n.NewCatalog(i18n.Settings{
		Messages:  i18n.EmbeddedMessages,
		Source:    i18n.MustParseLocale("es-419"),
		Supported: []i18n.Locale{i18n.MustParseLocale("es-419"), i18n.MustParseLocale("en"), i18n.MustParseLocale("ja")},
	})
	if err != nil {
		t.Fatalf("catalog: %v", err)
	}
	return catalog.WithLocale(context.Background(), i18n.MustParseLocale(locale))
}

func problemOf(t *testing.T, err error) *huma.ErrorModel {
	t.Helper()
	var model *huma.ErrorModel
	if !errors.As(err, &model) {
		t.Fatalf("error %v is not a problem", err)
	}
	return model
}

func TestProblemTranslatesDetailAndMessagesInTheRequestLocale(t *testing.T) {
	err := rest.Problem(localized(t, "en"), http.StatusUnprocessableEntity,
		rest.Text{Key: "problem.expand.too_many.detail", Data: i18n.Data{"Maximum": 20}},
		rest.Detail(localized(t, "en"), "query.expand[]", rest.Text{Key: "problem.expand.too_many.message"}, nil))
	model := problemOf(t, err)
	if model.Status != http.StatusUnprocessableEntity || model.Detail != "At most 20 expand[] values are allowed." {
		t.Fatalf("problem = %d %q", model.Status, model.Detail)
	}
	if len(model.Errors) != 1 || model.Errors[0].Message != "too many expansions" || model.Errors[0].Location != "query.expand[]" {
		t.Fatalf("errors = %+v", model.Errors)
	}
}

func TestProblemFallsBackToTheDefaultTextWithoutALocale(t *testing.T) {
	model := problemOf(t, rest.Problem(context.Background(), http.StatusNotFound,
		rest.Text{Key: "missing.key", Default: "No dish with id 7."}))
	if model.Detail != "No dish with id 7." {
		t.Fatalf("detail = %q", model.Detail)
	}
}

func TestCursorAndExpansionProblemsAreLocalized(t *testing.T) {
	ctx := localized(t, "ja")
	parameters := rest.PageParameters{Cursor: "garbage"}
	_, err := parameters.Position(ctx, newCodec(t), &position{})
	cursorProblem := problemOf(t, err)
	if cursorProblem.Status != http.StatusBadRequest || cursorProblem.Detail != "ページネーションカーソルが無効か、別の一覧のものです。最初のページからやり直してください。" ||
		cursorProblem.Detail == "The pagination cursor is invalid or belongs to another listing. Restart from the first page." {
		t.Fatalf("cursor problem = %d %q, want a Japanese detail", cursorProblem.Status, cursorProblem.Detail)
	}
	if cursorProblem.Errors[0].Location != "query.cursor" {
		t.Fatalf("cursor errors = %+v", cursorProblem.Errors)
	}

	_, err = rest.NewExpansions("country").Parse(ctx, []string{"owner", "Bad"})
	expandProblem := problemOf(t, err)
	if len(expandProblem.Errors) != 2 || expandProblem.Detail != "1つ以上の expand[] の値が無効です。" || expandProblem.Errors[0].Message != "この操作では展開できません" {
		t.Fatalf("expand problem = %q %+v, want Japanese texts", expandProblem.Detail, expandProblem.Errors)
	}
}

func TestNewCursorPageTakesAnExplicitNextPosition(t *testing.T) {
	codec := newCodec(t)
	last, err := rest.NewCursorPage(codec, rest.PageParameters{Limit: 2}, []dish{{Object: "dish", ID: "dish_a"}}, nil)
	if err != nil || last.Body.HasMore || last.Body.NextCursor != nil || last.Link != "" {
		t.Fatalf("last page = %+v, %v", last, err)
	}
	more, err := rest.NewCursorPage(codec, rest.PageParameters{Limit: 2}, []dish{{Object: "dish", ID: "dish_a"}}, position{ID: "dish_z"})
	if err != nil || !more.Body.HasMore || more.Body.NextCursor == nil || more.Link == "" {
		t.Fatalf("page with more = %+v, %v", more, err)
	}
	var decoded position
	if err := codec.Decode(*more.Body.NextCursor, "principal=||", &decoded); err != nil || decoded.ID != "dish_z" {
		t.Fatalf("next cursor position = %+v, %v", decoded, err)
	}
}
