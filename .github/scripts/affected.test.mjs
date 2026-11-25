import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { affected, loadProjects, owner, scope } from './affected.mjs';

// A small repository: a platform library, two modules, a host that references both, and their suites.
const root = mkdtempSync(join(tmpdir(), 'affected-'));
after(() => rmSync(root, { recursive: true, force: true }));
const project = (dir, ...references) => {
  mkdirSync(join(root, dir), { recursive: true });
  const items = references.map((reference) => `<ProjectReference Include="${reference}" />`).join('');
  writeFileSync(join(root, dir, `${dir.split('/').at(-1)}.csproj`), `<Project><ItemGroup>${items}</ItemGroup></Project>`);
};
project('src/Platform/Frappe.Results');
project('src/Modules/Orders/Frappe.Orders.Domain', '../../../Platform/Frappe.Results/Frappe.Results.csproj');
project('src/Modules/Menu/Frappe.Menu.Domain', '..\\..\\..\\Platform\\Frappe.Results\\Frappe.Results.csproj');
project('src/Hosts/Frappe.Api', '../../Modules/Orders/Frappe.Orders.Domain/Frappe.Orders.Domain.csproj',
  '../../Modules/Menu/Frappe.Menu.Domain/Frappe.Menu.Domain.csproj');
project('tests/Shared/Frappe.Testing');
project('tests/Modules/Orders/Frappe.Orders.UnitTests', '../../../../src/Modules/Orders/Frappe.Orders.Domain/Frappe.Orders.Domain.csproj',
  '../../../Shared/Frappe.Testing/Frappe.Testing.csproj');
project('tests/Modules/Menu/Frappe.Menu.UnitTests', '../../../../src/Modules/Menu/Frappe.Menu.Domain/Frappe.Menu.Domain.csproj');
project('tests/Hosts/Frappe.Api.IntegrationTests', '../../../src/Hosts/Frappe.Api/Frappe.Api.csproj');
project('tests/Frappe.Architecture.Tests');
project('.claude/worktrees/copy/src/Ignored');
const projects = loadProjects(root);

const ORDERS = 'tests/Modules/Orders/Frappe.Orders.UnitTests';
const MENU = 'tests/Modules/Menu/Frappe.Menu.UnitTests';
const API = 'tests/Hosts/Frappe.Api.IntegrationTests';

test('a module change runs its own suites and the suites of what references it, not the other modules', () => {
  assert.deepEqual(affected(projects, ['src/Modules/Orders/Frappe.Orders.Domain/Order.cs']), [API, ORDERS]);
});

test('a platform change runs every suite that reaches it, also through Windows-style references', () => {
  assert.deepEqual(affected(projects, ['src/Platform/Frappe.Results/Result.cs']), [API, MENU, ORDERS]);
});

test('a test change runs only that suite, and a shared test helper runs the suites that use it', () => {
  assert.deepEqual(affected(projects, [`${MENU}/MenuTests.cs`]), [MENU]);
  assert.deepEqual(affected(projects, ['tests/Shared/Frappe.Testing/Clock.cs']), [ORDERS]);
});

test('a shared build file means every suite', () => {
  for (const file of ['Directory.Packages.props', 'src/Directory.Build.props', 'global.json', 'Frappe.slnx', 'eng/analyzers.globalconfig']) {
    assert.equal(affected(projects, [file]), null, file);
  }
});

test('the owner is the deepest project above the file, and hidden folders such as worktrees are not projects', () => {
  assert.equal(owner(projects, 'src/Modules/Orders/Frappe.Orders.Domain/Sub/Order.cs'), 'src/Modules/Orders/Frappe.Orders.Domain');
  assert.equal(owner(projects, 'README.md'), undefined);
  assert.equal(projects.has('.claude/worktrees/copy/src/Ignored'), false);
});

test('CI runs every suite for a file the suites read that no project owns, and the affected ones otherwise', () => {
  assert.equal(scope(projects, ['contracts/openapi/v1.json']), '*');
  assert.equal(scope(projects, ['.editorconfig']), '*');
  assert.equal(scope(projects, ['Directory.Packages.props']), '*');
  assert.deepEqual(scope(projects, ['src/Modules/Menu/Frappe.Menu.Domain/Dish.cs', 'docs/menu.md']), [API, MENU]);
  assert.deepEqual(scope(projects, ['tools/bruno/menu/list.yml']), []);
});

test('the forge gate interface prints one suite per line, the architecture suite always included', () => {
  const script = join(dirname(fileURLToPath(import.meta.url)), 'affected.mjs');
  const run = (...files) => execFileSync('node', [script, root, ...files], { encoding: 'utf8' });
  assert.equal(run(`${MENU}/MenuTests.cs`), `tests/Frappe.Architecture.Tests\n${MENU}\n`);
  assert.equal(run('global.json').trim().split('\n').length, 4);
});
