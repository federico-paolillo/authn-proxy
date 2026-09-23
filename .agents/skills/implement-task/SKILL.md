---
name: implement-task
description: Execute a direct coding task or Markdown task queue through planning, implementation, independent review, bookkeeping, commit, and push.
---

# Implement Task

Use this skill to execute either one task supplied directly in the prompt or a
queue supplied in a referenced Markdown task file, including `/goal TODO.md`.

## Inputs and Task Selection

1. Record the active branch when the workflow starts. Stay on that branch and
   never switch branches implicitly.
2. Treat the direct prompt or selected queue item, applicable `AGENTS.md`
   instructions, and referenced durable requirements such as a `SEED` as the
   authoritative inputs. Stop for user direction if they materially contradict
   one another.
3. A direct prompt is one task unless it identifies a Markdown file as the task
   queue. Derive a concise imperative commit title from a direct task.
4. In a task queue, tasks are unchecked top-level Markdown checkboxes in
   document order. A task includes its nested description, acceptance criteria,
   and other content up to the next top-level checkbox. Checked top-level
   checkboxes and nested checkboxes are not separate tasks.
5. Before selecting a queue item, check whether an identifiable workflow commit
   from an already completed item is not on the branch's upstream. Push it
   first. Never push an unrelated or ambiguously attributable commit; report a
   blocker instead.

If the referenced task file has no unchecked top-level checkbox, finish only
when every tracked task is checked and its workflow commit is pushed. An empty
or malformed queue that cannot establish this state is a blocker.

## Per-Task Workflow

For each selected task:

1. Inspect the worktree and preserve all pre-existing unrelated changes. Stop if
   overlapping edits cannot be isolated safely.
2. Derive the task's stable ExecPlan path under `.agents/plans/`. For a queue
   item, use its complete top-level checkbox label, including its identifier
   when present; for a direct task, use its derived imperative commit title.
   Lowercase the source, replace each run outside `a-z` and `0-9` with one
   hyphen, and trim leading or trailing hyphens. Reuse the existing path when
   resuming the same task.
3. Start a fresh session using the configured `planner` agent profile through
   the best available monitorable orchestration mechanism. Give it the complete
   selected task, authoritative inputs, relevant constraints, ExecPlan path, and
   expected `PLAN_READY` handoff.
4. Review the Planner's ExecPlan and handoff. Approve planning only when the
   file exists, conforms to `.agents/docs/PLANS.md`, and matches the task's
   scope, requirements, invariants, risks, and intended validation. Return
   bounded revisions to the same Planner session.
5. Cap planning at five rounds unless the user explicitly authorizes another
   cap.
6. After plan approval, start a fresh session using the configured `worker`
   agent profile. Give it the approved ExecPlan and authorize implementation and
   validation.
7. When the Worker returns `WORK_READY`, start a fresh session using the
   configured `reviewer` agent profile for independent verification of the task,
   implementation, validation, and ExecPlan accuracy; require `REVIEW_READY`.
8. If the Reviewer reports findings, return them to the same Worker for
   remediation, then send the resulting `WORK_READY` handoff to the same
   Reviewer.
9. Repeat remediation and review until the Reviewer approves the task or five
   remediation cycles have completed, unless the user explicitly authorizes
   another cap.

Only after the Reviewer returns both `APPROVED` and `REVIEW_READY`, the
Coordinator may update the task's ExecPlan solely to record the review result
and evidence, close review-related Progress items, finalize Outcomes &
Retrospective, and append the revision note required by `.agents/docs/PLANS.md`.
This bookkeeping authority does not permit changes to implementation scope,
technical decisions, validation claims, authoritative requirements, or
implementation files. Any non-bookkeeping change returns to the same Worker and
Reviewer. A `CHANGES_REQUESTED` handoff follows the remediation flow above and
does not authorize finalization.

If the workflow stops on `BLOCKED: <reason>` or a push failure, the Coordinator
may update the ExecPlan solely to record the factual stop reason and evidence,
current progress, required next decision or prerequisite, and the revision note
required by `.agents/docs/PLANS.md`, but only when that update can be isolated
safely. This stopped-state bookkeeping does not imply approval or completion and
does not permit changes to implementation scope, technical decisions, validation
claims, authoritative requirements, or implementation files. If the bookkeeping
cannot be isolated, leave the ExecPlan unchanged and report the blocker through
the existing stopped-workflow path.

Do not select a later queue item until the current task is approved, recorded,
committed, and pushed. Stop on its first blocker or exhausted cap and do not
skip ahead. If this happens before Reviewer approval, leave the item unchecked.

## Remediation Cycle Accounting

- The initial implementation and first review establish the finding baseline and
  do not consume a remediation cycle.
- A remediation cycle begins when review findings are returned to the Worker and
  ends when the Reviewer reports on the resulting validated work.
- Worker retries needed to reach `WORK_READY` remain part of the same
  remediation cycle.
- Do not maintain separate Worker and Reviewer counters.

## Design Checkpoint

When remediation would introduce or replace a cross-boundary ownership,
concurrency, or authorization protocol:

1. Ask the same Reviewer for a read-only design checkpoint before authorizing
   implementation.
2. Provide the proposed invariants and material event orderings.
3. Require `DESIGN_READY` with approval or bounded findings.
4. The Reviewer must not edit files or choose the implementation.
5. This checkpoint does not consume a remediation cycle.

## Orchestration Discipline

- Run coordination using the configured `coordinator` agent profile and select
  the configured `planner`, `worker`, and `reviewer` profiles by exact name. Do
  not suggest or pass model or reasoning-effort overrides when starting them;
  each profile owns those settings.
- Give each role session its complete task, constraints, and expected handoff
  marker up front.
- After starting work or receiving the role's last message, wait at least 20
  uninterrupted minutes before sending a follow-up, interrupt, status request,
  or nudge.
- The quiet period may end early only when the selected orchestration mechanism
  reports `blocked`, `waiting`, or `error`; the terminal is visibly awaiting
  input; the role asks a question; or the user redirects the task.
- After 20 minutes without a handoff, inspect the selected orchestration
  mechanism's state and recent unwrapped output. Send at most one concise status
  request or explicitly grant another 20-minute quiet period.
- Never interrupt merely because no output or file change is visible.
- Treat the explicit marker in the role transcript as the handoff source of
  truth. A `done`, `idle`, or `unknown` orchestration state is not itself a
  completed handoff.

## Completion Signals

- Planner handoffs end with exactly `PLAN_READY` or `BLOCKED: <reason>` on the
  final line.
- Worker handoffs end with exactly `WORK_READY` or `BLOCKED: <reason>` on the
  final line.
- Reviewer payloads state exactly `APPROVED` or `CHANGES_REQUESTED` and end with
  exactly `REVIEW_READY`, `DESIGN_READY`, or `BLOCKED: <reason>` on the final
  line.
- The Coordinator ends with exactly `SESSION_COMPLETE` on the final line only
  after approval, required bookkeeping, commit, push, and the Session Overview.
  If the workflow stops before completion, it ends with exactly
  `BLOCKED: <reason>` on the final line.

## Completion, Commit, and Caps

- Stop after five remediation cycles even if findings remain unless the user
  explicitly authorized a different cap. Report unresolved work precisely.
- A finding may be recorded as accepted debt only when deferring it does not
  contradict an authoritative requirement or a required security, data-loss,
  atomicity, cancellation, reset, concurrency, or trust-boundary guarantee.
- `DEBT.md` records an eligible deferral; it does not waive product
  requirements. A finding that prevents Reviewer approval remains blocking until
  fixed or the authoritative requirements are explicitly amended.
- Do not mark a tracked task complete unless the Reviewer approved it.
- After approval, the Coordinator alone checks the selected task when it came
  from a queue, performs the permitted ExecPlan finalization, stages only that
  task's changes and tracker update, verifies the staged diff excludes
  pre-existing unrelated changes, includes the task's ExecPlan, creates one
  commit, and pushes it before selecting another task. Use the queue item title
  as the tracked task's commit title and the derived imperative title for a
  direct task.
- Stop if task-related and unrelated changes cannot be staged separately or if
  the branch cannot be pushed safely to its configured upstream.
- If a workflow commit is created but cannot be pushed, retain it, report the
  blocker, and select no later task. A resumed workflow must push that
  identifiable commit before task selection.
- A direct task finishes only after its approved commit is pushed. A queue
  finishes only after every top-level task is checked and every workflow commit
  is pushed.
- Produce the Session Overview using the `session-overview` skill whether the
  workflow completes or stops.
