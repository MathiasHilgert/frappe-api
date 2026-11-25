// Which test suites a change can break, from the ProjectReference graph of the repository's .csproj files. A suite is
// affected when it contains a changed file or references, directly or transitively, a project that contains one. CI runs
// only the affected unit and integration suites on a pull request; the forge gate runs the same set
// (`node .github/scripts/affected.mjs <root> <changed path>...`). When in doubt, every suite runs: a change to a shared build
// file, or to a file the suites read that no project owns (a committed contract, the root .editorconfig).
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { CATEGORIES, CHECKS, matches } from './changes.mjs';

/** Files every project builds with: a change to one affects every suite. */
export const SHARED = /(^|\/)Directory\.(Build|Packages)\.(props|targets)$|^global\.json$|^NuGet\.config$|\.slnx$|^eng\//;

/** The forge gate always runs the architecture suite; CI runs it in its own job. */
export const ALWAYS = 'tests/Frappe.Architecture.Tests';

const SKIPPED = new Set(['bin', 'obj', 'node_modules', 'TestResults']);

/**
 * Every project directory (relative, with forward slashes) and the project directories it references.
 * @param {string} root the repository root
 * @returns {Map<string, Set<string>>}
 */
export function loadProjects(root) {
  const projects = new Map();
  const toRelative = (path) => relative(root, path).split(sep).join('/');
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        if (!SKIPPED.has(entry.name) && !entry.name.startsWith('.')) {
          walk(join(dir, entry.name));
        }
      } else if (entry.name.endsWith('.csproj')) {
        const file = join(dir, entry.name);
        const references = new Set();
        for (const [, include] of readFileSync(file, 'utf8').matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
          const target = toRelative(dirname(resolve(dir, include.replaceAll('\\', '/'))));
          if (!target.startsWith('..')) {
            references.add(target);
          }
        }
        projects.set(toRelative(dir), references);
      }
    }
  };
  walk(root);
  return projects;
}

/**
 * The runnable test suites: projects under tests/ named *Tests (helper libraries such as Frappe.Testing hold no tests).
 * @param {Map<string, Set<string>>} projects
 */
export function suites(projects) {
  return [...projects.keys()].filter((project) => project.startsWith('tests/') && project.endsWith('Tests')).sort();
}

/**
 * The project that contains a file (the deepest project directory above it), or undefined.
 * @param {Map<string, Set<string>>} projects
 * @param {string} file
 */
export function owner(projects, file) {
  let best;
  for (const project of projects.keys()) {
    if ((file === project || file.startsWith(`${project}/`)) && (best === undefined || project.length > best.length)) {
      best = project;
    }
  }
  return best;
}

/**
 * The suites the changed files can break, or null when a shared build file changed (every suite).
 * @param {Map<string, Set<string>>} projects
 * @param {string[]} files
 */
export function affected(projects, files) {
  if (files.some((file) => SHARED.test(file))) {
    return null;
  }

  const touched = new Set(files.map((file) => owner(projects, file)).filter(Boolean));
  return suites(projects).filter((suite) => {
    const seen = new Set();
    const stack = [suite];
    while (stack.length > 0) {
      const current = stack.pop();
      if (!seen.has(current)) {
        seen.add(current);
        stack.push(...(projects.get(current) ?? []));
      }
    }
    return [...seen].some((project) => touched.has(project));
  });
}

/**
 * The suites CI runs for a pull request's changed files: '*' for every suite, or the affected ones. Besides a shared build
 * file, a file the suites read that no project owns (a committed contract, the root .editorconfig) runs every suite.
 * @param {Map<string, Set<string>>} projects
 * @param {string[]} files
 * @returns {'*' | string[]}
 */
export function scope(projects, files) {
  const read = CHECKS.tests.flatMap((category) => CATEGORIES[category]);
  const unowned = files.some((file) => owner(projects, file) === undefined && read.some((glob) => matches(glob, file)));
  const selected = affected(projects, files);
  return selected === null || unowned ? '*' : selected;
}

// The forge gate's interface: prints one suite directory per line, the architecture suite always included.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [root, ...files] = process.argv.slice(2);
  const projects = loadProjects(resolve(root));
  const selected = affected(projects, files.filter(Boolean)) ?? suites(projects);
  const lines = new Set(selected);
  if (projects.has(ALWAYS)) {
    lines.add(ALWAYS);
  }
  process.stdout.write([...lines].sort().join('\n') + (lines.size > 0 ? '\n' : ''));
}
