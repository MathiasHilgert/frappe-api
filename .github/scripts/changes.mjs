// Which checks a pull request's changed files can affect. The "Detect what the changes affect" job of ci.yml asks, and a
// heavy check whose inputs did not change is skipped through its job's `if:`, which GitHub reports as passing. When in
// doubt, everything runs: on any event other than a pull request, when the file list is incomplete or empty, when the
// CI itself changes, and when a changed file is in no category below. The tests next to this file pin the mapping.

/** Where a changed file can have an effect. A file in no category is unknown, and an unknown file runs everything. */
export const CATEGORIES = {
  // The CI itself: a pull request that changes it runs everything, so it proves the CI it changes.
  ci: ['.github/workflows/**', '.github/actions/**', '.github/scripts/**', '.github/requirements/**'],
  // What the solution builds from: sources, tests, MSBuild, SDK and NuGet files, lock files and analyzer
  // settings.
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
  // *Migration.cs, the role/grant/purge SQL (Persistence), the scheduling provisioning, how new
  // migrations are linted, the committed schema dumps and the packages whose DDL they carry (Quartz).
  data: ['**/Migrations/**', '**/*Configuration.cs', '**/*DbContext.cs', '**/ModuleDatabase*', 'src/Hosts/Frappe.Migrator/**',
    '**/*Migration.cs', 'src/Platform/Frappe.Persistence/**', 'src/Platform/Frappe.Scheduling/**',
    'eng/squawk.toml', '**/schema.sql', 'Directory.Packages.props'],
  // The Bruno collection: its CI job runs it against the stack, so a change to it must run that job.
  bruno: ['tools/bruno/**'],
  // What the image's dependencies resolve from: lock files, package versions, SDK and NuGet sources. Also in dotnet.
  packages: ['**/packages.lock.json', 'Directory.Packages.props', 'global.json', 'NuGet.config'],
  // Read by no gated check: only by "Code is formatted" (spelling, Markdown, links), which always runs, or by no check.
  inert: ['**/*.md', 'LICENSE', 'eng/markdownlint-cli2.jsonc', 'eng/typos.toml',
    '.justfile', 'eng/just/**', '.github/renovate.json5', '.gitignore', '.github/labeler.yml', '.github/rulesets/**',
    '.github/release-please-config.json', '.github/release-please-manifest.json'],
};

/** Each gated group of checks, and the categories that can affect it (the "ci" category affects every one). */
export const CHECKS = {
  // Code is formatted (its dotnet format steps) · Code compiles without warnings · Complexity is within limits ·
  // Code has no known vulnerabilities
  dotnet: ['dotnet'],
  // Unit tests pass · Architecture rules hold · Integration tests pass · Changed lines are covered
  tests: ['dotnet', 'contracts', 'observability'],
  // App starts and responds (pushes to main only) · No vulnerable dependencies
  image: ['dotnet', 'container'],
  // Container image is clean (it scans the image: only its Dockerfile, compose stack or dependencies change what it finds)
  scan: ['container', 'packages'],
  // Database migrations are safe (it builds the image and upgrades a database: any other .NET change cannot affect it)
  migrations: ['data', 'container'],
  // API has no unmarked breaking changes · Events have no unmarked breaking changes
  contracts: ['contracts'],
  // Bruno collection passes against the stack (it needs the image, so whatever rebuilds the image runs it too)
  bruno: ['dotnet', 'container', 'bruno'],
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
 * Every gated check runs, for the given reason; `everything` tells the caller not to narrow anything down either.
 * @param {string} reason
 */
export function runEverything(reason) {
  return { checks: Object.fromEntries(Object.keys(CHECKS).map((check) => [check, true])), reason, everything: true };
}

/**
 * Decides which gated checks run for a pull request's changed files.
 * @param {string[]} files every changed path; for a rename, both the new and the previous path
 * @param {{ complete: boolean }} options complete is false when the list may be missing files
 */
export function decide(files, { complete }) {
  if (!complete) {
    return runEverything('the list of changed files is incomplete');
  }

  if (files.length === 0) {
    return runEverything('no changed files were listed');
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
    return runEverything('the pull request changes the CI');
  }

  if (unknown.length > 0) {
    return runEverything(`files in no category: ${unknown.join(', ')}`);
  }

  const checks = Object.fromEntries(
    Object.entries(CHECKS).map(([check, categories]) => [check, categories.some((category) => touched.has(category))]));
  return { checks, reason: `changed categories: ${[...touched].sort().join(', ')}` };
}
