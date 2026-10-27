// Conventional Commits for every commit in a pull request.
export default {
  extends: ["@commitlint/config-conventional"],
  // Dependabot writes long, generated bodies; its subject line still follows the dependabot.yml prefix.
  ignores: [(message) => message.includes("Signed-off-by: dependabot[bot]")],
};
