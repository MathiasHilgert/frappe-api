package main

import "slices"

// localeLanguages maps every platform locale (canonical BCP 47, as stored in
// the locales table) to the GeoNames alternate name languages that may
// supply its name, most specific first. GeoNames tags names with ISO 639
// codes, optionally followed by a country or script variant ("pt-BR",
// "zh-Hans"); generic Spanish serves es-419, for instance.
var localeLanguages = []localeMapping{ //nolint:gochecknoglobals // constant lookup table.
	{locale: "es-419", languages: []string{"es-419", "es"}},
	{locale: "en", languages: []string{"en"}},
	{locale: "pt-BR", languages: []string{"pt-BR", "pt"}},
	{locale: "fr", languages: []string{"fr"}},
	{locale: "it", languages: []string{"it"}},
	{locale: "de", languages: []string{"de"}},
	{locale: "ru", languages: []string{"ru"}},
	{locale: "zh-Hans", languages: []string{"zh-Hans", "zh-CN", "zh"}},
	{locale: "ko", languages: []string{"ko"}},
	{locale: "ja", languages: []string{"ja"}},
}

type localeMapping struct {
	locale    string
	languages []string
}

// languageTarget is one locale a GeoNames language may supply, with the
// language's rank among that locale's languages (0 is most specific).
type languageTarget struct {
	locale string
	rank   int
}

// languageTargets indexes localeLanguages by GeoNames language.
func languageTargets() map[string][]languageTarget {
	targets := map[string][]languageTarget{}
	for _, mapping := range localeLanguages {
		for rank, language := range mapping.languages {
			targets[language] = append(targets[language], languageTarget{locale: mapping.locale, rank: rank})
		}
	}
	return targets
}

type nameKey struct {
	locale     string
	geonamesID int64
}

type nameCandidate struct {
	name alternateName
	rank int
}

// better reports whether candidate beats current: a more specific
// language first, then an official (preferred) name, then a short name,
// then the lowest alternate name identifier, so the pick is deterministic.
func (candidate nameCandidate) better(current nameCandidate) bool {
	if candidate.rank != current.rank {
		return candidate.rank < current.rank
	}
	if candidate.name.preferred != current.name.preferred {
		return candidate.name.preferred
	}
	if candidate.name.short != current.name.short {
		return candidate.name.short
	}
	return candidate.name.identifier < current.name.identifier
}

// nameSelector keeps the best alternate name per entity and locale.
type nameSelector struct {
	targets map[string][]languageTarget
	best    map[nameKey]nameCandidate
}

func newNameSelector() *nameSelector {
	return &nameSelector{targets: languageTargets(), best: map[nameKey]nameCandidate{}}
}

// consider offers one alternate name. Historic, colloquial and ended names
// (a non-empty "to" period) are never picked.
func (selector *nameSelector) consider(name alternateName) {
	if name.historic || name.colloquial || name.endedAt != "" || name.name == "" {
		return
	}
	for _, target := range selector.targets[name.language] {
		key := nameKey{geonamesID: name.geonamesID, locale: target.locale}
		candidate := nameCandidate{name: name, rank: target.rank}
		if current, found := selector.best[key]; !found || candidate.better(current) {
			selector.best[key] = candidate
		}
	}
}

// localizedName is one picked name, ready for a *_names table.
type localizedName struct {
	locale     string
	name       string
	geonamesID int64
}

// names returns the picked name of every entity in originalNames, sorted
// by entity then locale order, leaving out names equal to the entity's
// original name: readers fall back to the original name anyway.
func (selector *nameSelector) names(originalNames map[int64]string) []localizedName {
	var picked []localizedName
	for key, candidate := range selector.best {
		original, found := originalNames[key.geonamesID]
		if !found || candidate.name.name == original {
			continue
		}
		picked = append(picked, localizedName{geonamesID: key.geonamesID, locale: key.locale, name: candidate.name.name})
	}
	slices.SortFunc(picked, func(left, right localizedName) int {
		if left.geonamesID != right.geonamesID {
			if left.geonamesID < right.geonamesID {
				return -1
			}
			return 1
		}
		return localeOrder(left.locale) - localeOrder(right.locale)
	})
	return picked
}

func localeOrder(locale string) int {
	return slices.IndexFunc(localeLanguages, func(mapping localeMapping) bool { return mapping.locale == locale })
}
