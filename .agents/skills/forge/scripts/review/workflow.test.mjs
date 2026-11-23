// Run: node .agents/skills/forge/scripts/review/workflow.test.mjs
// Runs forge.workflow.js against a fake agent to check the review stage: blind critics, failed critics, ruled-out
// concerns, suppressions, revived findings, the fix review and the chain head.
import { readFileSync } from 'node:fs'

const src = readFileSync(new URL('../../assets/forge.workflow.js', import.meta.url), 'utf8').replace(/^export const meta/m, 'const meta')
const AsyncFn = Object.getPrototypeOf(async function () {}).constructor

const finding = (id, severity = 'major') => ({ id, severity, confidence: 90, file: 'a.cs', line: 1, title: 't', evidence: 'e', failure: 'f', fix: 'x' })

// behavior: { failCritic: { [lens]: times }, fixBug: bool, ruledOut: bool, revive: bool }
async function run(args, behavior = {}) {
  const calls = []
  const failures = { ...(behavior.failCritic || {}) }
  const agent = async (prompt, o) => {
    calls.push({ label: o.label, prompt })
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
    }
    if (l.startsWith('qa:')) return { status: 'pass', scenarios: [], defects: [], decisions: [] }
    const lens = l.split(':')[0]
    if (failures[lens] > 0) { failures[lens]--; return null }
    return { summary: '', checked: [], findings: [finding(`${lens}-1`)], ruledOut: behavior.ruledOut ? [{ concern: `${lens} concern`, evidence: 'guarded at b.cs:3' }] : [] }
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
  const { out, calls } = await run({ ...base, waves: [[task()]] }, { failCritic: { dx: 1 } })
  check('a critic that fails once is retried', calls.filter(c => c.label === 'dx:T1').length === 2)
  check('a retried critic counts as run', !(out.tasks[0].criticsFailed || []).length, JSON.stringify(out.tasks[0].criticsFailed))
}
{
  const { out, calls } = await run({ ...base, waves: [[task()]] }, { failCritic: { dx: 2 } })
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
  const { out } = await run({ ...base, waves: [[task()]] }, { revive: true })
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
process.exit(fails ? 1 : 0)
