// Which checks a pull request's changed files can affect. The workflows ask the "Changes" job (changes.yml), and a
// heavy check whose inputs did not change is skipped through its job's `if:`, which GitHub reports as passing. A check
// whose subject does not exist yet (no Dockerfile, no migrations, no contracts, no Bruno collection) is skipped the same
// way, so the ruleset can require it from the start. When in doubt, everything that exists runs: on any event other than
// a pull request, when the file list is incomplete or empty, when the CI itself changes, and when a changed file is in no
// category below. The tests next to this file pin the mapping.

/** Where a changed file can have an effect. A file in no category is unknown, and an unknown file runs everything. */
export const CATEGORIES = {
  // The CI itself: a pull request that changes it runs everything, so it proves the CI it changes.
  ci: ['.github/workflows/**', '.github/actions/**', '.github/scripts/**', '.github/requirements/**'],
  // What the solution builds from: sources, tests, MSBuild, SDK and NuGet files, lock files and analyzer settings.
  dotnet: ['src/**', 'tests/**', '**/*.cs', '**/*.csproj', '**/*.props', '**/*.targets', '**/*.slnx',
    '**/packages.lock.json', 'global.json', 'NuGet.config', '.config/dotnet-tools.json', '**/.editorconfig',
    'eng/CodeMetricsConfig.txt', 'eng/analyzers.globalconfig', 'eng/BannedSymbols*.txt',
    // just scaffold: ScaffoldTests (Architecture) runs the script and the templates.
    'eng/scaffold/**', 'eng/templates/**'],
  // Committed contracts: diffed against the base branch, and compared with the code by the suites.
  contracts: ['contracts/**', 'eng/spectral.yaml'],
  // The image and the compose stack the Migrator runs in.
  container: ['deploy/Dockerfile', 'deploy/Dockerfile.dockerignore', 'eng/hadolint.yaml', 'deploy/compose.yaml',
    'deploy/.env.example'],
  // Configuration of the LGTM stack.
  observability: ['deploy/observability/**'],
  // What can change the database schema, or is code the Migrator runs against an existing database: the migrations, the EF
  // model (entity configurations, DbContexts, the shared module database setup), the Migrator host, the per-module
  // *Migration.cs, the role/grant/purge SQL (Persistence), how new migrations are linted, the committed schema dumps and
  // the packages whose DDL they carry.
  data: ['**/Migrations/**', '**/*Configuration.cs', '**/*DbContext.cs', '**/ModuleDatabase*', 'src/Hosts/Frappe.Migrator/**',
    '**/*Migration.cs', 'src/Platform/Frappe.Persistence/**', 'eng/squawk.toml', '**/schema.sql', 'Directory.Packages.props'],
  // The Bruno collection: its CI job runs it against the stack, so a change to it must run that job.
  bruno: ['tools/bruno/**'],
  // Read by no gated check: only by "Code is formatted" (spelling, Markdown, links), which always runs, or by no check.
  inert: ['**/*.md', 'LICENSE', 'eng/frappe.markdownlint-cli2.jsonc', 'eng/typos.toml',
    '.justfile', 'eng/just/**', '.github/renovate.json5', '.gitignore', '.github/labeler.yml', '.github/labels.json', '.github/rulesets/**',
    'release-please-config.json', '.release-please-manifest.json'],
};

/** Each gated group of checks, and the categories that can affect it (the "ci" category affects every one). */
export const CHECKS = {
  // Code is formatted (its dotnet format steps) · Code compiles without warnings · Complexity is within limits ·
  // Code has no known vulnerabilities
  dotnet: ['dotnet'],
  // Unit tests pass · Architecture rules hold · Integration tests pass · Changed lines are covered
  tests: ['dotnet', 'contracts', 'observability'],
  // App starts and responds · Container image is clean · No vulnerable dependencies
  image: ['dotnet', 'container'],
  // App starts and responds (it needs the image and the end-to-end project)
  e2e: ['dotnet', 'container'],
  // Database migrations are safe (it builds the image and upgrades a database: any other .NET change cannot affect it)
  migrations: ['data', 'container'],
  // API has no unmarked breaking changes · Events have no unmarked breaking changes
  contracts: ['contracts'],
  // Bruno collection passes against the stack (it needs the image, so whatever rebuilds the image runs it too)
  bruno: ['dotnet', 'container', 'bruno'],
};

/**
 * The paths whose existence makes each group of checks meaningful. A group none of them matches has nothing to check
 * (no Dockerfile, no migration, no committed contract, no Bruno collection yet) and is skipped, never left pending.
 */
export const SUBJECTS = {
  dotnet: ['Frappe.slnx'],
  tests: ['tests/**/*.csproj'],
  image: ['deploy/Dockerfile'],
  e2e: ['tests/Frappe.EndToEnd.Tests/**'],
  migrations: ['**/Migrations/*.cs'],
  contracts: ['contracts/**'],
  bruno: ['tools/bruno/**'],
};

const compiled = new Map();

/**
 * Whether a path matches a glob: `**` spans directories (`**\/` also matches none), `*` and `?` stay within one.
 * @param {string} glob
 * @param {string} path
 */
export function matches(glob, path) {
  if (!compiled.has(glob)) {
    let source = '';
    for (let i = 0; i < glob.length; i++) {
      const char = glob[i];
      if (glob.startsWith('**/', i)) {
        source += '(?:.*/)?';
        i += 2;
      } else if (glob.startsWith('**', i)) {
        source += '.*';
        i += 1;
      } else if (char === '*') {
        source += '[^/]*';
      } else if (char === '?') {
        source += '[^/]';
      } else {
        source += char.replace(/[.+^${}()|[\]\\]/g, '\\$&');
      }
    }
    compiled.set(glob, new RegExp(`^${source}$`));
  }

  return compiled.get(glob).test(path);
}

/**
 * Which groups of checks have something to check, from every tracked path of the repository.
 * @param {string[]} tracked every file of the repository
 * @returns {Record<string, boolean>}
 */
export function existing(tracked) {
  return Object.fromEntries(Object.keys(CHECKS).map((check) => [check, tracked.some((file) => SUBJECTS[check].some((glob) => matches(glob, file)))]));
}

/**
 * Every gated check whose subject exists runs, for the given reason.
 * @param {string} reason
 * @param {Record<string, boolean>} [present] which groups have a subject; every group when omitted
 */
export function runEverything(reason, present = undefined) {
  return { checks: Object.fromEntries(Object.keys(CHECKS).map((check) => [check, present?.[check] ?? true])), reason };
}

/**
 * Decides which gated checks run for a pull request's changed files.
 * @param {string[]} files every changed path; for a rename, both the new and the previous path
 * @param {{ complete: boolean, present?: Record<string, boolean> }} options complete is false when the list may be
 *   missing files; present says which groups have a subject (every group when omitted)
 */
export function decide(files, { complete, present = undefined }) {
  if (!complete) {
    return runEverything('the list of changed files is incomplete', present);
  }

  if (files.length === 0) {
    return runEverything('no changed files were listed', present);
  }

  const touched = new Set();
  const unknown = [];
  for (const file of files) {
    const categories = Object.keys(CATEGORIES).filter((category) => CATEGORIES[category].some((glob) => matches(glob, file)));
    if (categories.length === 0) {
      unknown.push(file);
    }
    categories.forEach((category) => touched.add(category));
  }

  if (touched.has('ci')) {
    return runEverything('the pull request changes the CI', present);
  }

  if (unknown.length > 0) {
    return runEverything(`files in no category: ${unknown.join(', ')}`, present);
  }

  const checks = Object.fromEntries(
    Object.entries(CHECKS).map(([check, categories]) => [check, (present?.[check] ?? true) && categories.some((category) => touched.has(category))]));
  return { checks, reason: `changed categories: ${[...touched].sort().join(', ')}` };
}
