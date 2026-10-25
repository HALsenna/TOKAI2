# Repository instructions

## Branches and pull requests

- Start new work from the latest `origin/plan-main` on a `codex/` feature branch.
- Create all normal ChatGPT / Codex pull requests with **base `plan-main`**. Specify the base explicitly instead of relying on a client default.
- Do not push feature changes directly to `plan-main` or `main`.
- After a pull request merges into `plan-main`, GitHub Actions creates a corresponding pull request targeting `main`. These promotion PRs are the sole exception to the normal PR base rule.
- Never merge a pull request into `main`, enable auto-merge for it, or push directly to `main`. Leave promotion PRs for human review and merging. Preserve the existing `main` ruleset.
- The initial branch/workflow setup is a one-time bootstrap; subsequent changes follow this PR flow.

See `.github/PR_FLOW.md` for promotion behavior and recovery.
