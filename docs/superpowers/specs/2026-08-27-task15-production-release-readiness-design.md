# Task 15 Production Release & Store Readiness Design

**Goal:** Turn the completed native .NET 10/Avalonia Butchi application into a release candidate that is proven through automated installed-package validation, while preserving the existing Microsoft Store packaging/signing model.

## Context

Task 14 completed native Avalonia UI parity and deterministic screenshot evidence. The repository already publishes self-contained `win-x64` and `win-arm64` application payloads, stages Store MSIX packages, builds a multi-architecture `.msixbundle`, and produces a Store `.msixupload`. Tagged Store artifacts are intentionally unsigned because Microsoft Store certification signs accepted packages.

The remaining gap is that current CI proves build/package generation but does not prove that a packaged Windows installation can be trusted, installed, launched, upgraded, and removed successfully.

## Design principles

- Keep Store deliverables unsigned exactly as they are today.
- Create a separate CI-only signed copy for installation tests.
- Use a workflow-generated ephemeral certificate and trust it only inside the runner job.
- Never persist or publish the CI signing private key.
- Keep production payload and CI installation payload structurally identical apart from signature metadata.
- Require `win-x64` and `win-arm64` package creation on every release-readiness validation.
- Execute installed native code only where runner architecture supports it; do not claim ARM64 runtime execution on x64 GitHub-hosted runners.
- Keep Task 14 deterministic visual evidence as part of the final release gate.
- Use TDD for repository contracts, package validators, automation hooks, and application behavior changes.
- Task 15 is fully automated; there is no required manual Windows interaction checklist.

## Architecture

Task 15 adds a release-readiness validation layer around the existing release workflow rather than replacing release packaging.

The pipeline has two conceptual outputs from one staged application payload:

1. **Production Store path** — existing unsigned per-architecture MSIX packages, `.msixbundle`, and `.msixupload`. These remain the artifacts intended for Microsoft Store submission.
2. **CI installation path** — an ephemeral test certificate is generated at runtime, a CI-only copy of the x64 package is signed, the certificate is temporarily trusted on the runner, and that signed package is used only for automated install/launch/upgrade/uninstall validation.

Shared scripts should own manifest/package validation and lifecycle operations so workflow YAML remains declarative and testable.

## Slice 15.1: Package contract

Add executable repository/package contracts that validate:

- Store manifest template identity placeholders and full-trust application declaration.
- Expected executable name (`butchi.exe`) after staging.
- Required Store visual assets.
- Numeric four-component MSIX version semantics.
- x64 and ARM64 architecture-specific identities.
- Exactly one architecture-specific package per required RID.
- Bundle contains both required packages.
- `.msixupload` contains the generated bundle in the expected upload container.
- Store output remains unsigned at the production-artifact boundary.

Tests must begin RED against any missing validator behavior and then become GREEN with the smallest implementation.

## Slice 15.2: CI-only ephemeral signing

Introduce a reusable PowerShell helper that:

- creates a short-lived self-signed code-signing certificate,
- exports only the public certificate as needed for trust installation,
- keeps the private key in the runner certificate store/process scope,
- signs a copied CI MSIX with SignTool,
- validates the resulting Authenticode/package signature,
- installs the public certificate only into the runner trust store required for package installation,
- removes certificate material during cleanup.

The script must reject production artifact paths so test signing cannot accidentally modify release deliverables.

CI must never upload the PFX/private key.

## Slice 15.3: Installed x64 smoke

On a Windows x64 runner:

- build the normal x64 Store package,
- clone it into a dedicated CI-install location,
- sign only that copy,
- install with Windows package tooling,
- assert package identity, version, install location, and expected executable exist,
- launch the installed application through the package context,
- assert the packaged process reaches a deterministic healthy-start condition,
- terminate the test process,
- uninstall the package,
- assert package registration is gone.

A deterministic automation mode may be added to the app to avoid tray/UI timing races. That mode must be unavailable during ordinary startup unless explicitly requested through test-only command-line arguments.

## Slice 15.4: Upgrade lifecycle

Automate package upgrade using one CI identity and certificate:

1. Install package version N.
2. Seed deterministic application data representing compatible settings/history state.
3. Build/sign version N+1 with the same package identity.
4. Install N+1 as an update.
5. Assert package version changed and app data remains readable/compatible.
6. Launch N+1 through the deterministic installed-app probe.
7. Uninstall the package.
8. Verify uninstall removes package registration while not falsely asserting deletion of user roaming/local data unless Windows/package behavior explicitly requires it.

The gate protects config/history compatibility across Store upgrades.

## Slice 15.5: Packaged application behavior

Add narrow deterministic automation probes for release-critical packaged behavior that cannot be reliably proven by process existence alone. The probes should use existing services and composition rather than duplicate behavior.

Required assertions:

- application composition starts successfully from packaged install location,
- tray/startup composition can initialize without crashing,
- management surfaces/services are resolvable,
- first-run model state can be composed without downloading a real model,
- history/config paths resolve to the expected per-user locations,
- diagnostics emitted during the probe do not contain seeded selected text or prompt content,
- deterministic shutdown completes without orphaning the tested process.

Do not attempt synthetic global keyboard/mouse automation for Double-Ctrl or real selected-text replacement on GitHub-hosted runners; those native interaction mechanisms remain covered by their focused platform tests and production composition checks. The automated release gate must not overstate what it executes.

## Slice 15.6: ARM64 package validation

Continue publishing the self-contained `win-arm64` application and producing an ARM64 MSIX. Validate:

- executable and expected native/runtime files exist,
- manifest architecture is `arm64`,
- package can be structurally unpacked/validated,
- package identity/version matches x64 except architecture where appropriate,
- bundle includes the ARM64 package,
- existing ARM64-focused application/inference tests remain green.

Because GitHub-hosted Windows runners are x64, Task 15 does not claim native ARM64 package launch unless an actual ARM64 runner becomes available later.

## Slice 15.7: Final release gate

Add a release-readiness workflow or reusable workflow composition that requires all of the following on the exact candidate head:

- production cutover/repository contracts,
- full solution build and tests,
- Task 14 deterministic screenshot evidence,
- self-contained `win-x64` publish,
- self-contained `win-arm64` publish,
- unsigned production x64 Store MSIX,
- unsigned production ARM64 Store MSIX,
- valid multi-architecture `.msixbundle`,
- valid Store `.msixupload`,
- ephemeral signing of CI-only x64 copy,
- x64 install/launch/uninstall smoke,
- x64 N -> N+1 upgrade lifecycle,
- privacy-safe packaged diagnostics,
- artifact inspection proving no CI private signing material is uploaded.

Tagged releases may reuse the same validated packaging scripts, but creating a tag/release is separate from proving readiness.

## Failure handling

Every script should fail loudly with a concrete boundary-specific message: staging, manifest generation, signing, trust, install, launch, update, uninstall, bundle creation, or artifact validation. Cleanup blocks must remove installed test packages and temporary certificate material even after test failure.

No workflow should convert a failed installed-package test into a warning or continue-on-error result.

## Security and privacy

The CI certificate is disposable and exists only to satisfy Windows sideload trust for runner-local validation. It is not a release-signing credential and must never be presented as one.

Automation probes must use synthetic deterministic text. Logs must not emit model prompts, selected user text, clipboard content, or seeded history bodies. Existing Store identity values continue to come from repository variables for tagged releases.

## Testing strategy

Each slice starts with a failing contract/test where code behavior is introduced. Workflow-only behavior should be backed by script-level or repository-contract tests whenever practical, followed by a real GitHub Actions RED/GREEN run for the platform integration itself.

A slice is not merge-ready until its exact head has fresh green evidence for all checks it affects. The final Task 15 slice must additionally review generated package/artifact inventory and Task 14 screenshots before merge.

## Success criteria

Task 15 is complete only when the final merged `main` head has fresh evidence that the application tests pass, visual parity evidence is generated, x64 and ARM64 release packages are valid, the x64 CI package installs and launches, upgrade N -> N+1 preserves compatible app data, uninstall succeeds, Store bundle/upload artifacts are valid, production artifacts remain unsigned, and no ephemeral signing private key is published.
