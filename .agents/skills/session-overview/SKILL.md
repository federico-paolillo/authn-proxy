---
name: session-overview
description: Produce the final Session Overview for a completed or stopped direct-task or Markdown-queue workflow.
---

# Session Overview

Use this skill at the end of an implementation workflow, whether it ran to
completion or stopped because of a blocker or workflow cap.

Omit fields or prose that do not make sense for the session.

Use this format:

```markdown
Session Summary:

Source: direct prompt | path/to/tasks.md Task: Identifier/"Title"

- Tasks approved: 2/3
- Tasks pushed: 2/3
- Worker-Review loops: 4
- Review findings: 28
- DEBT.md changes: 0
- Status requests sent: 2
- 20-min window extensions: 2
- Agent interruptions: 1
- Ran to completion: no

Stopped at: Identifier/"Title"

This optional paragraph briefly reports a blocker, exhausted cap, error,
interruption, restart, or other event that prevented full completion or required
human intervention.
```

## Rules

- Report the actual direct task or task-queue source and the selected or stopped
  task identifier/title.
- Count workflow activity from the current session only and across all tasks
  processed in that session.
- Keep the overview concise.
- Count a task as approved only after Reviewer approval and as pushed only when
  its workflow commit is on the upstream branch.
- Report completion only when the direct task is approved and pushed, or every
  queued task is checked and pushed.
- If unresolved findings remain because a cap was reached, say so explicitly.
- If the workflow stopped for a blocker or required human intervention, identify
  its source, why it blocks progress, and the required decision or next step.
- Do not imply completion when Reviewer approval or a required push is missing.
