export const meta = {
  name: 'forge',
  description: 'Forge an ODD plan: writers per task in worktrees, diff-selected critics, arbiter, one correction, runtime QA',
  whenToUse: 'Launched by the /forge skill after the grill and the plan are approved; never directly.',
  phases: [
    { title: 'Write', detail: 'one forge-writer per task, test-first, CI checks before returning' },
    { title: 'Review', detail: 'critics chosen from the changed paths, then the arbiter' },
    { title: 'Correct', detail: 'one correction round on the arbiter blockers, recheck limited to them' },
    { title: 'QA', detail: 'forge-qa on an isolated Docker slot through Bruno' },
  ],
  // chains: a module or multi-slice feature built on ONE branch, one commit per planned slice; critics review slice k
  // while the writer builds slice k+1; corrections land as fixup commits; QA runs once on the chain's head.
}

// args: { repo, feature, featureDoc, base, answers: { [taskId]: { [decisionId]: string } }, apply: { [taskId]: [advisory id or title] }, waves: [[task]], chains: [[slice]] }
// chains (preferred for a module or any feature over one PR): each chain is one branch in one worktree, its slices run in order;
//   independent chains (different modules) run in parallel. A slice is a task plus `commit` (its Conventional Commit header,
//   which becomes its PR title). Waves and chains can be combined: waves run first.
// answers: only the user's answers to BLOCKING decisions; they reach the writer (which must act on them), so they change its cache key.
// apply: advisories to apply on a resume, by id or by title (ids are renumbered whenever critics rerun); they reach ONLY the
//   correction prompt, so a resume that only adds apply replays writer, critics and arbiter from cache.
// task.brief: stable per-task context written at launch (never edit it between a run and its resume: it is part of every cache key).
// task: { id, title, specs, worktree, branch, surfaces: [glob], profile: docs|tests|mechanical|small|standard|deep, contract: bool, qa: bool,
//         effort: low|medium|high (writer effort, default low), files: [path or path:lines] (assembled context the writer reads first),
//         codemods: bool (the orchestrator already ran scripts/codemods/run.sh in the worktree),
//         kinds: [docs|tests|small|feature|contract|data|cross-module|security|operational] (from the grill; makes lenses mandatory),
//         signals: (review mode only) the output of scripts/review/signals.py for the PR's diff }
// lensStats: { [lens]: { findings, blockers, advisories } } running totals from Engram forge/metrics/critics (demotes noisy lenses)
// pitfalls: [string] extra known pitfalls from Engram for this run, on top of references/pitfalls.md
// done: { [taskId]: { commit, changedPaths, contractChanged, signals, reviewed } } slices an earlier run already committed
//   (copy them from that run's tasks[]). Their writer never runs again; `reviewed: true` also skips their critics. This is how a
//   run continues: launch FRESH with `done` instead of resumeFromRunId, because the Workflow cache is positional and a chain
//   starts agents concurrently, so a resume replays only up to the first call whose order or args moved.
// rulings: { [taskId]: { [decisionId]: string | { title, context, answer } } } the user's answers to blocking decisions the
//   ARBITER or QA raised about committed code. They never reach the writer (it does not rerun on top of its own commit and
//   the slices after it): they become blockers the correction round fixes as a `fixup!`.
// task.base: the base of a chain that starts from another chain's head (fan-out after a foundation chain); default args.base.
const A = args || {}
const BASE = A.base || 'origin/main'
const baseOf = t => t.base || BASE
const ANSWERS = A.answers || {}
const DONE = A.done || {}
const RULINGS = A.rulings || {}
// A ruling as a blocker the correction round fixes. `d` is the decision the arbiter raised, when this run raised it again.
const rulingItem = (taskId, id, d) => {
  const v = RULINGS[taskId][id]
  const answer = typeof v === 'string' ? v : v.answer
  return { id: `ruling-${id}`, file: '', title: (d && d.title) || (v && v.title) || `user ruling ${id}`, evidence: (d && d.context) || (v && v.context) || '', fix: `The user decided: ${answer}. Apply it to the committed code.` }
}
// Advisories the user chose to apply after a pass: { [taskId]: [advisoryId] }. Relaunch with resumeFromRunId so the
// writer, critics and arbiter replay from cache and only the correction round runs.
const APPLY = A.apply || {}
const applies = (t, a) => (APPLY[t.id] || []).some(x => {
  const k = String(x).toLowerCase()
  return k === String(a.id).toLowerCase() || (k.length > 8 && String(a.title || '').toLowerCase().includes(k))
})
// mode: 'full' (default: write, review, correct, QA per task), 'write' (writers only; the orchestrator opens the PRs),
// 'review' (no writer: review the open PR's diff in its worktree, given as task.changedPaths, while CI runs; correct
// blockers only; no advisory round, no QA).
const MODE = A.mode || 'full'
// deferQa (full mode): skip QA and return it as pending, so the orchestrator opens the PRs and starts CI first, then
// launches mode 'qa' while CI runs. mode 'qa': no writer, no review: run only QA (and its fix) on the given tasks, which
// carry what the pending QA entry returned (changedPaths, contract, profile, qa, slices, journalOf).
const DEFER_QA = !!A.deferQa && MODE === 'full'
// suppressions: [{ lens?, rule, reason }] findings the user rejected before, from Engram forge/review/suppressions. Only the
// arbiter sees them (critics stay unbiased); it drops matching non-blocking findings and never suppresses a blocker.
const SUPPRESSIONS = A.suppressions || []

// Priority when the cap drops critics: earlier wins. Lenses that catch wrong behavior outrank style: a reviewer asked
// for problems always finds some, and style findings crowd out the ones that matter, so craft goes last.
const ORDER = ['correctness', 'security', 'data', 'contracts', 'tests', 'performance', 'dx', 'observability', 'architecture', 'craft']
const CAP = { docs: 2, tests: 1, mechanical: 2, small: 3, standard: 5, deep: 6 }
// Writer effort: the orchestrator sets task.effort per slice when it plans (low | medium | high); low is the default.
// Most of a writer's time is generating tokens, and effort is the main control on how much it generates. Corrections
// and QA fixes run at the slice's effort.
const writerEffort = t => t.effort || 'low'

const isDoc = p => /\.md$/.test(p)
const isTest = p => /^tests\//.test(p)
const isRuntime = p => /^src\/|^deploy\//.test(p)

// The profile the diff actually supports: never deeper than planned, shallower when the diff is docs- or tests-only.
function effectiveProfile(planned, paths) {
  if (paths.length && paths.every(isDoc)) return 'docs'
  if (paths.length && paths.every(p => isTest(p) || isDoc(p))) return 'tests'
  return planned || 'standard'
}

// Layer 1, the plan: each change kind the grill recorded on the task (task.kinds) makes its lenses mandatory. A mandatory
// lens is never dropped by the profile cap.
const KIND_LENSES = {
  security: ['security'],
  data: ['data', 'performance'],
  contract: ['contracts', 'dx'],
  'cross-module': ['architecture', 'contracts'],
  operational: ['performance', 'observability'],
}
// Layer 3, the data: args.lensStats is the running total from Engram (forge/metrics/critics), { [lens]: { findings,
// blockers, advisories } }. A lens with enough findings whose findings the arbiter mostly drops loses priority (never
// its mandatory place): it runs only when the cap leaves room after the useful ones.
const LENS_STATS = A.lensStats || {}
const DEMOTE_MIN_FINDINGS = 30
const DEMOTE_MAX_KEPT_SHARE = 0.2
const demoted = lens => {
  const s = LENS_STATS[lens]
  if (!s || !(s.findings >= DEMOTE_MIN_FINDINGS)) return false
  return ((s.blockers || 0) + (s.advisories || 0)) / s.findings < DEMOTE_MAX_KEPT_SHARE
}

// Deterministic selector in three layers: the plan's kinds (mandatory), the content of the diff (layer 2: the
// signals scripts/review/signals.py found in changed lines), and the changed paths. Correctness always runs.
// Optional lenses fill the cap in this order: content signal before path only, useful before demoted, then ORDER.
function select(paths, contract, profile, kinds, signals) {
  const has = re => paths.some(p => re.test(p))
  const srcCount = paths.filter(p => /^src\//.test(p)).length
  const why = {}
  const add = (set, lens, reason) => { set.add(lens); (why[lens] = why[lens] || []).push(reason) }
  const mandatory = new Set()
  add(mandatory, 'correctness', 'always')
  for (const k of kinds || []) for (const lens of KIND_LENSES[k] || []) add(mandatory, lens, `kind ${k}`)
  const content = new Set()
  for (const [lens, evidence] of Object.entries((signals && signals.lenses) || {})) {
    if (ORDER.includes(lens) && (evidence || []).length) add(content, lens, `content: ${evidence[0]}`)
  }
  const set = new Set()
  if (profile === 'docs') {
    if (has(/^contracts\/|^tools\/bruno\//)) { add(set, 'contracts', 'path'); add(set, 'dx', 'path') }
    mandatory.delete('correctness')
  } else if (profile === 'tests') {
    add(set, 'tests', 'path')
    mandatory.delete('correctness')
  } else {
    pathLenses(paths, contract, srcCount, has, (lens) => add(set, lens, 'path'))
  }
  const cap = CAP[profile] || 4
  const optional = ORDER.filter(c => !mandatory.has(c) && (content.has(c) || set.has(c)))
    .sort((a, b) => (content.has(b) - content.has(a)) || (demoted(a) - demoted(b)) || (ORDER.indexOf(a) - ORDER.indexOf(b)))
  const room = Math.max(0, cap - mandatory.size)
  const kept = [...ORDER.filter(c => mandatory.has(c)), ...optional.slice(0, room)]
  for (const c of optional) if (demoted(c)) why[c].push('demoted by lensStats')
  return { kept, dropped: optional.slice(room), why, mandatory: ORDER.filter(c => mandatory.has(c)) }
}

// The path layer: file names that point at a lens.
function pathLenses(paths, contract, srcCount, has, addLens) {
  const set = { add: addLens }
  if (has(/\/Http\/|Endpoints\.cs$|contracts\/openapi|tools\/bruno\//)) { set.add('dx'); set.add('contracts') }
  if (has(/\/Http\/|Endpoints\.cs$|Session|Auth|Role|Grant|Frappe\.Migrator\/|ModuleRoles|Secret|Password|(Access|Refresh|Session|Api)Token|Token(Service|Store|Hasher|Issuer)|Credential/)) set.add('security')
  if (has(/\.Contracts\/|IntegrationEvent|contracts\/events|Options\.cs$|OptionsValidator\.cs$/) || contract) set.add('contracts')
  if (has(/Migrations\/|Configuration\.cs$|Row\.cs$/)) set.add('data')
  if (has(/Migrations\//)) { set.add('contracts'); set.add('performance') }
  if (has(/Persistence\/|\/Ef[A-Z]\w*\.cs$|DbContext|Frappe\.Messaging\/|Caching\/|Cached[A-Z]|Repository|Queries\//)) set.add('performance')
  if (has(/Metrics|Observability|Telemetry|Instrumentation|deploy\/observability|Consumer|Handler\.cs$/)) set.add('observability')
  if (srcCount > 0) set.add('craft')
  // Any test change gets the tests lens: it is the only one that checks no test was weakened to pass.
  if (has(/^tests\//)) set.add('tests')
  if (has(/\.csproj$|Module\.cs$|Registration\.cs$|Composition\.cs$|LayeringTests|\.Contracts\//) || srcCount >= 6) set.add('architecture')
  if (has(/Directory\.Packages\.props$|package(-lock)?\.json$|\.csproj$/)) set.add('security')
}

const STR = { type: 'string' }
const STRS = { type: 'array', items: STR }
const DECISIONS = {
  type: 'array',
  items: {
    type: 'object',
    properties: {
      id: STR, title: STR, context: STR,
      options: { type: 'array', items: { type: 'object', properties: { label: STR, consequence: STR }, required: ['label', 'consequence'] } },
      recommended: STR, blocking: { type: 'boolean' },
    },
    required: ['id', 'title', 'context', 'options', 'recommended', 'blocking'],
  },
}

const FRICTION = { type: 'array', items: { type: 'object', properties: { problem: STR, attempts: { type: 'number' }, solution: STR }, required: ['problem', 'attempts', 'solution'] } }

const LEARNINGS = { type: 'array', items: STR }
// A proposal to change a frappe-conventions rule, add an exception, fix an inconsistency or add a missing rule
// (format in .agents/skills/frappe-conventions/SKILL.md). Writers and critics raise them; the arbiter keeps the proven ones.
const CONVENTIONS = {
  type: 'array',
  items: {
    type: 'object',
    properties: { rule: STR, kind: { type: 'string', enum: ['change', 'exception', 'inconsistency', 'missing'] }, evidence: STR, proposal: STR },
    required: ['rule', 'kind', 'evidence', 'proposal'],
  },
}
const WRITER = {
  type: 'object',
  properties: {
    status: { type: 'string', enum: ['done', 'partial', 'needs_input', 'blocked'] },
    specsCovered: STRS,
    changedPaths: STRS,
    contractChanged: { type: 'boolean' },
    docsUpdated: { type: 'object', properties: { openapi: { type: 'boolean' }, events: { type: 'boolean' }, bruno: { type: 'boolean' } } },
    redGreen: STRS,
    checks: { type: 'array', items: { type: 'object', properties: { command: STR, result: STR }, required: ['command', 'result'] } },
    decisions: DECISIONS,
    friction: FRICTION,
    learnings: LEARNINGS,
    conventions: CONVENTIONS,
    scratch: STRS,
    commit: STR,
    notes: STR,
    // The JSON scripts/review/signals.py printed, unchanged (layer 2 of the critic selector).
    signals: { type: 'object', properties: { lenses: { type: 'object' }, files: { type: 'number' } } },
  },
  required: ['status', 'changedPaths', 'contractChanged', 'checks', 'decisions'],
}

const FINDING = {
  type: 'object',
  properties: {
    id: STR, severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'nit'] }, confidence: { type: 'number' },
    file: STR, line: { type: 'number' }, rule: STR, title: STR, evidence: STR, failure: STR, fix: STR, preexisting: { type: 'boolean' },
  },
  required: ['id', 'severity', 'confidence', 'file', 'title', 'evidence', 'failure', 'fix'],
}
const FINDINGS = {
  type: 'object',
  properties: {
    summary: STR, checked: STRS, findings: { type: 'array', items: FINDING }, conventions: CONVENTIONS,
    // Concerns the critic looked at and dismissed, with the code that dismisses them: the arbiter re-checks the shaky ones.
    ruledOut: { type: 'array', items: { type: 'object', properties: { concern: STR, file: STR, line: { type: 'number' }, evidence: STR }, required: ['concern', 'evidence'] } },
  },
  required: ['summary', 'checked', 'findings'],
}

const BLOCKER = {
  type: 'object',
  properties: { id: STR, file: STR, line: { type: 'number' }, title: STR, evidence: STR, fix: STR, autoApply: { type: 'boolean' }, uncertain: { type: 'boolean' } },
  required: ['id', 'file', 'title', 'evidence', 'fix'],
}
const VERDICT = {
  type: 'object',
  properties: {
    verdict: { type: 'string', enum: ['pass', 'fix'] },
    blockers: { type: 'array', items: BLOCKER },
    advisories: { type: 'array', items: BLOCKER },
    dropped: { type: 'array', items: { type: 'object', properties: { id: STR, reason: STR }, required: ['id', 'reason'] } },
    followups: STRS,
    // Ids of findings the arbiter revived from a critic's ruledOut list (`<lens>-r<n>`); they also sit in blockers or advisories.
    revived: STRS,
    decisions: DECISIONS,
    learnings: LEARNINGS,
    conventions: CONVENTIONS,
  },
  required: ['verdict', 'blockers', 'advisories', 'dropped'],
}
const RECHECK = {
  type: 'object',
  properties: {
    results: { type: 'array', items: { type: 'object', properties: { id: STR, outcome: { type: 'string', enum: ['resolved', 'still_failing'] }, evidence: STR }, required: ['id', 'outcome', 'evidence'] } },
    advisories: STRS,
  },
  required: ['results'],
}
const QA = {
  type: 'object',
  properties: {
    status: { type: 'string', enum: ['pass', 'fail', 'needs_input'] },
    slot: { type: 'number' }, baseUrl: STR,
    scenarios: { type: 'array', items: { type: 'object', properties: { spec: STR, request: STR, expected: STR, observed: STR, pass: { type: 'boolean' } }, required: ['spec', 'request', 'expected', 'observed', 'pass'] } },
    brunoFiles: STRS,
    defects: { type: 'array', items: { type: 'object', properties: { spec: STR, evidence: STR, expected: STR, observed: STR }, required: ['spec', 'evidence', 'expected', 'observed'] } },
    decisions: DECISIONS,
    friction: FRICTION,
    learnings: LEARNINGS,
    commands: STRS,
  },
  required: ['status', 'scenarios', 'defects', 'decisions'],
}

// Roles are agent definitions under .agents/skills/forge/agents/. Claude Code registers them only when a session starts,
// so a session that created them (or a machine without the .claude/agents symlinks) falls back to a general agent that
// reads the definition file. The registered agent is preferred: it carries the restricted tool list.
// Writers run on Sonnet; the lenses that judge behavior and the arbiter run on Opus, so the code is not judged only by
// the model that wrote it (judges favor their own model's output). Same values as the frontmatter.
const ROLE_MODEL = {
  'forge-arbiter': 'opus', 'critic-correctness': 'opus', 'critic-security': 'opus', 'critic-data': 'opus',
}
// Effort per role when a role file is not registered as an agent type (the frontmatter carries the same values).
const ROLE_EFFORT = {
  'forge-arbiter': 'medium', 'critic-craft': 'low', 'critic-tests': 'low', 'critic-observability': 'low',
}
const roleFile = role => `${A.repo}/.agents/skills/forge/agents/${role}.md`
async function role(name, prompt, opts) {
  const preamble = `Your role definition: ${roleFile(name)} (read it first, ignore its frontmatter, follow it exactly).\n\n`
  try {
    return await agent(preamble + prompt, { ...opts, agentType: name })
  } catch (unregistered) {
    log(`${name} is not registered in this session; running it from its definition file`)
    return await agent(preamble + prompt, { effort: ROLE_EFFORT[name] || 'medium', ...opts, model: ROLE_MODEL[name] || 'sonnet' })
  }
}

// A chain head reads every slice's journal (its own id has none); it appends to the last one.
const journalPath = id => `${A.repo}/odd/tasks/${A.feature}.journal/${id}.md`
const journal = t => (t.journalOf && t.journalOf.length ? t.journalOf : [t.id]).map(journalPath).join(', ')
const journalTarget = t => journalPath(t.journalOf && t.journalOf.length ? t.journalOf[t.journalOf.length - 1] : t.id)

// blind: critics review the code against the specs alone. The writer's notes and journal tell the author's story, and a
// reviewer who reads it inherits its blind spots, so critics never get them (the arbiter does, to refute findings).
function context(t, withAnswers = false, blind = false) {
  const a = withAnswers ? ANSWERS[t.id] : undefined
  const answer = a ? `\nThe user decided (apply these, do not ask again): ${typeof a === 'string' ? a : JSON.stringify(a)}\n` : ''
  // Stable lines first: every agent of the run shares this prefix, so it is served from the prompt cache.
  return [
    `Repository main checkout: ${A.repo}`,
    `Conventions: read ${A.repo}/.agents/skills/frappe-conventions/SKILL.md (an index) and load only the rule files your task or diff needs; cite rule ids. A rule you think is wrong: keep following it and return conventions[] as that file describes.`,
    `Never ask the user anything: return decisions[] in the format of communication.md; take non-blocking ones with the recommended option.`,
    `Known pitfalls: read ${A.repo}/.agents/skills/forge/references/pitfalls.md first.${(A.pitfalls || []).length ? ' Also: ' + A.pitfalls.join(' ') : ''}`,
    `<use_parallel_tool_calls>When several tool calls do not depend on each other, make them all in the same turn. Change files with Edit on the lines that change, never by rewriting the whole file.</use_parallel_tool_calls>`,
    `Worktree: ${t.worktree} (branch ${t.branch}, base ${baseOf(t)})`,
    t.brief ? `Feature document: ${A.featureDoc} — the task brief below is your starting point; from the document read only the header, the specs ${t.specs} and the Design parts of this task, and open the rest only when the brief leaves a question.` : `Feature document: ${A.featureDoc} — read it from the top until "## Log".`,
    `Task ${t.id}: ${t.title} — specs ${t.specs}.`,
    t.chainOf ? `Chain slice ${t.chainIndex + 1} of ${t.chainOf} on ${t.branch}: earlier slices are already committed on this branch; build on them and touch only this slice's scope.` : '',
    t.brief ? `Task brief from the orchestrator: ${t.brief}` : '',
    `Public contract change expected: ${t.contract ? 'yes (update OpenAPI/events via just contracts update and tools/bruno)' : 'no'}.`,
    t.files && t.files.length ? `Assembled context (the orchestrator chose these for this task; read them first, in one parallel batch, and treat them as enough): ${t.files.join(', ')}. Anything else: codegraph_explore (projectPath = the worktree) or Read with offset/limit, never cat a whole file.` : '',
    blind
      ? `Do not read the task journal, the writer's notes or the Log of the feature document: judge the code against the specs alone.`
      : `Task journal: ${journal(t)} — read it first; append your section to ${journalTarget(t)} when you finish (see ${A.repo}/.agents/skills/forge/references/communication.md).`,
    t.codemods ? `Codemods already ran in this worktree (collection expressions, target-typed new, file-scoped namespaces, sealing): their changes are uncommitted; keep them and do not redo that work.` : '',
    `Profile: ${t.profile || 'standard'}.`,
    t.chainOf ? `Gate base: this slice's parent. Record \`git -C ${t.worktree} rev-parse HEAD\` before your first edit and pass it as \`gate.sh --base <that sha>\`, so the gate checks only this slice (earlier slices' fixups and migrations are not yours).` : '',
    answer,
  ].join('\n')
}

// Layer 2 of the critic selector: the writer runs the signals script last and returns its output untouched.
const signalsStep = t => `Last step, after your final gate${t.chainOf ? ' and the slice commit' : ''}: run \`python3 -I ${A.repo}/.agents/skills/forge/scripts/review/signals.py --worktree ${t.worktree} ${t.chainOf ? '--commit <your slice sha>' : `--base ${baseOf(t)}`}\` and return its JSON unchanged in \`signals\` (it decides which critics review you; never edit it).`

const surfaces = t => `## Allowed edit surfaces\n${(t.surfaces || []).join('\n')}\n`

function stopped(r) { return !r || r.stop }
// What critics review: a chain slice is its own commit; anything else is the whole branch against the base.
const range = r => r.write && r.write.commit ? `${r.write.commit}^ ${r.write.commit}` : baseOf(r.task)

async function write(t) {
  if (DONE[t.id]) {
    log(`${t.id}: committed in an earlier run (${DONE[t.id].commit || 'no sha'}); writer skipped${DONE[t.id].reviewed ? ', review skipped' : ''}`)
    return { task: t, write: { status: 'done', changedPaths: [], contractChanged: false, checks: [], decisions: [], ...DONE[t.id], notes: `Committed in an earlier run as ${DONE[t.id].commit}.` }, fromDone: true }
  }
  if (MODE === 'review' || MODE === 'qa') {
    return { task: t, write: { status: 'done', changedPaths: t.changedPaths || [], contractChanged: !!t.contract, signals: t.signals, notes: MODE === 'qa' ? `QA of the open PRs ${t.pr || ''}.` : `Open PR ${t.pr || ''}: review its diff against ${BASE}.` } }
  }
  if (!t.brief) log(`${t.id}: no brief: the writer starts by exploring (orientation was 20-25% of writer time)`)
  const out = await role('forge-writer',
    `${context(t, true)}\nImplement this task.${t.chainOf ? `\nChain slice: once your final gate passes, commit this slice and nothing else: \`git -C ${t.worktree} add -A && git -C ${t.worktree} commit -m "${t.commit}"\` (no attribution lines), and return its sha in \`commit\`. Never push, rebase or amend.` : ''}\n${signalsStep(t)}\n\n${surfaces(t)}`,
    { schema: WRITER, label: `write:${t.id}`, phase: 'Write', effort: writerEffort(t) })
  if (!out) return { task: t, stop: true, reason: 'writer returned nothing' }
  const blocking = (out.decisions || []).filter(d => d.blocking)
  if (out.status === 'needs_input' || out.status === 'blocked' || blocking.length) return { task: t, write: out, stop: true, reason: blocking.length ? 'decision needed' : out.status }
  if (t.chainOf && !out.commit) return { task: t, write: out, stop: true, reason: 'chain slice not committed' }
  return { task: t, write: out }
}

async function review(r) {
  if (stopped(r)) return r
  if (MODE === 'write') return { ...r, writeOnly: true }
  if (MODE === 'qa') return { ...r, profile: r.task.profile || 'standard' }
  const t = r.task
  const ruled = Object.keys(RULINGS[t.id] || {})
  if (r.fromDone && DONE[t.id].reviewed) {
    // pendingFixes: what an earlier run's arbiter asked for and no correction applied yet (it stopped first).
    const blockers = [...(DONE[t.id].pendingFixes || []), ...ruled.map(id => rulingItem(t.id, id))]
    if (blockers.length) log(`${t.id}: reviewed earlier; the correction applies ${blockers.length} item(s): ${blockers.map(b => b.id).join(', ')}`)
    return { ...r, profile: t.profile || 'standard', critics: [], verdict: { verdict: blockers.length ? 'fix' : 'pass', blockers, advisories: [], dropped: [] } }
  }
  const paths = r.write.changedPaths || []
  const profile = effectiveProfile(t.profile, paths)
  if (profile !== (t.profile || 'standard')) log(`${t.id}: planned ${t.profile || 'standard'}, diff is ${profile}`)
  if (!r.write.signals) log(`${t.id}: no content signals returned; selecting critics by kinds and paths only`)
  const { kept, dropped, why, mandatory } = select(paths, r.write.contractChanged, profile, t.kinds, r.write.signals)
  const selection = Object.fromEntries([...kept, ...dropped].map(c => [c, { run: kept.includes(c), why: why[c] }]))
  log(`${t.id}: critics ${kept.map(c => `${c} (${why[c].join('; ')})`).join(', ') || 'none'}`)
  if (dropped.length) log(`${t.id}: ${profile} caps critics at ${CAP[profile]}; not run: ${dropped.join(', ')}`)
  if (!kept.length) { log(`${t.id}: ${profile} change, no critic needed`); return { ...r, profile, critics: [], selection, verdict: { verdict: 'pass', blockers: [], advisories: [], dropped: [] } } }
  const runCritic = c =>
    role(`critic-${c}`, `${context(t, false, true)}\nReview this change through your lens, against the task's specs.${c === 'craft' ? '' : ' Skip style, naming and formatting: that is the craft lens.'} Return in ruledOut[] the plausible concerns you checked and dismissed, each with the code that dismisses it.\nChanged paths:\n${paths.join('\n')}\nToken economy: start from \`git -C ${t.worktree} diff ${range(r)} --stat\`, then read the diff one file at a time (\`git diff ${range(r)} -- <file>\`)${r.write.commit ? ` — this is ONE slice of a chain: the writer is already building the next slice in this worktree, so read files as of the slice (\`git show ${r.write.commit}:<path>\`), never the working tree` : ''} only for files your lens covers; read code with codegraph_explore or Read with offset/limit, never \`cat\` a whole file or dump the whole diff. Return findings only, no restated code.`,
      { schema: FINDINGS, label: `${c}:${t.id}`, phase: 'Review' })
      .then(rep => rep && { critic: c, ...rep })
  let reports = (await parallel(kept.map(c => () => runCritic(c)))).filter(Boolean)
  // A critic that failed did not run: it is never "zero findings". Retry it once; a mandatory one that still fails stops the task.
  let failed = kept.filter(c => !reports.some(x => x.critic === c))
  if (failed.length) {
    log(`${t.id}: critics failed, retrying once: ${failed.join(', ')}`)
    reports = [...reports, ...(await parallel(failed.map(c => () => runCritic(c)))).filter(Boolean)]
    failed = kept.filter(c => !reports.some(x => x.critic === c))
  }
  const failedMandatory = failed.filter(c => mandatory.includes(c))
  if (failedMandatory.length) return { ...r, profile, critics: kept, selection, criticsFailed: failed, stop: true, reason: `mandatory critic not run: ${failedMandatory.join(', ')}` }
  if (failed.length) log(`${t.id}: critics not run (failed twice): ${failed.join(', ')}`)
  const findings = reports.flatMap(rep => rep.findings.map(f => ({ critic: rep.critic, ...f })))
  const ruledOut = reports.flatMap(rep => (rep.ruledOut || []).map(x => ({ critic: rep.critic, ...x })))
  const proposals = [...(r.write.conventions || []), ...reports.flatMap(rep => (rep.conventions || []).map(c => ({ critic: rep.critic, ...c })))]
  const verdict = await role('forge-arbiter',
    `${context(t)}\nWriter's notes: ${r.write.notes || 'none'}\nJudge these findings (${findings.length}) from critics ${kept.filter(c => !failed.includes(c)).join(', ')}.${failed.length ? ` Critics that did NOT run (they failed; this is not "zero findings", say so in your verdict): ${failed.join(', ')}.` : ''} When a blocker needs a product or trade-off choice, return it as a decision instead of guessing. Keep every finding's original id: each one ends in exactly one of blockers, advisories or dropped (a merged duplicate goes to dropped with reason "duplicate of <id>"), so the run can measure each critic.\n${JSON.stringify(findings)}\nConcerns the critics ruled out (${ruledOut.length}): re-check only the shaky ones (no file:line, evidence that does not cover every caller, or that contradicts the code); revive a real one as a finding with id <lens>-r<n> and list it in revived[].\n${JSON.stringify(ruledOut)}\nUser suppressions (${SUPPRESSIONS.length}): drop a non-blocking finding that matches one, with reason "suppressed: <rule>"; never suppress a blocker.\n${JSON.stringify(SUPPRESSIONS)}\nConvention proposals (${proposals.length}): verify each against the code and the rule; return in conventions[] only the proven ones, duplicates merged.\n${JSON.stringify(proposals)}`,
    { schema: VERDICT, label: `arbiter:${t.id}`, phase: 'Review' })
  if (!verdict) return { ...r, profile, critics: kept, selection, criticsFailed: failed, stop: true, reason: 'arbiter returned nothing' }
  const stats = criticStats(kept, reports, verdict)
  // A decision the user already ruled on is not a stop: it becomes a blocker the correction round fixes. A ruling this
  // run's arbiter did not raise again still applies (critics are not deterministic).
  if (ruled.length) {
    const raised = (verdict.decisions || []).filter(d => d.blocking && ruled.includes(d.id))
    verdict.decisions = (verdict.decisions || []).filter(d => !raised.includes(d))
    verdict.blockers = [...(verdict.blockers || []), ...ruled.map(id => rulingItem(t.id, id, raised.find(d => d.id === id)))]
    verdict.verdict = 'fix'
  }
  if ((verdict.decisions || []).some(d => d.blocking)) return { ...r, profile, critics: kept, droppedCritics: dropped, verdict, stats, selection, criticsFailed: failed, stop: true, reason: 'decision needed' }
  return { ...r, profile, critics: kept, droppedCritics: dropped, verdict, stats, selection, criticsFailed: failed }
}

// What each critic's findings became after the arbiter: the data to prune or promote critics with, instead of guessing.
function criticStats(kept, reports, verdict) {
  const ids = xs => new Set((xs || []).map(x => String(x.id)))
  const blockers = ids(verdict.blockers), advisories = ids(verdict.advisories), dropped = ids(verdict.dropped)
  return kept.map(c => {
    const rep = reports.find(x => x.critic === c)
    const fs = (rep && rep.findings) || []
    const n = set => fs.filter(f => set.has(String(f.id))).length
    const revived = (verdict.revived || []).filter(id => String(id).startsWith(`${c}-r`)).length
    return { critic: c, returned: !!rep, findings: fs.length, blockers: n(blockers), advisories: n(advisories), dropped: n(dropped), unaccounted: fs.length - n(blockers) - n(advisories) - n(dropped), revived }
  })
}

// A fix is new code: one correctness pass over what it changed, looking only for defects the fix introduced (a broken
// caller, a lost case, a test weakened to pass). The arbiter verifies what it finds. There is no second correction
// round: a proven defect stops the task and reaches the user as a decision.
async function reviewFix(r, fixed, items, label, phase) {
  const t = r.task
  const scope = fixed.commit
    ? `the fix commit (\`git -C ${t.worktree} show ${fixed.commit}\`)`
    : `what the fix changed in ${(fixed.changedPaths || []).join(', ') || 'the worktree'} (\`git -C ${t.worktree} diff ${baseOf(t)} -- <file>\`, read against the items below)`
  const rep = await role('critic-correctness',
    `${context(t, false, true)}\nA ${label} just changed this task to fix the items below. Review ONLY ${scope}. Report only defects the fix itself introduced; never repeat the items.\nItems fixed:\n${JSON.stringify(items)}`,
    { schema: FINDINGS, label: `fix-review:${t.id}`, phase })
  if (!rep) return { ok: false, reason: 'fix review returned nothing' }
  const serious = (rep.findings || []).filter(f => !f.preexisting && (f.severity === 'blocker' || f.severity === 'major'))
  if (!serious.length) return { ok: true }
  const verdict = await role('forge-arbiter',
    `${context(t)}\nJudge these findings (${serious.length}) about a ${label}, which may have introduced them. Keep every finding's original id.\n${JSON.stringify(serious.map(f => ({ critic: 'correctness', ...f })))}\nConvention proposals (0): [].`,
    { schema: VERDICT, label: `fix-arbiter:${t.id}`, phase })
  if (!verdict) return { ok: false, reason: 'fix arbiter returned nothing' }
  return { ok: !(verdict.blockers || []).length, verdict }
}

// The correction is two stages. fixStage needs the worktree (a writer edits and commits); verifyStage only reads
// (recheck + fix review, by commit sha), so in a chain it runs alongside the next slice's writer instead of blocking it.
async function fixStage(r) {
  if (stopped(r) || r.writeOnly || MODE === 'qa') return r
  const t = r.task
  const chosen = MODE === 'review' ? [] : (r.verdict.advisories || []).filter(a => a.autoApply || applies(t, a))
  if (r.verdict.verdict === 'pass' && !chosen.length) return r
  const blockers = r.verdict.blockers || []
  const toFix = [...blockers, ...chosen]
  if (chosen.length) log(`${t.id}: applying ${chosen.length} advisory(ies) (auto or accepted): ${chosen.map(a => a.id).join(', ')}`)
  const fixed = await role('forge-writer',
    `${context(t, true)}\nCorrection round: fix every item below (blockers, plus advisories that are auto-applied or the user chose; each carries the critic's evidence), nothing else, then check with \`test-class.sh\` on the classes you touched and finish with \`gate.sh --rerun\` (writer rules 6 and 6-bis).${r.write.commit ? `\nChain slice: the findings are about slice ${r.write.commit}; later slices may already sit on top of it, keep them building. Commit the fix as \`git -C ${t.worktree} commit -am "fixup! ${t.commit}"\` (add new files first), so the orchestrator folds it into that slice, and return its sha in \`commit\`.` : ''}\n${JSON.stringify(toFix)}\n\n${surfaces(t)}`,
    { schema: WRITER, label: `correct:${t.id}`, phase: 'Correct', effort: writerEffort(t) })
  if (!fixed || fixed.status !== 'done') return { ...r, correction: fixed, stop: true, reason: 'correction not done' }
  // What the fix still needs: a recheck only when blockers were fixed; a fix review unless every item was an
  // autoApply advisory (by definition no behavior, contract, schema or security change).
  const needs = { recheck: blockers.length > 0, review: !toFix.every(a => a.autoApply) }
  if (!needs.recheck && !needs.review) log(`${t.id}: only autoApply advisories were applied; no recheck or fix review`)
  return { ...r, correction: fixed, toFix, needs }
}

async function verifyStage(r) {
  if (stopped(r) || !r.correction || !r.needs) return r
  const t = r.task
  const at = r.correction.commit ? ` Read the code as of the fix commit ${r.correction.commit} (\`git -C ${t.worktree} show ${r.correction.commit}:<path>\`): the worktree may already hold a later slice.` : ''
  let recheck
  if (r.needs.recheck) {
    const blockers = r.toFix.filter(a => !a.autoApply && (r.verdict.blockers || []).some(b => b.id === a.id))
    recheck = await role('forge-arbiter',
      `${context(t)}\nRecheck ONLY these items against the corrected code; add no new blockers.${at}\n${JSON.stringify(blockers)}`,
      { schema: RECHECK, label: `recheck:${t.id}`, phase: 'Correct' })
    const open = (recheck && recheck.results || []).filter(x => x.outcome === 'still_failing')
    if (!recheck || open.length) return { ...r, recheck, stop: true, reason: 'blockers still open' }
  }
  if (!r.needs.review) return { ...r, recheck }
  const check = await reviewFix(r, r.correction, r.toFix, 'correction round', 'Correct')
  if (!check.ok) return { ...r, recheck, fixReview: check.verdict, stop: true, reason: check.reason || 'correction introduced blockers' }
  return { ...r, recheck, fixReview: check.verdict }
}

async function correct(r) { return verifyStage(await fixStage(r)) }

async function qa(r) {
  if (stopped(r) || r.writeOnly) return r
  if (MODE === 'review') { log(`${r.task.id}: QA skipped (review mode)`); return { ...r, qaSkipped: true } }
  const t = r.task
  const runtime = (r.write.changedPaths || []).some(isRuntime)
  const profile = r.profile || t.profile || 'standard'
  const endpoint = (r.write.changedPaths || []).some(p => /\/Http\/|Endpoints\.cs$/.test(p))
  const wanted = t.qa === false ? false : t.qa === true || profile === 'deep' || r.write.contractChanged || endpoint
  if (!wanted || !runtime || profile === 'docs' || profile === 'tests') {
    log(`${t.id}: QA skipped (${!runtime ? 'no runtime change' : profile === 'docs' || profile === 'tests' ? profile + ' change' : 'not requested'})`)
    return { ...r, qaSkipped: true }
  }
  if (DEFER_QA) {
    log(`${t.id}: QA deferred: open the PRs, then run mode 'qa' while CI runs`)
    return { ...r, qaPending: { id: t.id, title: t.title, specs: t.specs, worktree: t.worktree, branch: t.branch, surfaces: t.surfaces, profile, contract: !!r.write.contractChanged, qa: true, changedPaths: r.write.changedPaths || [], slices: t.slices, journalOf: t.journalOf, brief: t.brief } }
  }
  const run = () => role('forge-qa',
    `${context(t)}\nExercise this change end to end on a slot. Writer's notes: ${r.write.notes || 'none'}\nCorrection applied before QA: ${r.correction ? (r.correction.notes || 'yes') + ' — ' + (r.correction.changedPaths || []).join(', ') : 'none'}\nArbiter advisories: ${JSON.stringify((r.verdict && r.verdict.advisories) || [])}\nChanged paths:\n${(r.write.changedPaths || []).join('\n')}`,
    { schema: QA, label: `qa:${t.id}`, phase: 'QA' })
  let result = await run()
  if (result && (result.decisions || []).some(d => d.blocking)) return { ...r, qa: result, stop: true, reason: 'decision needed' }
  if (result && result.status === 'fail' && result.defects.length) {
    const fixed = await role('forge-writer',
      `${context(t)}\nQA found these defects at runtime; fix them (test-first), then finish with \`gate.sh --rerun\` (writer rule 6-bis):\n${JSON.stringify(result.defects)}${t.slices ? `\nChain: commit each fix as \`fixup! <header>\` of the slice that introduced the defect, one of: ${t.slices.join(' | ')}.` : ''}\n\n${surfaces(t)}`,
      { schema: WRITER, label: `qa-fix:${t.id}`, phase: 'QA', effort: writerEffort(t) })
    if (fixed && fixed.status === 'done') {
      const check = await reviewFix(r, fixed, result.defects, 'QA fix', 'QA')
      if (!check.ok) return { ...r, qa: result, qaFix: fixed, fixReview: check.verdict, stop: true, reason: check.reason || 'QA fix introduced blockers' }
      r = { ...r, qaFix: fixed, fixReview: check.verdict }
      result = await run()
    }
  }
  const stop = !result || result.status !== 'pass'
  return { ...r, qa: result, stop, reason: stop ? `qa ${result ? result.status : 'missing'}` : undefined }
}

// One chain, one worktree. Iteration k runs in parallel: the writer of slice k, the critics of slice k-1 (they read
// by sha) and the verification of slice k-2's correction (also by sha). Only the correction writer of slice k-1 needs
// the worktree, so it runs after the writer of slice k. A failed review or verification stops the chain after the
// slice already being written. QA runs once, on the head, after every slice passed (or is deferred, see deferQa).
async function runChain(chain) {
  const slices = chain.map((t, i) => ({ ...t, chainIndex: i, chainOf: chain.length }))
  const done = []
  let reviewing = null
  let verifying = null
  for (const t of slices) {
    const [w, prev, verified] = await Promise.all([write(t), reviewing, verifying])
    verifying = null
    if (verified) {
      done.push(verified)
      // The review of the previous slice finished in the same step: keep it (verdict, decisions), never drop it.
      if (stopped(verified)) { if (prev) done.push(stopped(prev) ? prev : { ...prev, stop: true, reason: `waiting on ${verified.task.id}` }); done.push({ ...(w || { task: t }), stop: true, reason: `waiting on ${verified.task.id}` }); return done }
    }
    if (prev) {
      const f = await fixStage(prev)
      if (stopped(f)) { done.push(f, { ...(w || { task: t }), stop: true, reason: `waiting on ${prev.task.id}` }); return done }
      verifying = verifyStage(f)
    }
    if (stopped(w)) {
      if (verifying) done.push(await verifying)
      done.push(w)
      return done
    }
    reviewing = review(w)
  }
  const [lastReview, verified] = await Promise.all([reviewing, verifying])
  if (verified) {
    done.push(verified)
    if (stopped(verified)) { if (lastReview) done.push({ ...lastReview, stop: true, reason: `waiting on ${verified.task.id}` }); return done }
  }
  if (lastReview) {
    const c = await correct(lastReview)
    done.push(c)
    if (stopped(c)) return done
  }
  const last = done[done.length - 1]
  const head = {
    ...last,
    task: { ...last.task, id: `${last.task.id}-head`, title: `chain head of ${chain.map(t => t.id).join(', ')}`, qa: done.some(r => r.task.qa) ? true : undefined, slices: slices.map(t => t.commit), journalOf: slices.map(t => t.id), chainOf: undefined },
    write: { ...last.write, changedPaths: [...new Set(done.flatMap(r => r.write.changedPaths || []))], contractChanged: done.some(r => r.write.contractChanged) },
    profile: done.some(r => r.profile === 'deep') ? 'deep' : last.profile,
    // The head is not a new review: never count the last slice's critic stats or fix review twice.
    stats: undefined, fixReview: undefined, selection: undefined, criticsFailed: undefined, correction: undefined, recheck: undefined,
  }
  const q = await qa(head)
  if (!q.qaSkipped) done.push({ ...q, task: head.task })
  return done
}

const outcome = []
for (let i = 0; i < (A.waves || []).length; i++) {
  const wave = A.waves[i]
  log(`wave ${i + 1}: ${wave.map(t => t.id).join(', ')}`)
  const results = await pipeline(wave, write, review, correct, qa)
  results.forEach((r, k) => outcome.push(r || { task: wave[k], stop: true, reason: 'pipeline dropped the task' }))
  const blocked = results.filter(r => !r || r.stop)
  if (blocked.length) {
    log(`wave ${i + 1} stopped: ${blocked.map(r => r ? `${r.task.id} (${r.reason})` : 'unknown').join('; ')}`)
    break
  }
}
if ((A.chains || []).length && !outcome.some(r => r.stop)) {
  log(`chains: ${A.chains.map(c => c.map(t => t.id).join('>')).join(' | ')}`)
  const chains = await parallel(A.chains.map(c => () => runChain(c)))
  chains.forEach(c => outcome.push(...(c || [])))
}

// source tells the orchestrator where an answer goes: 'writer' → answers (the writer reruns), anything else → rulings
// (a fixup on the committed slice, nothing reruns).
const tag = (xs, source) => (xs || []).map(d => ({ ...d, source }))
const decisions = outcome.flatMap(r => [
  ...(r.fromDone ? [] : tag(r.write && r.write.decisions, 'writer')),
  ...tag(r.verdict && r.verdict.decisions, 'arbiter'),
  ...tag(r.fixReview && r.fixReview.decisions, 'arbiter'),
  ...tag(r.qa && r.qa.decisions, 'qa'),
].map(d => ({ feature: A.feature, task: r.task.id, ...d })))

// Per critic across the run: findings returned and what the arbiter made of them (precision = (blockers + advisories) / findings).
const runCriticStats = Object.values(outcome.flatMap(r => r.stats || []).reduce((acc, s) => {
  const a = acc[s.critic] || (acc[s.critic] = { critic: s.critic, runs: 0, silent: 0, findings: 0, blockers: 0, advisories: 0, dropped: 0, unaccounted: 0, revived: 0 })
  a.runs++; if (!s.returned) a.silent++
  for (const k of ['findings', 'blockers', 'advisories', 'dropped', 'unaccounted', 'revived']) a[k] += s[k] || 0
  return acc
}, {}))
const pending = decisions.filter(d => d.blocking)

return {
  status: outcome.some(r => r.stop) ? (pending.length ? 'needs_input' : 'needs_decision') : 'done',
  decisions: pending,
  takenDecisions: decisions.filter(d => !d.blocking),
  learnings: outcome.flatMap(r => [
    ...((r.write && r.write.learnings) || []),
    ...((r.verdict && r.verdict.learnings) || []),
    ...((r.qa && r.qa.learnings) || []),
  ].map(l => ({ task: r.task.id, learning: l }))),
  conventions: outcome.flatMap(r => ((r.verdict && r.verdict.conventions) || []).map(c => ({ task: r.task.id, ...c }))),
  criticStats: runCriticStats,
  friction: outcome.flatMap(r => [
    ...((r.write && r.write.friction) || []),
    ...((r.correction && r.correction.friction) || []),
    ...((r.qa && r.qa.friction) || []),
  ].map(f => ({ task: r.task.id, ...f }))),
  tasks: outcome.map(r => ({
    id: r.task.id,
    worktree: r.task.worktree,
    branch: r.task.branch,
    stopped: !!r.stop,
    reason: r.reason,
    profile: r.profile,
    critics: r.critics,
    qaSkipped: !!r.qaSkipped,
    qaPending: r.qaPending,
    droppedCritics: r.droppedCritics,
    criticsFailed: r.criticsFailed,
    selection: r.selection,
    write: r.write && { status: r.write.status, commit: r.write.commit, specsCovered: r.write.specsCovered, changedPaths: r.write.changedPaths, contractChanged: r.write.contractChanged, signals: r.write.signals, docsUpdated: r.write.docsUpdated, checks: r.write.checks, scratch: r.write.scratch },
    // Ready-made entry for args.done of the next fresh run: reviewed when the review and its correction finished, or when
    // it stopped only on a decision (the ruling the user gives becomes the correction).
    done: r.write && r.write.status === 'done' && r.write.commit && !r.task.id.endsWith('-head')
      ? {
        commit: r.write.commit, changedPaths: r.write.changedPaths || [], contractChanged: !!r.write.contractChanged, signals: r.write.signals,
        reviewed: !!r.verdict && (!r.stop || r.reason === 'decision needed' || /^waiting on /.test(r.reason || '')),
        // The arbiter's blockers and auto-applied advisories that no correction applied yet, so the next run fixes them.
        pendingFixes: r.verdict && !r.correction ? [...(r.verdict.blockers || []).filter(b => !String(b.id).startsWith('ruling-')), ...(r.verdict.advisories || []).filter(a => a.autoApply)] : undefined,
      }
      : undefined,
    correction: r.correction && { commit: r.correction.commit, status: r.correction.status },
    verdict: r.verdict && { verdict: r.verdict.verdict, blockers: r.verdict.blockers, advisories: r.verdict.advisories, followups: r.verdict.followups },
    recheck: r.recheck,
    fixReview: r.fixReview && { blockers: r.fixReview.blockers, advisories: r.fixReview.advisories },
    stats: r.stats,
    qa: r.qa && { status: r.qa.status, baseUrl: r.qa.baseUrl, scenarios: r.qa.scenarios, defects: r.qa.defects, brunoFiles: r.qa.brunoFiles },
  })),
}
