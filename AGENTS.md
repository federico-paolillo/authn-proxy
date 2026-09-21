# AGENTS.md

## Shared Engineering Guidelines

> The word "module" means a cohesive unit of code in the relevant language or
> framework.

- Prefer established language, framework, and standard-library capabilities over
  custom implementations. Use modern, idiomatic syntax.
- Apply SOLID and GRASP where they make responsibilities clearer. A little
  ceremony is welcome when it visibly produces smaller, more cohesive modules.
- Keep one module per file unless a module is private or used only by the code
  declaring it. Do not split cohesive models or private helpers solely by size.
- Keep endpoint handlers, service wiring, dependency checks, and implementations
  in concern-specific files or folders; keep composition roots small.
- Prefer guard clauses, descriptive domain names, and named conditions over
  dense control flow. Centralize genuinely repeated rules, constants, encoding,
  and validation in small intention-revealing helpers. Keep sibling paths
  behaviorally consistent.
- Refactor when it is necessary to implement or review the story cleanly. Avoid
  unrelated cleanup unless it is required for canonical validation to pass.
- At dependency boundaries, distinguish invalid data from unavailability,
  propagate cancellation, preserve exception causes, keep diagnostics
  secret-free.
- Give each filtering, normalization, validation, and configuration policy one
  authoritative enforcement owner. Add another enforcement layer only for a
  distinct trust boundary or independently stated failure mode.
- For telemetry, application code owns secret filtering, cardinality, and event
  semantics; telemetry infrastructure owns transport, routing, batching,
  authentication, and sampling. Do not duplicate application field allowlists
  downstream without a distinct trust-boundary requirement.
- This is not an enterprise-grade project. Cover the happy path and the most
  obvious and material failure paths; do not harden against every conceivable
  edge case.

## Verification Guidelines

- Do not hand off validated work until all applicable canonical validation
  passes without errors.
- Unit tests start no external processes. Integration tests may provision
  task-owned dependency containers and must not require a prestarted project
  stack, a fixed shared Compose project, or a fixed host port.
- Automated tests are limited to unit and integration tests. Do not create or
  demand smoke, end-to-end, browser end-to-end, or deployment tests.
- Native configuration rendering, image building, and release-archive assembly
  are build validation, not smoke or deployment tests. Prefer a tool's native
  parser, renderer, compiler, or build command. Add a bespoke structural
  validator only when native tooling cannot prove a required invariant.
- Test each behavior once at the lowest stable boundary that can prove it. Add a
  cross-boundary integration test only when behavior can fail despite the
  lower-level check, such as serialization compatibility, dependency semantics,
  or framework wiring.
- Log prose is not an API. Test exact text only when the wording is itself a
  stable filtering or operator contract. Otherwise test severity, event
  identity, required fields, forbidden sensitive fields, and cardinality with
  one representative event per policy class.
- Do not reproduce the same assertion across unit, integration, image,
  configuration, script, and documentation checks merely for reassurance. Reuse
  an existing test at the owning boundary and delete redundant checks when a
  stronger authoritative check supersedes them.
- Do not introduce a test seam, interface, fake, fixture family, or helper whose
  complexity exceeds the behavior it verifies.
- Test volume, branch count, mutation survival, and assertion count are not
  goals. Validation ends when the story's observable acceptance criteria and
  material regression risks are covered.

## Documentation and Comment Guidelines

- First make production code explain itself through precise names, explicit
  types, cohesive structure, ordinary control flow, and established idioms.
- Add a concise inline comment only when important reasoning cannot be recovered
  quickly from the code: a non-obvious invariant, security or compatibility
  constraint, protocol ownership, calibrated limit, or why a simpler-looking
  implementation is incorrect.
- Do not require file, module, function, or test comments merely for coverage.
  Do not narrate names or create a Markdown document instead of clarifying the
  code. Prefer a precise comment beside the constrained mechanism.
- Do not change README contents unless one of your changes directly contradicts
  the contents.
- Do not teach standard Docker, Compose, shell, Git, or service-lifecycle
  commands, and do not narrate implementation details.
- Do not create separate feature, developer, implementation, test, or
  architecture documents unless the SEED explicitly requires one. Encode
  behavior in code and focused tests; explain remaining non-obvious reasoning
  inline.

## Runtime and Container Safety

- Use ports in the range 65100-65199 for Docker Compose, Docker containers, and
  services in general.
- Bind every published Docker or Compose port explicitly to `127.0.0.1`.
- Test-only containers may use dependency-native container ports and
  Docker-assigned ephemeral host ports, but every published test port must bind
  explicitly to `127.0.0.1`.
- Never require `sudo`, privileged containers, firewall changes, or a shared
  prestarted dependency.
- Harden images made by the project as rootless and distroless with read-only
  filesystems. Do not impose project-owned hardening on third-party images.

