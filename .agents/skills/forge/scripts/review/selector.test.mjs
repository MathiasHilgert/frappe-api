import { readFileSync } from 'node:fs'
// Run: node .agents/skills/forge/scripts/review/selector.test.mjs (checks the critic selector of forge.workflow.js).
const src = readFileSync(new URL("../../assets/forge.workflow.js", import.meta.url), 'utf8').replace(/^export const meta/m, 'const meta')
const fnSrc = src.slice(0, src.indexOf('const STR ='))  // constants + selector only
const AsyncFn = Object.getPrototypeOf(async function () {}).constructor
const mk = args => new AsyncFn('args', fnSrc + '\nreturn select')(args)
let fails = 0
const check = (name, got, want) => { const ok = JSON.stringify(got) === JSON.stringify(want); if (!ok) fails++; console.log(ok ? 'PASS' : 'FAIL', name, ok ? '' : JSON.stringify(got)) }
const sel = await mk({})
// Layer 1: a security kind on an application-only diff (no security path) makes security run.
check('kind security, app-only diff', sel(['src/M/Application/Uc.cs'], false, 'small', ['security'], null).kept, ['correctness', 'security', 'craft'])
// Layer 1 beats the cap: small cap 3, kinds security+data -> correctness, security, data, performance all run.
check('mandatory beats cap', sel(['src/M/Application/Uc.cs'], false, 'small', ['security', 'data'], null).kept, ['correctness', 'security', 'data', 'performance'])
// Layer 2: content signal for security on a use case file, without the kind.
check('content signal', sel(['src/M/Application/Uc.cs'], false, 'small', [], { lenses: { security: ['src/M/Application/Uc.cs:4 +AuthorizeAsync'] } }).kept, ['correctness', 'security', 'craft'])
// Layer 2 outranks path-only: small cap 3, path gives dx+contracts+security (Http), content gives observability.
const r = sel(['src/M/Http/Endpoints.cs'], false, 'small', [], { lenses: { observability: ['x:1 +LogWarning'] } })
check('content before path', r.kept, ['correctness', 'observability', 'security'])
// Unknown lens names in signals are ignored.
check('unknown lens ignored', sel(['src/M/Application/Uc.cs'], false, 'small', [], { lenses: { nonsense: ['a'] } }).kept, ['correctness', 'craft'])
// Layer 3: a demoted lens yields its place to an undemoted one of the same tier.
const sel3 = await mk({ lensStats: { security: { findings: 40, blockers: 1, advisories: 2 } } })
check('demoted lens', sel3(['src/M/Http/Endpoints.cs'], false, 'small', [], null).kept, ['correctness', 'contracts', 'dx'])
// ...but never when it is mandatory.
check('demoted but mandatory', sel3(['src/M/Http/Endpoints.cs'], false, 'small', ['security'], null).kept, ['correctness', 'security', 'contracts'])
// Too few findings: no demotion.
const sel4 = await mk({ lensStats: { security: { findings: 10, blockers: 0, advisories: 0 } } })
check('few findings, no demotion', sel4(['src/M/Http/Endpoints.cs'], false, 'small', [], null).kept, ['correctness', 'security', 'contracts'])
// Old behaviors kept.
check('docs profile', sel(['README.md'], false, 'docs', [], null).kept, [])
check('tests profile', sel(['tests/A/T.cs'], false, 'tests', [], null).kept, ['tests'])
check('CancellationToken file does not trigger security', sel(['src/M/Application/CancellationTokenHelper.cs'], false, 'small', [], null).kept, ['correctness', 'craft'])
process.exit(fails ? 1 : 0)
