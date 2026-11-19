import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHECKS, decide, existing, matches, runEverything } from './changes.mjs';

const none = Object.fromEntries(Object.keys(CHECKS).map((check) => [check, false]));
const all = Object.fromEntries(Object.keys(CHECKS).map((check) => [check, true]));

test('globs: ** spans directories, * stays within one, dots are literal', () => {
  assert.equal(matches('src/**', 'src/Modules/Orders/Order.cs'), true);
  assert.equal(matches('**/*.md', 'README.md'), true);
  assert.equal(matches('**/*.md', 'tools/bruno/README.md'), true);
  assert.equal(matches('*.md', 'tools/notes.md'), false);
  assert.equal(matches('**/packages.lock.json', 'src/Hosts/Frappe.Api/packages.lock.json'), true);
  assert.equal(matches('global.json', 'globalXjson'), false);
  assert.equal(matches('deploy/Dockerfile', 'src/deploy/Dockerfile'), false);
});

test('a README-only change runs none of the heavy checks', () => {
  assert.deepEqual(decide(['README.md'], { complete: true }).checks, none);
});

test('documentation, the justfile, the lint configuration and repository settings run none of the heavy checks', () => {
  const files = ['LICENSE', '.justfile', 'eng/just/test.just',
    '.github/renovate.json5', 'eng/typos.toml', 'eng/frappe.markdownlint-cli2.jsonc', '.github/labeler.yml', '.github/labels.json',
    '.github/pull_request_template.md', '.github/rulesets/main.json', '.github/CONTRIBUTING.md'];
  assert.deepEqual(decide(files, { complete: true }).checks, none);
});

test('a Bruno request runs the collection against the stack and nothing else', () => {
  for (const file of ['tools/bruno/orders/place-order.yml', 'tools/bruno/environments/Stack.yml', 'tools/bruno/README.md']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...none, bruno: true }, file);
  }
});

test('a C# change runs the build, the tests and the image, but not the migrations or the contract diff', () => {
  assert.deepEqual(decide(['src/Modules/Orders/Frappe.Orders.Domain/Order.cs'], { complete: true }).checks,
    { ...all, contracts: false, migrations: false });
});

test('a migration, an entity configuration, a DbContext or the module database setup runs the migration check and the .NET checks', () => {
  for (const file of ['src/Modules/Orders/Frappe.Orders.Infrastructure/Persistence/Migrations/20260101000000_Init.cs',
    'src/Modules/Orders/Frappe.Orders.Infrastructure/Persistence/Migrations/OrdersDbContextModelSnapshot.cs',
    'src/Modules/Orders/Frappe.Orders.Infrastructure/Lines/LineConfiguration.cs',
    'src/Modules/Billing/Frappe.Billing.Infrastructure/Persistence/BillingDbContext.cs',
    'src/Platform/Frappe.Persistence/ModuleDatabaseRegistration.cs', 'src/Hosts/Frappe.Migrator/Program.cs']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...all, contracts: false }, file);
  }
});

test('code the Migrator runs against an existing database runs the migration check', () => {
  for (const file of ['src/Platform/Frappe.Persistence/ModuleRoles.cs',
    'src/Modules/Billing/Frappe.Billing.Infrastructure/Persistence/BillingMigration.cs']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...all, contracts: false }, file);
  }
});

test('a test anywhere under tests/, its fixtures, helpers and shared settings, runs the suites', () => {
  for (const file of ['tests/Modules/Orders/Frappe.Orders.UnitTests/Domain/OrderTests.cs',
    'tests/Modules/Orders/Frappe.Orders.IntegrationTests/Persistence/OrdersDatabase.cs',
    'tests/Modules/Orders/Frappe.Orders.UnitTests/Fixtures/orders-2026.json',
    'tests/Modules/Orders/Frappe.Orders.Testing/FakeClock.cs', 'tests/Platform/Frappe.Platform.UnitTests/Results/ResultTests.cs',
    'tests/Shared/Frappe.Testing/RepositoryRoot.cs', 'tests/Directory.Build.props']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...all, contracts: false, migrations: false }, file);
  }
});

test('a lock file or the SDK version is a .NET change', () => {
  for (const file of ['src/Hosts/Frappe.Api/packages.lock.json', 'global.json', 'Directory.Packages.props', 'Frappe.slnx',
    'NuGet.config', '.config/dotnet-tools.json', 'eng/CodeMetricsConfig.txt',
    'eng/analyzers.globalconfig', 'eng/BannedSymbols.txt', 'eng/BannedSymbols.Production.txt',
    'eng/scaffold/scaffold.sh', 'eng/templates/usecase/UseCaseName.cs']) {
    assert.equal(decide([file], { complete: true }).checks.dotnet, true, file);
  }
});

test('a committed schema dump runs the migrations check that compares it with the database', () => {
  for (const file of ['src/Modules/Orders/Frappe.Orders.Infrastructure/Persistence/schema.sql', 'src/Platform/Frappe.Persistence/schema.sql']) {
    // It also counts as a source file (it lives under src/), so the .NET checks run too: the globs cannot exclude it.
    assert.equal(decide([file], { complete: true }).checks.migrations, true, file);
  }
});

test('a package version change runs the migrations check, since a package can carry DDL', () => {
  assert.equal(decide(['Directory.Packages.props'], { complete: true }).checks.migrations, true);
});

test('a contract change runs the contract diff and the suites that keep it current', () => {
  assert.deepEqual(decide(['contracts/openapi/v1.json'], { complete: true }).checks,
    { ...none, contracts: true, tests: true });
});

test('a change to the REST lint rules runs the contract checks', () => {
  assert.equal(decide(['eng/spectral.yaml'], { complete: true }).checks.contracts, true);
});

test('a Dockerfile change runs the image, its dependencies and the migrations, not the .NET checks', () => {
  assert.deepEqual(decide(['deploy/Dockerfile'], { complete: true }).checks, { ...none, image: true, e2e: true, migrations: true, bruno: true });
});

test('the ignore file and lint rules of the image, and the compose stack, count as the container', () => {
  for (const file of ['deploy/Dockerfile.dockerignore', 'eng/hadolint.yaml', 'deploy/compose.yaml', 'deploy/.env.example']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...none, image: true, e2e: true, migrations: true, bruno: true }, file);
  }
});

test('the Grafana provisioning runs the suites that read it', () => {
  assert.deepEqual(decide(['deploy/observability/grafana/provisioning/alerting/orders-sync.json'], { complete: true }).checks,
    { ...none, tests: true });
});

test('the Squawk configuration runs the migration check', () => {
  assert.deepEqual(decide(['eng/squawk.toml'], { complete: true }).checks, { ...none, migrations: true });
});

test('a Markdown file inside the code counts as code', () => {
  assert.equal(decide(['src/Modules/Orders/README.md'], { complete: true }).checks.dotnet, true);
});

test('a file moved from src/ to tools/ counts at both paths', () => {
  // The caller passes a rename's previous_filename next to its filename.
  assert.equal(decide(['tools/Order.cs.md', 'src/Modules/Orders/Order.cs'], { complete: true }).checks.dotnet, true);
});

test('a change to the CI itself runs everything', () => {
  for (const file of ['.github/workflows/tests.yml', '.github/actions/image/action.yml', '.github/scripts/changes.mjs', '.github/requirements/diff-cover.txt']) {
    const result = decide([file], { complete: true });
    assert.deepEqual(result.checks, all, file);
    assert.match(result.reason, /CI/);
  }
});

test('a file in no category runs everything', () => {
  const result = decide(['README.md', 'scripts/new-tool.sh'], { complete: true });
  assert.deepEqual(result.checks, all);
  assert.match(result.reason, /scripts\/new-tool\.sh/);
});

test('an incomplete or empty file list runs everything', () => {
  assert.deepEqual(decide(['README.md'], { complete: false }).checks, all);
  assert.deepEqual(decide([], { complete: true }).checks, all);
});

const tracked = ['Frappe.slnx', 'src/Platform/Frappe.Results/Result.cs', 'tests/Frappe.Architecture.Tests/Frappe.Architecture.Tests.csproj'];

test('a group has a subject only when the repository holds one', () => {
  assert.deepEqual(existing(tracked), { ...none, dotnet: true, tests: true });
  assert.deepEqual(existing([...tracked, 'deploy/Dockerfile', 'contracts/openapi/v1.json', 'tools/bruno/README.md',
    'tests/Frappe.EndToEnd.Tests/Frappe.EndToEnd.Tests.csproj',
    'src/Modules/Orders/Frappe.Orders.Infrastructure/Migrations/20260101000000_Init.cs']), all);
});

test('a check whose subject does not exist is skipped even when its inputs changed', () => {
  const present = existing(tracked);
  assert.deepEqual(decide(['deploy/Dockerfile'], { complete: true, present }).checks, none);
  assert.deepEqual(decide(['contracts/openapi/v1.json'], { complete: true, present }).checks, { ...none, tests: true });
  const withImage = { ...present, image: true };
  assert.deepEqual(decide(['deploy/Dockerfile'], { complete: true, present: withImage }).checks, { ...none, image: true });
  assert.deepEqual(decide(['src/Platform/Frappe.Results/Result.cs'], { complete: true, present }).checks,
    { ...none, dotnet: true, tests: true });
});

test('running everything runs only what exists', () => {
  const present = existing(tracked);
  const expected = { ...none, dotnet: true, tests: true };
  assert.deepEqual(runEverything('not a pull request', present).checks, expected);
  assert.deepEqual(decide(['.github/workflows/tests.yml'], { complete: true, present }).checks, expected);
  assert.deepEqual(decide(['README.md', 'scripts/new-tool.sh'], { complete: true, present }).checks, expected);
  assert.deepEqual(decide(['README.md'], { complete: false, present }).checks, expected);
});

test('running everything names its reason', () => {
  assert.deepEqual(runEverything('not a pull request'), { checks: all, reason: 'not a pull request' });
});
