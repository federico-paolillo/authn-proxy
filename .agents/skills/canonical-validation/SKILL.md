---
name: canonical-validation
description: Select and run the repository's canonical validation commands for the areas changed, and report the results.
---

# Canonical Validation

Use this skill before a validated implementation or review handoff.

## Procedure

1. If the repository documents a canonical aggregate validation entrypoint, run
   it.
2. Otherwise determine which areas changed and run the applicable canonical Mise
   tasks from `mise.toml`. The names below are a reference; only run the tasks
   that are actually defined in `mise.toml` (list them with `mise tasks`), and
   skip any that are missing — a project may not define every task.
   - Backend changes: `be:build`, `be:test`, `be:lint`, `be:types`, `be:openapi`
   - Frontend changes: `fe:build`, `fe:test`, `fe:lint`, `fe:check`, `fe:openapi`
   - Run the applicable tasks for both sets when both areas changed.
3. Do not hand off validated work until all applicable canonical tasks pass
   without errors.
4. Report the commands run and their outcomes in the handoff.

## Interpretation

- Native configuration rendering, image building, and release-archive assembly
  count as build validation when applicable.
- Prefer a tool's native parser, renderer, compiler, or build command over a
  bespoke structural validator.
- Add a custom structural validator only when native tooling cannot prove a
  required invariant.
- Follow the repository-wide Verification Guidelines in `AGENTS.md` when
  deciding what additional focused tests are required.
