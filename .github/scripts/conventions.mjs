// The repository's naming conventions, in one place: the pull request title, every commit message and the branch
// name are checked against these rules by the "Pull request" workflow, and the tests next to this file pin them.

/** Conventional Commits types this repository accepts. */
export const TYPES = ['feat', 'fix', 'refactor', 'perf', 'test', 'docs', 'style', 'build', 'ci', 'chore', 'revert'];

const MAX_HEADER = 100;

// <type>(<scope>)!: <summary>; the scope is mandatory, lower-case kebab-case.
const HEADER = new RegExp(`^(?<type>${TYPES.join('|')})\\((?<scope>[a-z][a-z0-9-]*)\\)(?<breaking>!)?: (?<summary>.+)$`);

// Branches created by tools, which name them themselves.
const TOOL_BRANCH = /^(renovate|dependabot)\/|^release-please--/;
const BRANCH = new RegExp(`^(?<type>${TYPES.join('|')})\\/[a-z0-9]+(-[a-z0-9]+)*$`);

// Traces an AI assistant leaves in a message: co-author trailers, "generated with" footers, session links.
const AI_ATTRIBUTION = [
  /co-authored-by:[^\n]*(claude|anthropic|openai|chatgpt|gpt-\d|copilot|gemini|cursor|codex|devin|aider|windsurf)/i,
  /co-authored-by:[^\n]*noreply@(anthropic|openai)\.com/i,
  /generated (with|by) [^\n]*(claude|chatgpt|copilot|gemini|cursor|codex|\bai\b)/i,
  /claude\.(ai|com)\/code/i,
  /claude-session:/i,
  /\u{1F916}/u,
];

/**
 * Checks a Conventional Commits header. Returns the parsed parts, and the problems (empty when valid).
 * @param {string} header
 */
export function checkHeader(header) {
  const problems = [];
  const match = HEADER.exec(header);
  if (!match) {
    problems.push(
      `"${header}" is not "<type>(<scope>): <summary>": type one of ${TYPES.join(', ')}, ` +
        'scope mandatory and lower-case (e.g. geo, platform, ci), "!" before ":" for a breaking change');
    return { problems };
  }

  const { type, scope, breaking, summary } = match.groups;
  if (header.length > MAX_HEADER) {
    problems.push(`the header is ${header.length} characters; keep it at ${MAX_HEADER} or fewer`);
  }

  // "Add x" is refused; acronyms and names written as such (OpenAPI, HEAD, EF Core) are not.
  if (/^[A-Z][a-z]+(\s|$)/.test(summary)) {
    problems.push('the summary starts with a lower-case letter');
  }

  if (summary.endsWith('.')) {
    problems.push('the summary has no trailing period');
  }

  return { type, scope, breaking: breaking === '!', summary, problems };
}

/**
 * Lines of a text that look like an AI assistant's attribution.
 * @param {string} text
 */
export function findAiAttribution(text) {
  return (text ?? '')
    .split('\n')
    .filter((line) => AI_ATTRIBUTION.some((pattern) => pattern.test(line)))
    .map((line) => line.trim());
}

/**
 * Checks a full commit message: a valid header and no AI attribution anywhere.
 * @param {string} message
 */
export function checkCommitMessage(message) {
  const [header] = message.split('\n');
  const { problems } = checkHeader(header);
  for (const line of findAiAttribution(message)) {
    problems.push(`AI attribution is not allowed: "${line}"`);
  }

  return problems;
}

/**
 * Checks a branch name: <type>/<kebab-case-topic>, with the type of the pull request title. Tool branches pass.
 * @param {string} branch
 * @param {string | undefined} titleType the type parsed from the pull request title, when it parsed
 */
export function checkBranch(branch, titleType) {
  if (TOOL_BRANCH.test(branch)) {
    return [];
  }

  const match = BRANCH.exec(branch);
  if (!match) {
    return [`"${branch}" is not "<type>/<topic>": type one of ${TYPES.join(', ')}, topic in lower-case kebab-case (e.g. feat/geo-holidays)`];
  }

  if (titleType && match.groups.type !== titleType) {
    return [`the branch says "${match.groups.type}" but the title says "${titleType}"; use the same type in both`];
  }

  return [];
}
