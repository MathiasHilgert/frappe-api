import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHECKS, decide, matches, runEverything } from './changes.mjs';

const none = Object.fromEntries(Object.keys(CHECKS).map((check) => [check, false]));
const all = Object.fromEntries(Object.keys(CHECKS).map((check) => [check, true]));

test('globs: ** spans directories, * stays within one, dots are literal', () => {
  assert.equal(matches('src/**', 'src/Modules/Geo/Holiday.cs'), true);
  assert.equal(matches('**/*.md', 'README.md'), true);
  assert.equal(matches('**/*.md', 'docs/adr/0001-own-messaging-over-cap.md'), true);
  assert.equal(matches('*.md', 'docs/notes.md'), false);
  assert.equal(matches('**/packages.lock.json', 'src/Hosts/Frappe.Api/packages.lock.json'), true);
  assert.equal(matches('global.json', 'globalXjson'), false);
  assert.equal(matches('deploy/Dockerfile', 'src/deploy/Dockerfile'), false);
});

test('a README-only change runs none of the heavy checks', () => {
  assert.deepEqual(decide(['README.md'], { complete: true }).checks, none);
});

test('documentation, the justfile, the lint configuration and repository settings run none of the heavy checks', () => {
  const files = ['docs/adr/0001-own-messaging-over-cap.md', 'LICENSE', '.justfile', 'eng/just/test.just',
    '.github/renovate.json5', 'eng/typos.toml', 'eng/markdownlint-cli2.jsonc', '.github/labeler.yml',
    '.github/pull_request_template.md', '.github/rulesets/main.json', '.github/CONTRIBUTING.md',
    'tools/bruno/geo/countries/get-country.yml'];
  assert.deepEqual(decide(files, { complete: true }).checks, none);
});

test('a C# change runs the build, the tests, the image and the migrations, but not the contract diff', () => {
  assert.deepEqual(decide(['src/Modules/Geo/Frappe.Geo.Domain/Holiday.cs'], { complete: true }).checks,
    { ...all, contracts: false });
});

test('a test anywhere under tests/, its fixtures, helpers and shared settings, runs the suites', () => {
  for (const file of ['tests/Modules/Geo/Frappe.Geo.UnitTests/Domain/HolidayCalendarTests.cs',
    'tests/Modules/Geo/Frappe.Geo.IntegrationTests/Persistence/GeoDatabase.cs',
    'tests/Modules/Geo/Frappe.Geo.UnitTests/Fixtures/argentinadatos-feriados-2026.json',
    'tests/Modules/Geo/Frappe.Geo.Testing/FakeHolidaySource.cs', 'tests/Platform/Frappe.Platform.Tests/Results/ResultTests.cs',
    'tests/Shared/Frappe.Testing/RepositoryRoot.cs', 'tests/Directory.Build.props']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...all, contracts: false }, file);
  }
});

test('a lock file or the SDK version is a .NET change', () => {
  for (const file of ['src/Hosts/Frappe.Api/packages.lock.json', 'global.json', 'Directory.Packages.props', 'Frappe.slnx',
    'NuGet.config', '.config/dotnet-tools.json', 'eng/CodeMetricsConfig.txt', 'tools/seeds/cldr-seed.cs']) {
    assert.equal(decide([file], { complete: true }).checks.dotnet, true, file);
  }
});

test('a contract change runs the contract diff and the suites that keep it current', () => {
  assert.deepEqual(decide(['docs/api/openapi/v1.json'], { complete: true }).checks,
    { ...none, contracts: true, tests: true });
});

test('a Dockerfile change runs the image, its dependencies and the migrations, not the .NET checks', () => {
  assert.deepEqual(decide(['deploy/Dockerfile'], { complete: true }).checks, { ...none, image: true, migrations: true });
});

test('the ignore file and lint rules of the image, and the compose stack, count as the container', () => {
  for (const file of ['deploy/Dockerfile.dockerignore', 'eng/hadolint.yaml', 'deploy/compose.yaml', 'deploy/.env.example']) {
    assert.deepEqual(decide([file], { complete: true }).checks, { ...none, image: true, migrations: true }, file);
  }
});

test('the Grafana provisioning runs the suites that read it', () => {
  assert.deepEqual(decide(['deploy/observability/grafana/provisioning/alerting/geo-sync.json'], { complete: true }).checks,
    { ...none, tests: true });
});

test('the Squawk configuration runs the migration check', () => {
  assert.deepEqual(decide(['eng/squawk.toml'], { complete: true }).checks, { ...none, migrations: true });
});

test('a Markdown file inside the code counts as code', () => {
  assert.equal(decide(['src/Modules/Geo/README.md'], { complete: true }).checks.dotnet, true);
});

test('a file moved from src/ to docs/ counts at both paths', () => {
  // The caller passes a rename's previous_filename next to its filename.
  assert.equal(decide(['docs/Holiday.cs.md', 'src/Modules/Geo/Holiday.cs'], { complete: true }).checks.dotnet, true);
});

test('a change to the CI itself runs everything', () => {
  for (const file of ['.github/workflows/tests.yml', '.github/scripts/changes.mjs', '.github/requirements/diff-cover.txt']) {
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

test('running everything names its reason', () => {
  assert.deepEqual(runEverything('not a pull request'), { checks: all, reason: 'not a pull request' });
});
