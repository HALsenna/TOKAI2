# Pull request flow

1. Branch from the latest `plan-main` using a `codex/` feature branch.
2. Create the implementation PR with base `plan-main`.
3. When the PR is merged, `promote-to-main.yml` creates a branch named `codex/promote-pr-<number>` at that PR's merge commit, then opens a corresponding PR with base `main`.
4. A human reviews and merges the `main` PR under the existing ruleset. The workflow never merges or approves PRs and never pushes to `main`.

The promotion branch is an immutable snapshot of `plan-main` at the merge. It works with merge, squash, and rebase merges and survives deletion of the original feature branch. The new PR links the source PR and preserves its title and description. Later merges into `plan-main` do not change earlier promotion PRs.

Promotion PRs include earlier `plan-main` changes that are still absent from `main`; they preserve dependencies rather than cherry-picking a potentially incomplete change. Review and merge them in source-merge order. GitHub may automatically mark a later PR merged if its entire snapshot is already reachable from `main`.

Retries reuse an existing promotion PR, including a closed or merged one, and do not overwrite its branch. If the snapshot has no commits missing from `main`, no PR is created. A closed promotion PR is intentionally not recreated; reopen it manually if needed.

## Repository settings

- Default branch: `main`. Normal implementation PRs explicitly target `plan-main`.
- GitHub Actions must be enabled, with **Allow GitHub Actions to create and approve pull requests** enabled. GitHub combines these capabilities in one setting; this workflow only creates PRs.
- The workflow explicitly requests `contents: write` and `pull-requests: write`; the repository's default workflow token permissions can remain read-only.
- Keep the existing `main` branch ruleset enabled.

## Recovery and manual promotion

For a failed promotion, rerun the failed Actions jobs. Once the promotion workflow is also present on the default branch, you can alternatively open **Promote merged PR to main**, choose **Run workflow** on `plan-main`, and enter the number of a merged PR whose base was `plan-main`. The workflow validates the source PR and uses its recorded merge SHA. Check failed runs if permissions or repository rules prevent PR creation. No personal access token is required.

Existing open PRs are not automatically retargeted by changing the default branch. Retarget them to `plan-main` when continuing their work.

## Standard Codex review

- Connect `HALsenna/TOKAI2` to ChatGPT Codex Connector. In its repository code-review settings, enable automatic review, choose **All pull requests**, and trigger reviews when a PR is opened. This includes both `plan-main` and `main` PRs. Do not enable automatic review globally for unrelated repositories.
- The managed Codex integration handles review requests. There is no custom review-request workflow, no `CODEX_REVIEW_TOKEN`, and no OpenAI API key. Comments from `github-actions[bot]` using `GITHUB_TOKEN` did not start Codex reviews during verification; use the managed integration instead.
- Existing PRs are not necessarily reviewed retroactively when this setting changes. To request an existing PR's review or retry one, a connected human account can comment `@codex review`.
- Review activity and completion appear in the Codex bot's PR summary. A successful promotion job means the PR was created, not that its review completed. Check quota or connection errors there.
- Codex uses the connected ChatGPT account's code-review entitlement. No paid-usage opt-in is added. Reviews do not replace tests or human review; no automatic approval or merge is configured, and main's protection rules remain in effect.
