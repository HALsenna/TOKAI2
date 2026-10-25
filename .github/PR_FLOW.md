# Pull request flow

1. Branch from the latest `plan-main` using a `codex/` feature branch.
2. Create the implementation PR with base `plan-main`.
3. When the PR is merged, `promote-to-main.yml` creates a branch named `codex/promote-pr-<number>` at that PR's merge commit, then opens a corresponding PR with base `main`.
4. A human reviews and merges the `main` PR under the existing ruleset. The workflow never merges or approves PRs and never pushes to `main`.

The promotion branch is an immutable snapshot of `plan-main` at the merge. It works with merge, squash, and rebase merges and survives deletion of the original feature branch. The new PR links the source PR and preserves its title and description. Later merges into `plan-main` do not change earlier promotion PRs.

Promotion PRs include earlier `plan-main` changes that are still absent from `main`; they preserve dependencies rather than cherry-picking a potentially incomplete change. Review and merge them in source-merge order. GitHub may automatically mark a later PR merged if its entire snapshot is already reachable from `main`.

Retries reuse an existing promotion PR, including a closed or merged one, and do not overwrite its branch. If the snapshot has no commits missing from `main`, no PR is created. A closed promotion PR is intentionally not recreated; reopen it manually if needed.

## Repository settings

- Default branch: `plan-main`.
- GitHub Actions must be enabled, with **Allow GitHub Actions to create and approve pull requests** enabled. GitHub combines these capabilities in one setting; this workflow only creates PRs.
- The workflow explicitly requests `contents: write` and `pull-requests: write`; the repository's default workflow token permissions can remain read-only.
- Keep the existing `main` branch ruleset enabled.

## Recovery and manual promotion

In Actions, open **Promote merged PR to main**, choose **Run workflow** on `plan-main`, and enter the number of a merged PR whose base was `plan-main`. The workflow validates the source PR and uses its recorded merge SHA. Check failed runs if permissions or repository rules prevent PR creation. No personal access token is required.

Existing open PRs are not automatically retargeted by changing the default branch. Retarget them to `plan-main` when continuing their work.
