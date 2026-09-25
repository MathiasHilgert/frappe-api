// Package localizedtext stores user-entered, translatable strings (a dish
// description, a menu section name) once, platform-wide, instead of one
// translation table per entity.
//
// # Model
//
// An entity column "<field>_text_id uuid REFERENCES localized_texts (id)"
// points at a localized text: a source value in a source locale, its
// SHA-256 source hash (see Hash) and an optional machine translation
// Context. Each translation stores its locale, value, origin (manual or
// machine), status (current, stale or pending) and the source hash it
// was made for:
//
//   - Changing the source (UpdateSource) marks every translation of the
//     old source stale. Manual translations stay manual and are only
//     flagged; machine ones are requested again.
//   - A machine translation never overwrites a manual one, and is
//     discarded when the source changed since it was requested.
//   - Reading (Localize, LocalizeMany) resolves the requested locale
//     through the catalog (es-MX serves es-419) and falls back to the
//     source when no usable translation exists. Stale translations are
//     still served, flagged with their Status.
//
// # Tenancy
//
// The Postgres store runs in the caller's transaction; Row Level
// Security scopes every statement to its application.tenant setting,
// which also stamps new texts. Global texts (tenant_id NULL) are
// readable by every tenant and maintained by migrations only.
//
// # Translation needed hook
//
// TranslationRequester is called, in the caller's transaction, whenever
// a text needs a machine translation. Requests are first recorded as
// pending rows, so each one is requested once. The machine translation
// feature implements it (outbox, then an asynchronous translator); until
// then no requester is wired and nothing is requested.
//
// # Usage from a module
//
// Declare the field once, at wiring time, with its default context:
//
//	var dishDescription = localizedtext.Field{
//		Table:   "dishes",
//		Column:  "description_text_id",
//		Context: "Description of a dish on a restaurant menu",
//	}
//
//	descriptions, err := dependencies.LocalizedTexts.Field(dishDescription)
//
// In the use case, inside the module's unit of work (its Atomic port):
//
//	err := atomic.Run(ctx, func(ctx context.Context) error {
//		textID, err := descriptions.Create(ctx, localizedtext.Source{Value: command.Description})
//		if err != nil {
//			return err
//		}
//		dish := domain.NewDish(command.Name, uuid.UUID(textID))
//		return dishes.Save(ctx, dish)
//	})
//
// Listing dishes without N+1 queries:
//
//	ids := make([]localizedtext.ID, 0, len(page))
//	for _, dish := range page {
//		ids = append(ids, localizedtext.ID(dish.DescriptionTextID))
//	}
//	locale, _ := i18n.FromContext(ctx)
//	localized, err := descriptions.LocalizeMany(ctx, ids, locale)
//	// localized[id].Value, .Locale, .Fallback, .Origin, .Status
//
// Deleting a dish deletes its text in the same transaction:
//
//	if err := dishes.Delete(ctx, dish.ID); err != nil {
//		return err
//	}
//	return descriptions.Delete(ctx, localizedtext.ID(dish.DescriptionTextID))
//
// Service.DeleteOrphans is the periodic garbage collection for texts no
// declared field references (a scheduler will call it per tenant).
package localizedtext
