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

## Codex review for main PRs

- Connect `HALsenna/TOKAI2` to ChatGPT Codex Connector and enable code review for this repository. Keep the review target on **Follow personal preferences** with personal automatic review off: the workflows request reviews explicitly, only for `main`.
- The promotion workflow calls `request-codex-review.yml` directly after creating or finding an open promotion PR. This also works for PRs created by `GITHUB_TOKEN`.
- The review workflow posts `@codex review` once per PR head commit. Retries reuse the request marker. Draft and closed PRs, and PRs targeting `plan-main`, are skipped. Mark a draft ready to request its review.
- After a human merges this workflow into `main`, its `pull_request_target` trigger also covers other main PRs and new pushes. The GitHub Actions event policy must permit this event. Before that merge, automatic requests are available through the promotion workflow only.
- The request job succeeds when it posts the request; review completion and findings appear separately in the PR. Check the Codex bot's review summary, including quota or connection errors. A missing marker comment can be retried by rerunning the jobs; for a failed Codex review after a request was posted, add `@codex review` manually.
- Codex uses the connected ChatGPT account's code-review entitlement. This setup adds no OpenAI API key or paid-usage opt-in. It never approves or merges PRs, and main's existing protection rules remain in effect.
