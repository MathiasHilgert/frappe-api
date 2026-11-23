import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { test } from 'node:test';

// S14: the ruleset of main requires exactly the checks CI reports on a pull request, under the names the jobs have.
const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');

/** The id and `name:` of every job of a workflow file: a job starts at two spaces of indentation, its name at four. */
function jobs(file) {
  const lines = read(`../workflows/${file}`).split('\n');
  const found = [];
  let inJobs = false;
  for (const line of lines) {
    if (/^jobs:\s*$/.test(line)) {
      inJobs = true;
    } else if (inJobs && /^ {2}[a-z0-9-]+:\s*$/.test(line)) {
      found.push({ id: line.trim().slice(0, -1), name: undefined });
    } else if (inJobs && /^ {4}name: /.test(line) && found.length > 0 && found.at(-1).name === undefined) {
      found.at(-1).name = line.slice(10).trim();
    }
  }
  return found;
}

const required = JSON.parse(read('../rulesets/main.json')).rules
  .find((rule) => rule.type === 'required_status_checks').parameters.required_status_checks.map((check) => check.context);

// Jobs that report as a check but are not required: they carry no verdict of their own (detection, a matrix suite, the image).
const NOT_REQUIRED = [/^Detect what/, /^Unit tests \(/, /^Integration tests \(/, /^Image is built$/];

test('the ruleset requires exactly the pull request checks of ci.yml', () => {
  const reported = jobs('ci.yml').map((job) => job.name).filter((name) => !NOT_REQUIRED.some((pattern) => pattern.test(name)));
  assert.deepEqual([...reported].sort(), [...required].sort());
});

test('every required check is named once', () => {
  assert.equal(new Set(required).size, required.length);
});

test('the metadata workflow reports only checks CI reports, under the same names', () => {
  const ci = jobs('ci.yml').map((job) => job.name);
  const metadata = jobs('pull-request-metadata.yml').map((job) => job.name);
  assert.ok(metadata.length > 0);
  for (const name of metadata) {
    assert.ok(ci.includes(name) && required.includes(name), name);
  }
});

test('no other workflow reports a required name on a pull request', () => {
  for (const file of readdirSync(new URL('../workflows/', import.meta.url)).filter((name) => !['ci.yml', 'pull-request-metadata.yml'].includes(name))) {
    for (const job of jobs(file)) {
      assert.ok(!required.includes(job.name), `${file}: ${job.name}`);
    }
  }
});
