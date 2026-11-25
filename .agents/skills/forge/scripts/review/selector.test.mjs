import { readFileSync } from 'node:fs'
// Run: node .agents/skills/forge/scripts/review/selector.test.mjs (checks the critic selector of forge.workflow.js).
const src = readFileSync(new URL("../../assets/forge.workflow.js", import.meta.url), 'utf8').replace(/^export const meta/m, 'const meta')
const fnSrc = src.slice(0, src.indexOf('const STR ='))  // constants + selector only
const AsyncFn = Object.getPrototypeOf(async function () {}).constructor
const mk = args => new AsyncFn('args', fnSrc + '\nreturn select')(args)
let fails = 0
const check = (name, got, want) => { const ok = JSON.stringify(got) === JSON.stringify(want); if (!ok) fails++; console.log(ok ? 'PASS' : 'FAIL', name, ok ? '' : JSON.stringify(got)) }
const sel = await mk({})
const UC = ['src/M/Application/Uc.cs']
const HTTP = ['src/M/Http/Endpoints.cs']
const ALL = [...HTTP, ...UC, 'tests/M/T.cs']
const noisy = { lenses: { security: ['src/M/Http/Endpoints.cs:4 +AuthorizeAsync'], observability: ['x:1 +LogWarning'] } }

// Shapes: at standard risk each shape runs exactly its own critics, whatever the paths say; security evidence in the
// changed lines always adds the security critic (a grill that missed the kind still gets one).
check('fix runs correctness only', sel(ALL, false, 'fix', [], null).kept, ['correctness'])
check('security content adds security to fix', sel(ALL, false, 'fix', [], noisy).kept, ['correctness', 'security'])
check('security content does not make it critical', sel(ALL, false, 'fix', [], noisy).critical, false)
check('refactor runs craft and architecture', sel([...HTTP, ...UC], false, 'refactor', [], null).kept, ['architecture', 'craft'])
check('crud runs correctness and contracts', sel(ALL, false, 'crud', ['feature'], null).kept, ['correctness', 'contracts'])
check('security content adds security to crud', sel(ALL, false, 'crud', ['feature'], noisy).kept, ['correctness', 'security', 'contracts'])
check('shape critics are mandatory', sel(UC, false, 'refactor', [], null).mandatory, ['architecture', 'craft'])
check('standard risk is not critical', sel(UC, false, 'crud', [], null).critical, false)

// Old names keep working as aliases.
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
const alias = (old, shape, risk) => {
  const a = sel(ALL, false, old, [], noisy), b = sel(ALL, false, shape, [], noisy, risk)
  return same(a.kept, b.kept) && a.shape === b.shape && a.critical === b.critical
}
check('alias small = fix', alias('small', 'fix'), true)
check('alias mechanical = refactor', alias('mechanical', 'refactor'), true)
check('alias standard = crud', alias('standard', 'crud'), true)
check('alias deep = crud + critical', alias('deep', 'crud', 'critical'), true)
check('alias deep resolves', [sel(UC, false, 'deep', [], null).shape, sel(UC, false, 'deep', [], null).critical], ['crud', true])
check('no profile is crud', sel(UC, false, undefined, [], null).shape, 'crud')

// Critical: kinds security, money, data-migration or cross-module aggravate any shape; security and data become mandatory.
for (const k of ['security', 'money', 'data-migration', 'cross-module']) {
  const r = sel(UC, false, 'fix', [k], null)
  check(`kind ${k} makes fix critical`, r.critical && r.mandatory.includes('security') && r.mandatory.includes('data'), true)
}
check('risk critical without a kind', sel(UC, false, 'refactor', [], null, 'critical').mandatory, ['security', 'data', 'architecture', 'craft'])
check('kind security, app-only diff', sel(UC, false, 'fix', ['security'], null).kept, ['correctness', 'security', 'data', 'craft'])
// Mandatory beats the cap: critical leaves room up to 6 critics, but never drops a mandatory lens.
check('mandatory beats cap', sel(UC, false, 'fix', ['security', 'data', 'contract', 'cross-module', 'operational'], null).kept,
  ['correctness', 'security', 'data', 'contracts', 'performance', 'dx', 'observability', 'architecture'])
// Content signals fill the critical room before path-only lenses.
check('content signal fills critical room', sel(UC, false, 'fix', ['money'], { lenses: { observability: ['x:1 +LogWarning'] } }).kept,
  ['correctness', 'security', 'data', 'tests', 'observability', 'craft'])
check('content before path', sel(HTTP, false, 'fix', ['money'], { lenses: { observability: ['x:1 +LogWarning'] } }).kept,
  ['correctness', 'security', 'data', 'tests', 'observability', 'contracts'])
check('unknown lens ignored', sel(UC, false, 'fix', ['money'], { lenses: { nonsense: ['a'] } }).kept, ['correctness', 'security', 'data', 'tests', 'craft'])
// lensStats: a demoted lens yields its place to an undemoted one of the same tier, never when it is mandatory.
const sel3 = await mk({ lensStats: { contracts: { findings: 40, blockers: 1, advisories: 2 } } })
check('demoted lens', sel3(HTTP, false, 'fix', ['money'], null).kept, ['correctness', 'security', 'data', 'tests', 'dx', 'craft'])
check('demoted but mandatory', sel3(HTTP, false, 'crud', ['money'], null).kept, ['correctness', 'security', 'data', 'contracts', 'tests', 'dx'])
const sel4 = await mk({ lensStats: { contracts: { findings: 10, blockers: 0, advisories: 0 } } })
check('few findings, no demotion', sel4(HTTP, false, 'fix', ['money'], null).kept, ['correctness', 'security', 'data', 'tests', 'contracts', 'dx'])
check('CancellationToken file does not trigger security by path', sel(['src/M/Application/CancellationTokenHelper.cs'], false, 'fix', ['money'], null).why.security, ['critical'])

// A refactor that touches contracts or removes an assertion is not a refactor: it escalates to crud.
const esc1 = sel([...UC, 'contracts/openapi/v1.json'], false, 'refactor', [], null)
check('contracts diff escalates refactor', [esc1.shape, esc1.kept], ['crud', ['correctness', 'contracts']])
const esc2 = sel([...UC, 'tests/M/T.cs'], false, 'refactor', [], { lenses: { tests: ['tests/M/T.cs:12 -Assert.That(x).IsTrue()'] } })
check('removed assertion escalates refactor', [esc2.shape, !!esc2.escalated], ['crud', true])
const esc3 = sel([...UC, 'tests/M/T.cs'], false, 'refactor', [], { lenses: { tests: ['tests/M/T.cs:12 +[Skip("later")]'] } })
check('a test signal without a removed line keeps refactor', esc3.shape, 'refactor')

// docs and tests stay as they are.
check('docs profile', sel(['README.md'], false, 'docs', [], null).kept, [])
check('docs profile with contracts', sel(['contracts/openapi/v1.json'], false, 'docs', [], null).kept, ['contracts', 'dx'])
check('tests profile', sel(['tests/A/T.cs'], false, 'tests', [], null).kept, ['tests'])
check('tests lens is not mandatory in tests', sel(['tests/A/T.cs'], false, 'tests', [], null).mandatory, [])
check('docs keeps content signals within its cap', sel(['README.md'], false, 'docs', [], { lenses: { security: ['README.md:1 +token'] } }).kept, ['security'])
process.exit(fails ? 1 : 0)
