# Task 15 Production Release & Store Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that Butchi’s Store packages can be generated, validated, installed, launched, upgraded, and removed automatically while keeping production Store artifacts unsigned and preserving x64/ARM64 release coverage.

**Architecture:** Keep the existing release packaging path intact and add focused PowerShell validators plus a CI-only signed-install path. Production artifacts remain unsigned; an ephemeral runner certificate signs only copied test packages used for x64 lifecycle validation.

**Tech Stack:** .NET 10, C# 14, Avalonia, xUnit, PowerShell 7, GitHub Actions, WinApp CLI, MakeAppx, SignTool, Windows Appx/MSIX tooling.

**Spec:** `docs/superpowers/specs/2026-08-27-task15-production-release-readiness-design.md`

## Global Constraints

- Keep Store deliverables unsigned exactly as they are today.
- Create a separate CI-only signed copy for installation tests.
- Use a workflow-generated ephemeral certificate and trust it only inside the runner job.
- Never persist or publish the CI signing private key.
- Keep production payload and CI installation payload structurally identical apart from signature metadata.
- Require `win-x64` and `win-arm64` package creation on every release-readiness validation.
- Do not claim ARM64 runtime execution on x64 GitHub-hosted runners.
- Keep Task 14 deterministic visual evidence in the final release gate.
- Use TDD for validators, automation hooks, and application behavior changes.
- Task 15 is fully automated; there is no required manual Windows interaction checklist.

---

### Task 1: Package contract and validator

**Files:**
- Create: `scripts/Release/Validate-StorePackage.ps1`
- Create: `tests/Butchi.App.Tests/StorePackageContractTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `store/Package.appxmanifest.template`, staged package directory, generated `.msix`, `.msixbundle`, `.msixupload`.
- Produces: `Validate-StorePackage.ps1 -StagePath <path> -Architecture <x64|arm64> -Version <a.b.c.d> [-BundlePath <path>] [-UploadPath <path>]` with non-zero exit on contract violation.

- [ ] **Step 1: Write failing contract tests** asserting the repository defines a package validator that checks manifest placeholders, `Windows.FullTrustApplication`, `butchi.exe`, Store assets, numeric four-part MSIX versions, x64/ARM64 architecture values, and production unsigned-output rules.
- [ ] **Step 2: Run** `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj -c Release --filter StorePackageContractTests` and verify RED because the validator does not exist.
- [ ] **Step 3: Implement** `Validate-StorePackage.ps1` with explicit failures for missing manifest, executable, assets, invalid version, wrong architecture, unexpected package counts, malformed bundle/upload container, or a signature on a production artifact.
- [ ] **Step 4: Update release workflow** so each architecture-specific package is validated immediately after packing, and bundle/upload outputs are validated after creation.
- [ ] **Step 5: Run focused tests**, then `dotnet test Butchi.slnx -c Release`; verify PASS.
- [ ] **Step 6: Trigger CI/release workflow on the exact branch head** and verify x64 + ARM64 package validation passes.
- [ ] **Step 7: Commit** `test: enforce Store package release contract`.

### Task 2: CI-only ephemeral signing

**Files:**
- Create: `scripts/Release/New-CiMsixSigningCertificate.ps1`
- Create: `scripts/Release/Sign-CiMsix.ps1`
- Test: `tests/Butchi.App.Tests/CiMsixSigningContractTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Produces: a temporary certificate thumbprint/public `.cer`; `Sign-CiMsix.ps1 -InputMsix <copy> -CertificateThumbprint <thumbprint> -ProductionRoot <path>`.
- Invariant: signing script must reject any input path under the production Store artifact root.

- [ ] **Step 1: Write failing tests** requiring separate certificate/signing scripts, private-key non-upload guarantees, production-path rejection, signature verification, and cleanup behavior.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement certificate creation** with a short-lived self-signed code-signing cert in the runner user store and public certificate export only.
- [ ] **Step 4: Implement CI package signing** using SignTool, then verify the signature and signer thumbprint.
- [ ] **Step 5: Add cleanup** that removes trust/store entries and temporary certificate files in a `finally`/always-running workflow step.
- [ ] **Step 6: Add a release-workflow job** that copies the x64 MSIX to `artifacts/ci-install/`, signs only that copy, verifies it, and never uploads certificate/private-key material.
- [ ] **Step 7: Run tests and the workflow**; verify the production MSIX remains unsigned and the CI copy is signed.
- [ ] **Step 8: Commit** `ci: add ephemeral MSIX signing for install tests`.

### Task 3: Installed x64 smoke

**Files:**
- Create: `scripts/Release/Test-InstalledMsix.ps1`
- Modify: `src/Butchi.App/Program.cs`
- Create/Modify: focused startup probe code under `src/Butchi.App/Diagnostics/`
- Test: `tests/Butchi.App.Tests/InstalledAppProbeTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- App probe CLI: `butchi.exe --release-probe <output-json>`.
- Probe output includes package/startup health markers only; no user content.

- [ ] **Step 1: Write failing tests** for parsing `--release-probe`, deterministic probe completion, and privacy-safe output fields.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement the smallest release probe** that composes the real application services, verifies startup dependencies, writes a deterministic JSON result, and exits cleanly without opening normal interactive UI.
- [ ] **Step 4: Implement `Test-InstalledMsix.ps1`** to install the signed x64 CI package, assert package identity/version/install path, launch the packaged executable with `--release-probe`, validate probe JSON, stop any remaining process, uninstall, and assert package registration is removed.
- [ ] **Step 5: Ensure cleanup executes after failures** so a test package is not left installed.
- [ ] **Step 6: Run focused/full tests**, then execute the workflow on the exact branch head and verify install/launch/uninstall passes.
- [ ] **Step 7: Commit** `test: validate installed x64 Store package lifecycle`.

### Task 4: Upgrade lifecycle

**Files:**
- Create: `scripts/Release/Test-MsixUpgrade.ps1`
- Modify: release-probe code under `src/Butchi.App/Diagnostics/`
- Test: `tests/Butchi.App.Tests/UpgradeProbeTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Upgrade script accepts package N, package N+1, package identity, and deterministic app-data seed root.
- Release probe can report config/history readability without exposing their text bodies.

- [ ] **Step 1: Write failing tests** requiring the probe to report config/history compatibility markers and never emit seeded selected text, prompt content, or history body content.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Extend the probe minimally** to load real config/history services and report only boolean/count/schema-safe metadata.
- [ ] **Step 4: Implement `Test-MsixUpgrade.ps1`**: install N, seed deterministic compatible app data, install N+1 with the same identity/certificate, assert version changed, run the N+1 probe, verify compatibility, uninstall, and verify package registration removal.
- [ ] **Step 5: Build two test versions** in CI such as `0.1.0.0` and `0.1.0.1`, preserving identical package identity.
- [ ] **Step 6: Run focused/full tests and workflow**; verify the exact head passes N→N+1 upgrade validation.
- [ ] **Step 7: Commit** `test: validate Store package upgrade compatibility`.

### Task 5: Packaged application behavior and privacy gate

**Files:**
- Modify: release-probe code under `src/Butchi.App/Diagnostics/`
- Test: `tests/Butchi.App.Tests/ReleaseProbeBehaviorTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Probe result fields: composition success, tray-service initialization success, management-service resolution success, first-run model-state success, config path root, history path root, shutdown success, and diagnostics privacy result.

- [ ] **Step 1: Write failing tests** for all required probe markers and privacy redaction using synthetic secret sentinel strings.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Compose existing tray/startup, management, model, config, and history services** in probe mode without downloading a model or synthesizing real global keyboard/mouse events.
- [ ] **Step 4: Add diagnostic capture** and assert sentinel selected text/prompt/history bodies never occur in emitted logs.
- [ ] **Step 5: Add deterministic shutdown verification** so the probe exits without orphaning the packaged process.
- [ ] **Step 6: Run focused/full tests and installed-package workflow**; verify PASS.
- [ ] **Step 7: Commit** `test: enforce packaged app behavior and privacy`.

### Task 6: ARM64 release package validation

**Files:**
- Modify: `scripts/Release/Validate-StorePackage.ps1`
- Test: `tests/Butchi.App.Tests/Arm64ReleaseContractTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Validator must inspect the ARM64 staged payload/package without attempting native execution on x64 runners.

- [ ] **Step 1: Write failing tests** requiring ARM64 manifest architecture, expected executable/runtime/native files, matching identity/version semantics, and bundle inclusion.
- [ ] **Step 2: Run focused tests** and verify RED for any missing ARM64-specific validation.
- [ ] **Step 3: Extend the validator** to unpack/inspect ARM64 package contents and compare identity/version with x64 while allowing architecture-specific differences.
- [ ] **Step 4: Keep existing `dotnet publish -r win-arm64 --self-contained true` smoke and ARM64-focused tests as required dependencies of this gate.**
- [ ] **Step 5: Run full tests and release workflow**; verify ARM64 package structural validation passes without claiming ARM64 native launch.
- [ ] **Step 6: Commit** `test: enforce ARM64 Store package readiness`.

### Task 7: Final automated release-readiness gate

**Files:**
- Create: `.github/workflows/release-readiness.yml` or factor reusable jobs from `.github/workflows/release.yml`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`
- Create: `tests/Butchi.App.Tests/ReleaseReadinessCiContractTests.cs`

**Interfaces:**
- Produces one exact-head release-readiness result and an artifact inventory containing only expected public build/package/screenshot outputs.

- [ ] **Step 1: Write failing CI contract tests** requiring build/tests, Task 14 screenshots, x64/ARM64 publishes, unsigned production MSIX packages, bundle, `.msixupload`, signed CI x64 copy, installed smoke, upgrade lifecycle, privacy probe, and artifact inventory validation.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Compose the release-readiness workflow** from the existing packaging steps and Task 15 scripts; no required gate may use `continue-on-error`.
- [ ] **Step 4: Add artifact inventory verification** that rejects `.pfx`, private keys, certificate-store exports containing private keys, or other CI signing secrets from uploaded artifacts.
- [ ] **Step 5: Run** `dotnet test Butchi.slnx -c Release`, Task 14 screenshot smoke, `dotnet publish` for win-x64/win-arm64, and the complete release-readiness workflow on the exact candidate head.
- [ ] **Step 6: Review generated screenshots and package/artifact inventory**; reject blank/overflowing UI evidence, missing architectures, signed production packages, or unexpected signing material.
- [ ] **Step 7: Commit** `ci: enforce automated production release readiness`.
- [ ] **Step 8: Open the final Task 15 PR** and merge only after the exact current head is fresh-green with no blocking reviews or unresolved threads.
- [ ] **Step 9: After merge, verify `main` again** with the complete release-readiness workflow before declaring Task 15 complete.
