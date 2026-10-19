# Issue tracker: Linear

Issues and specs for this repo live in Linear, project **VR Fencing Trainer** (team **Personal**, key `PER`, project id `P-PER-9`). Use the Linear MCP tools (`mcp__claude_ai_Linear__*`) for all operations. Load them with ToolSearch first if they are deferred.

## Conventions

- **Create an issue**: `save_issue` with `team: "Personal"`, `project: "VR Fencing Trainer"`, a title, and a markdown description (real newlines, no `\n` escapes). New issues start in `Backlog` unless told otherwise.
- **Read an issue**: `get_issue` with the identifier (e.g. `PER-12`), then `list_comments` for the discussion.
- **List issues**: `list_issues` with `project: "VR Fencing Trainer"`, filtered by `label` and `state` as needed.
- **Comment on an issue**: `save_comment` with the issue id and a markdown body.
- **Apply / remove labels**: `save_issue` with the issue id and the full new `labels` list (read the current labels first, then add or drop).
- **Close**: `save_comment` with the closing note, then `save_issue` with `state: "Done"` (or `"Canceled"` when not actioned).

Workflow states in team Personal: `Backlog`, `Todo`, `In Progress`, `Done`, `Canceled`, `Duplicate`.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

## When a skill says "publish to the issue tracker"

Create a Linear issue in project VR Fencing Trainer.

## When a skill says "fetch the relevant ticket"

`get_issue` with the `PER-<n>` identifier, plus `list_comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body.
- **Child ticket**: a Linear sub-issue of the map (`save_issue` with `parentId` set to the map). Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: Linear's native "blocked by" relation on the child (`save_issue` with `blockedBy`). A ticket is unblocked when every blocker is `Done` or `Canceled`.
- **Frontier query**: list the map's open children, drop any with an open blocker or an assignee; first in map order wins.
- **Claim**: `save_issue` with `assignee: "me"`, the session's first write.
- **Resolve**: `save_comment` with the answer, then `save_issue` with `state: "Done"`, then append a context pointer (gist + link) to the map's Decisions-so-far.
