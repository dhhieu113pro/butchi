# Task 13 — Production Cutover Design

## Goal

Make the completed .NET 10 / Avalonia / LLamaSharp implementation the canonical Butchi application while preserving the existing public `dhhieu113pro/butchi` repository identity, legacy Tauri/Rust history, package identity, release history, and rollback path.

## Canonical Repository Strategy

`dhhieu113pro/butchi` remains the permanent public repository for Butchi. The completed implementation currently developed in `dhhieu113pro/butchi-fake` becomes the implementation on canonical `butchi/main`.

The existing Tauri/Rust code on `butchi/main` must be preserved before cutover using both:

- a permanent legacy branch, `legacy-tauri`
- a permanent pre-cutover tag identifying the final Tauri/Rust state

Existing issues, pull requests, releases, tags, stars, and repository URL remain attached to `dhhieu113pro/butchi`.

`butchi-fake` is not the long-term production repository. It remains active until canonical validation succeeds, then becomes an archived migration/reference repository with a README pointer to `dhhieu113pro/butchi`.

## Cutover Mechanics

The production cutover is reversible and staged:

1. Capture the current canonical `butchi/main` commit.
2. Create `legacy-tauri` from that exact commit.
3. Create a permanent pre-cutover legacy tag from that exact commit.
4. Import the proven .NET/Avalonia tree from `butchi-fake/main` into `butchi/main` while preserving the canonical repository itself.
5. Remove obsolete Tauri/Rust implementation files from canonical `main` as part of the same cutover tree, while leaving them reachable through `legacy-tauri`, the legacy tag, and normal git history.
6. Update canonical documentation so the .NET/Avalonia implementation is described simply as Butchi rather than as a successor or clean-room candidate.
7. Update CI, parity, final-validation, and release workflows to run from `dhhieu113pro/butchi`.
8. Validate canonical `main` before creating the first .NET/Avalonia production tag.
9. Produce the first canonical .NET/Avalonia release from `dhhieu113pro/butchi`.
10. Archive `butchi-fake` only after canonical CI, migration validation, and release evidence are green.

No destructive cleanup of historical tags or releases is part of Task 13.

## Safety and Rollback Rules

The following are hard requirements:

- Never delete the final Tauri/Rust source history.
- Never delete existing canonical tags or releases.
- Never archive `butchi-fake` before canonical validation is green.
- Do not change the public canonical repository URL.
- Preserve package identity wherever possible to avoid creating a second Microsoft Store application identity.
- Preserve release artifact naming unless a change is required by the canonical repository transition.
- A failed canonical validation blocks the cutover from being declared complete.
- `legacy-tauri` and the pre-cutover tag are permanent rollback/reference anchors.

Rollback means restoring canonical `main` from the preserved Tauri/Rust anchor or applying a corrective .NET/Avalonia commit; history must not be rewritten to erase either implementation.

## Package Identity and Release Behavior

The migrated release workflow already produces:

- x64 Store MSIX
- ARM64 Store MSIX
- a multi-architecture MSIX bundle
- a Microsoft Store `.msixupload`

Task 13 preserves the existing Store identity secrets:

- `STORE_PACKAGE_IDENTITY_NAME`
- `STORE_PACKAGE_PUBLISHER`
- `STORE_PUBLISHER_DISPLAY_NAME`

Tagged releases continue to produce `Butchi_<version>` artifacts. The first canonical .NET/Avalonia production release uses the next normal semantic version and explicitly notes that it is the first Avalonia/.NET release.

Store artwork or package resources that currently reference historical canonical `dhhieu113pro/butchi` content may continue to do so if they resolve to immutable commit SHAs. No migration-time dependency may point back to a mutable `butchi-fake` branch.

## Canonical Verification Gate

Before the production cutover is considered complete, `dhhieu113pro/butchi` must independently pass all relevant evidence gates from the migrated implementation:

- normal CI on Windows
- x64 behavioral parity validation
- ARM64 behavioral parity validation
- Task 12 performance gate
- final Task 12 migration validation
- release packaging verification

Release packaging verification must prove that the artifacts were produced from the canonical repository and include:

- one x64 `.msix`
- one ARM64 `.msix`
- one `.msixbundle`
- one `.msixupload`

The canonical release workflow must fail if any required artifact is missing.

## Documentation State After Cutover

The canonical README must describe Butchi as a Windows-first local Translate & Rewrite utility implemented with .NET 10, Avalonia, and LLamaSharp. It must not describe the implementation as `butchi-fake`, a candidate successor, or a migration work-in-progress.

Migration documentation may remain under `docs/superpowers/` for traceability. A short legacy note should point contributors who need the old implementation to `legacy-tauri` and the preserved pre-cutover tag.

After canonical validation succeeds, the `butchi-fake` README should state that development has moved to `dhhieu113pro/butchi` and the repository should be archived/read-only.

## Testing Strategy

Task 13 implementation should use TDD for new guardrails where practical. At minimum, automated tests or workflow contract tests must verify:

- canonical documentation contains no active `butchi-fake` production references
- release workflows do not depend on mutable `butchi-fake` paths
- both Windows architectures remain configured
- the canonical final migration gate remains wired
- release packaging still requires `.msix`, `.msixbundle`, and `.msixupload`
- legacy preservation metadata is documented and cannot be silently omitted from the cutover procedure

The actual repository cutover should be executed only after the code/workflow changes have been reviewed and their tests are green.

## Completion Criteria

Task 13 is complete only when all of the following are true:

1. The former Tauri/Rust canonical state is permanently preserved in `legacy-tauri` and a pre-cutover tag.
2. `dhhieu113pro/butchi/main` contains the .NET/Avalonia implementation.
3. Canonical README/docs identify that implementation as Butchi.
4. CI, parity, performance, and final migration validation are green in `dhhieu113pro/butchi`.
5. A canonical release run produces both MSIX packages, the MSIX bundle, and the `.msixupload`.
6. Store package identity remains compatible with the existing Butchi listing.
7. `butchi-fake` is marked migrated and archived only after the canonical proof is complete.

## Out of Scope

Task 13 does not add new product features, UI redesigns, inference optimizations, or unrelated refactoring. Those belong to the post-cutover hardening phase.
