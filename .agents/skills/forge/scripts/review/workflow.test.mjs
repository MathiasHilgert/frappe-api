// Run: node .agents/skills/forge/scripts/review/workflow.test.mjs
// Runs forge.workflow.js against a fake agent to check the review stage: blind critics, failed critics, ruled-out
// concerns, suppressions, revived findings, the fix review and the chain head.
import { readFileSync } from 'node:fs'

const src = readFileSync(new URL('../../assets/forge.workflow.js', import.meta.url), 'utf8').replace(/^export const meta/m, 'const meta')
const AsyncFn = Object.getPrototypeOf(async function () {}).constructor

const finding = (id, severity = 'major') => ({ id, severity, confidence: 90, file: 'a.cs', line: 1, title: 't', evidence: 'e', failure: 'f', fix: 'x' })

// behavior: { failCritic: { [lens]: times }, fixBug: bool, ruledOut: bool, revive: bool, clean: bool (critics find nothing),
//   unregistered: bool (agent types are not registered: role() falls back to a general agent) }
async function run(args, behavior = {}) {
  const calls = []
  const failures = { ...(behavior.failCritic || {}) }
  const agent = async (prompt, o) => {
    calls.push({ label: o.label, prompt, effort: o.effort, model: o.model })
    if (behavior.unregistered && o.agentType) throw new Error('unregistered')
    if (behavior.delay) await new Promise(r => setTimeout(r, behavior.delay))
    const l = o.label
    if (l.startsWith('write:')) return { status: 'done', changedPaths: ['src/M/Http/Endpoints.cs', 'src/M/Application/Uc.cs', 'tests/M/T.cs'], contractChanged: true, checks: [], decisions: [], notes: 'WRITER-NOTES-MARKER', commit: prompt.includes('Chain slice') ? 'sha' + calls.length : undefined }
    if (l.startsWith('correct:') || l.startsWith('qa-fix:')) return { status: 'done', changedPaths: ['src/M/Application/Uc.cs'], contractChanged: false, checks: [], decisions: [], commit: prompt.includes('fixup!') ? 'fix' + calls.length : undefined }
    if (l.startsWith('fix-review:')) return { summary: '', checked: [], findings: behavior.fixBug ? [finding('correctness-9', 'blocker')] : [] }
    if (l.startsWith('fix-arbiter:')) return { verdict: 'fix', blockers: [{ id: 'correctness-9', file: 'a', title: 't', evidence: 'e', fix: 'x' }], advisories: [], dropped: [] }
    if (l.startsWith('recheck:')) return { results: [{ id: 'correctness-1', outcome: 'resolved', evidence: 'ok' }] }
    if (l.startsWith('arbiter:') && behavior.autoOnly) return { verdict: 'pass', blockers: [], advisories: [{ id: 'craft-1', file: 'a', title: 't', evidence: 'e', fix: 'x', autoApply: true }], dropped: [] }
    if (l.startsWith('arbiter:')) return {
      verdict: 'fix',
      blockers: [{ id: 'correctness-1', file: 'a', title: 't', evidence: 'e', fix: 'x' }],
      advisories: behavior.revive ? [{ id: 'security-r1', file: 'a', title: 't', evidence: 'e', fix: 'x', autoApply: false, uncertain: true }] : [],
      dropped: [], revived: behavior.revive ? ['security-r1'] : [],
      decisions: behavior.decision ? [{ id: 'D1', title: 'Which id', context: 'ids leak state', options: [], recommended: 'new id', blocking: true }] : [],
    }
    if (l.startsWith('qa:')) return { status: 'pass', scenarios: [], defects: [], decisions: [] }
    const lens = l.split(':')[0]
    if (failures[lens] > 0) { failures[lens]--; return null }
    return { summary: '', checked: [], findings: behavior.clean ? [] : [finding(`${lens}-1`)], ruledOut: behavior.ruledOut ? [{ concern: `${lens} concern`, evidence: 'guarded at b.cs:3' }] : [] }
  }
  const parallel = fns => Promise.all(fns.map(f => f()))
  const pipeline = (items, ...stages) => Promise.all(items.map(async it => { let r = it; for (const s of stages) r = await s(r); return r }))
  const out = await new AsyncFn('args', 'agent', 'parallel', 'pipeline', 'log', src)(args, agent, parallel, pipeline, () => {})
  return { out, calls }
}

const task = (extra = {}) => ({ id: 'T1', title: 't', specs: 'S1', worktree: '/w', branch: 'feat/x', surfaces: [], profile: 'standard', qa: false, ...extra })
const base = { repo: '/r', feature: 'f', featureDoc: '/r/odd/tasks/f.md' }
let fails = 0
const check = (name, ok, detail = '') => { if (!ok) fails++; console.log(ok ? 'PASS' : 'FAIL', name, ok ? '' : detail) }

{
  const { out, calls } = await run({ ...base, waves: [[task()]] }, { ruledOut: true })
  const critics = calls.filter(c => /^(correctness|security|data|contracts|tests|performance|dx|observability|architecture|craft):/.test(c.label))
  check('critics never see the writer notes', critics.every(c => !c.prompt.includes('WRITER-NOTES-MARKER')))
  check('critics are told not to read the journal', critics.every(c => c.prompt.includes('Do not read the task journal') && !c.prompt.includes('Task journal:')))
  const arbiter = calls.find(c => c.label.startsWith('arbiter:'))
  check('arbiter still gets the writer notes', arbiter.prompt.includes('WRITER-NOTES-MARKER'))
  check('arbiter gets the ruled-out concerns', arbiter.prompt.includes('ruled out (') && arbiter.prompt.includes('concern'))
  check('a clean run passes', out.status === 'done', out.status)
}
{
  const { out, calls } = await run({ ...base, waves: [[task({ profile: 'deep' })]] }, { failCritic: { dx: 1 } })
  check('a critic that fails once is retried', calls.filter(c => c.label === 'dx:T1').length === 2)
  check('a retried critic counts as run', !(out.tasks[0].criticsFailed || []).length, JSON.stringify(out.tasks[0].criticsFailed))
}
{
  const { out, calls } = await run({ ...base, waves: [[task({ profile: 'deep' })]] }, { failCritic: { dx: 2 } })
  const arbiter = calls.find(c => c.label.startsWith('arbiter:'))
  check('an optional critic that fails twice is reported, not zero', out.tasks[0].criticsFailed.includes('dx') && arbiter.prompt.includes('did NOT run') && arbiter.prompt.includes('dx'))
  check('the run goes on without it', out.status === 'done', out.status)
}
{
  const { out, calls } = await run({ ...base, waves: [[task({ kinds: ['security'] })]] }, { failCritic: { security: 2 } })
  check('a mandatory critic that fails twice stops the task', out.tasks[0].stopped && /mandatory critic not run: security/.test(out.tasks[0].reason), out.tasks[0].reason)
  check('no arbiter runs after it', !calls.some(c => c.label.startsWith('arbiter:')))
}
{
  const supp = [{ lens: 'craft', rule: 'No comments asking for XML docs on private members', reason: 'declined by the user' }]
  const { calls } = await run({ ...base, suppressions: supp, waves: [[task()]] })
  check('arbiter gets the user suppressions', calls.find(c => c.label.startsWith('arbiter:')).prompt.includes('XML docs on private members'))
  check('critics never get the suppressions', calls.filter(c => /^(correctness|security):/.test(c.label)).every(c => !c.prompt.includes('XML docs on private members')))
}
{
  const { out } = await run({ ...base, waves: [[task({ profile: 'deep' })]] }, { revive: true })
  const sec = out.criticStats.find(s => s.critic === 'security')
  check('revived findings are counted per critic', sec && sec.revived === 1, JSON.stringify(sec))
}
{
  const { out } = await run({ ...base, waves: [[task()]] }, { fixBug: true })
  check('a correction that introduces a blocker stops the task', out.tasks[0].stopped && /correction introduced blockers/.test(out.tasks[0].reason), out.tasks[0].reason)
}
{
  const chain = [task({ id: 'S1', commit: 'feat(x): a', qa: true }), task({ id: 'S2', commit: 'feat(x): b' })]
  const { out, calls } = await run({ ...base, chains: [chain] })
  const qa = calls.find(c => c.label.startsWith('qa:'))
  check('chain head QA reads every slice journal', qa && qa.prompt.includes('f.journal/S1.md') && qa.prompt.includes('f.journal/S2.md'))
  check('chain head does not double count critic stats', out.criticStats.find(s => s.critic === 'correctness').runs === 2)
}
{
  // The verification of slice 1's correction (recheck, fix review) reads by sha, so slice 3's writer starts without
  // waiting for it; only the correction writer holds the worktree.
  const chain = ['S1', 'S2', 'S3'].map(id => task({ id, commit: `feat(x): ${id}` }))
  const { out, calls } = await run({ ...base, chains: [chain] }, { delay: 5 })
  const at = label => calls.findIndex(c => c.label === label)
  check('next writer does not wait for the fix verification', at('write:S3') !== -1 && at('fix-review:S1') !== -1 && at('write:S3') < at('fix-review:S1'), calls.map(c => c.label).join(' '))
  check('recheck reads the fixup by sha', calls.find(c => c.label === 'recheck:S1').prompt.includes('Read the code as of the fix commit'))
  check('the pipelined chain still finishes', out.status === 'done', out.status)
}
{
  const { calls } = await run({ ...base, waves: [[task()]] }, { autoOnly: true })
  check('autoApply-only correction runs the writer', calls.some(c => c.label === 'correct:T1'))
  check('autoApply-only correction skips recheck and fix review', !calls.some(c => /^(recheck|fix-review):/.test(c.label)), calls.map(c => c.label).join(' '))
}
{
  const { out, calls } = await run({ ...base, deferQa: true, waves: [[task({ qa: true })]] })
  const pending = out.tasks[0].qaPending
  check('deferQa returns QA as pending', !!pending && !calls.some(c => c.label.startsWith('qa:')))
  check('pending QA carries what mode qa needs', pending && pending.changedPaths.length > 0 && pending.qa === true)
  const second = await run({ ...base, mode: 'qa', waves: [[pending]] })
  check('mode qa runs only QA', second.calls.every(c => c.label.startsWith('qa:')) && second.out.status === 'done', second.calls.map(c => c.label).join(' '))
}
{
  // A blocking arbiter decision stops the slice; the result tags it 'arbiter' and offers a reviewed done entry.
  const { out } = await run({ ...base, waves: [[task({ commit: 'feat(x): a' })]] }, { decision: true })
  check('an arbiter decision is tagged with its source', out.decisions.length === 1 && out.decisions[0].source === 'arbiter', JSON.stringify(out.decisions))
}
{
  // A fresh run with the ruling: the arbiter raises D1 again, but it becomes a correction blocker, never a stop, and
  // never reaches the writer.
  const { out, calls } = await run({ ...base, rulings: { T1: { D1: 'RULING-MARKER' } }, waves: [[task()]] }, { decision: true })
  check('a ruled decision does not stop the task', out.status === 'done', out.status + ' ' + JSON.stringify(out.tasks[0].reason))
  check('the ruling reaches the correction', calls.find(c => c.label === 'correct:T1').prompt.includes('RULING-MARKER'))
  check('the ruling never reaches the writer', !calls.find(c => c.label === 'write:T1').prompt.includes('RULING-MARKER'))
}
{
  // done slices skip their writer; reviewed ones skip their critics; a ruling on a reviewed slice runs only its fixup.
  const chain = [task({ id: 'S1', commit: 'feat(x): a' }), task({ id: 'S2', commit: 'feat(x): b' }), task({ id: 'S3', commit: 'feat(x): c' })]
  const done = {
    S1: { commit: 'aaa', changedPaths: ['src/M/A.cs'], reviewed: true },
    S2: { commit: 'bbb', changedPaths: ['src/M/Http/Endpoints.cs'], reviewed: false },
  }
  const { out, calls } = await run({ ...base, done, rulings: { S1: { D1: { title: 'Which id', answer: 'RULING-MARKER' } } }, chains: [chain] })
  const labels = calls.map(c => c.label)
  check('done slices never rerun their writer', !labels.includes('write:S1') && !labels.includes('write:S2') && labels.includes('write:S3'), labels.join(' '))
  check('a reviewed done slice runs no critics', !labels.some(l => /^[a-z]+:S1$/.test(l) && !/^(correct|recheck|fix-review):/.test(l)), labels.join(' '))
  check('its ruling lands as a fixup of that slice', (calls.find(c => c.label === 'correct:S1') || { prompt: '' }).prompt.includes('fixup! feat(x): a') && calls.find(c => c.label === 'correct:S1').prompt.includes('RULING-MARKER'))
  check('an unreviewed done slice is reviewed from its commit', calls.some(c => c.label === 'correctness:S2' && c.prompt.includes('bbb^ bbb')), labels.join(' '))
  check('the result offers done entries for the next run', out.tasks.some(t => t.id === 'S3' && t.done && t.done.reviewed), JSON.stringify(out.tasks.map(t => [t.id, t.done])))
}
{
  const { calls } = await run({ ...base, chains: [[task({ id: 'F1', commit: 'feat(x): f', base: 'feat/foundation' })]] })
  check('a fan-out chain uses its own base', calls.find(c => c.label === 'write:F1').prompt.includes('base feat/foundation'))
}
{
  // When the verification of slice 1 stops the chain, the review of slice 2 that finished in the same step is kept,
  // and its unfixed blockers travel in done.pendingFixes so the next fresh run corrects them without new critics.
  const chain = ['S1', 'S2', 'S3'].map(id => task({ id, commit: `feat(x): ${id}` }))
  const { out } = await run({ ...base, chains: [chain] }, { fixBug: true })
  const s2 = out.tasks.find(x => x.id === 'S2')
  check('an in-flight review survives a chain stop', s2 && s2.verdict && s2.verdict.blockers.length === 1, JSON.stringify(out.tasks.map(x => [x.id, x.reason])))
  check('its unfixed blockers go to pendingFixes', s2 && s2.done && s2.done.reviewed && s2.done.pendingFixes.some(b => b.id === 'correctness-1'), JSON.stringify(s2 && s2.done))
  const next = await run({ ...base, done: { S1: out.tasks.find(x => x.id === 'S1').done, S2: s2.done }, chains: [chain] })
  const labels = next.calls.map(c => c.label)
  check('the next run corrects them without new critics', labels.includes('correct:S2') && !labels.includes('correctness:S2') && next.calls.find(c => c.label === 'correct:S2').prompt.includes('correctness-1'), labels.join(' '))
}
{
  const { calls } = await run({ ...base, waves: [[task({ files: ['src/M/A.cs:10-40'] }), task({ id: 'T2', effort: 'medium' })]] })
  check('writers default to low effort', calls.find(c => c.label === 'write:T1').effort === 'low')
  check('the orchestrator can raise a slice effort, corrections follow it', calls.find(c => c.label === 'write:T2').effort === 'medium' && calls.find(c => c.label === 'correct:T2').effort === 'medium')
  check('the assembled context reaches the writer', calls.find(c => c.label === 'write:T1').prompt.includes('Assembled context') && calls.find(c => c.label === 'write:T1').prompt.includes('src/M/A.cs:10-40'))
  const w1 = calls.find(c => c.label === 'write:T1').prompt, w2 = calls.find(c => c.label === 'write:T2').prompt
  check('writers of different slices share a stable prompt prefix', w1.slice(0, 600) === w2.slice(0, 600))
}
{
  const { calls } = await run({ ...base, answers: { T1: { D1: 'ANSWER-MARKER' } }, waves: [[task()]] })
  check('the arbiter sees the user answers so it never asks again', calls.find(c => c.label === 'arbiter:T1').prompt.includes('ANSWER-MARKER'))
}
{
  // fix and refactor: the arbiter runs only when the critics found something.
  const { out, calls } = await run({ ...base, waves: [[task({ profile: 'fix' })]] }, { clean: true })
  check('fix without findings skips the arbiter', !calls.some(c => c.label.startsWith('arbiter:')) && out.status === 'done', calls.map(c => c.label).join(' '))
  check('fix runs correctness only', out.tasks[0].critics.join() === 'correctness', JSON.stringify(out.tasks[0].critics))
  const found = await run({ ...base, waves: [[task({ profile: 'fix' })]] })
  check('fix with findings runs the arbiter', found.calls.some(c => c.label === 'arbiter:T1'))
  const crud = await run({ ...base, waves: [[task({ profile: 'crud' })]] }, { clean: true })
  check('crud runs the arbiter even without findings', crud.calls.some(c => c.label === 'arbiter:T1'))
  const ruled = await run({ ...base, rulings: { T1: { D1: 'RULING-MARKER' } }, waves: [[task({ profile: 'small' })]] }, { clean: true })
  check('a ruling still reaches the correction when the arbiter is skipped', (ruled.calls.find(c => c.label === 'correct:T1') || { prompt: '' }).prompt.includes('RULING-MARKER'), ruled.calls.map(c => c.label).join(' '))
  check('critic stats still count a skipped arbiter', ruled.out.criticStats.some(s => s.critic === 'correctness'), JSON.stringify(ruled.out.criticStats))
}
{
  // Writer model and effort follow the risk; the orchestrator can still set them per slice.
  const { calls } = await run({ ...base, waves: [[task({ kinds: ['money'] }), task({ id: 'T2', profile: 'fix' }), task({ id: 'T3', profile: 'crud', model: 'opus' })]] })
  const w = id => calls.find(c => c.label === `write:${id}`)
  check('a critical writer runs on opus at high effort', w('T1').model === 'opus' && w('T1').effort === 'high', JSON.stringify(w('T1')))
  check('its correction follows', calls.find(c => c.label === 'correct:T1').model === 'opus')
  check('a fix writer runs on sonnet at low effort', w('T2').model === 'sonnet' && w('T2').effort === 'low', JSON.stringify(w('T2')))
  check('task.model overrides (module foundation)', w('T3').model === 'opus')
  check('the writer is told its shape', w('T2').prompt.includes('Profile: fix') && w('T2').prompt.includes('failing regression test') && w('T1').prompt.includes('critical'))
  const fb = await run({ ...base, waves: [[task({ profile: 'deep' })]] }, { unregistered: true })
  check('the unregistered fallback keeps the opus writer', fb.calls.filter(c => c.label === 'write:T1').every(c => c.model === 'opus'), JSON.stringify(fb.calls.filter(c => c.label === 'write:T1')))
}
{
  // QA by shape and risk (the fake writer changes an endpoint).
  const r = await run({ ...base, deferQa: true, waves: [[task({ profile: 'refactor', qa: undefined }), task({ id: 'T2', profile: 'fix', qa: undefined }), task({ id: 'T3', profile: 'crud', kinds: ['security'], qa: false })]] })
  const t = id => r.out.tasks.find(x => x.id === id)
  check('refactor never runs QA', !t('T1').qaPending)
  check('fix runs QA when an endpoint changed', !!t('T2').qaPending)
  check('critical always runs QA, even with qa: false', !!t('T3').qaPending && t('T3').qaPending.risk === 'critical', JSON.stringify(t('T3').qaPending))
  const chain = [task({ id: 'S1', commit: 'feat(x): a', profile: 'deep' }), task({ id: 'S2', commit: 'feat(x): b', profile: 'standard' })]
  const c = await run({ ...base, deferQa: true, chains: [chain] })
  const head = c.out.tasks.find(x => x.id === 'S2-head')
  check('a chain with a critical slice keeps QA on its head', !!(head && head.qaPending), JSON.stringify(c.out.tasks.map(x => x.id)))
  // The head is QA'd as its most demanding slice, not its last one.
  const mixed = [task({ id: 'S1', commit: 'feat(x): a', profile: 'crud', qa: undefined }), task({ id: 'S2', commit: 'refactor(x): b', profile: 'refactor', qa: undefined })]
  const m = await run({ ...base, deferQa: true, chains: [mixed] })
  check('a chain ending in a refactor still gets QA for its crud slice', !!(m.out.tasks.find(x => x.id === 'S2-head') || {}).qaPending, JSON.stringify(m.out.tasks.map(x => x.id)))
}
{
  const { calls } = await run({ ...base, waves: [[task({ profile: 'fix' })]] })
  check('critics are not told the writer rules', calls.filter(c => c.label === 'correctness:T1').every(c => !c.prompt.includes('Profile:')))
}
process.exit(fails ? 1 : 0)
