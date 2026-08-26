# Task 13 Production Cutover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the proven .NET 10 / Avalonia / LLamaSharp implementation into the canonical `dhhieu113pro/butchi` repository without losing the existing Tauri/Rust history, Store identity, release history, or rollback path.

**Architecture:** Prepare all cutover guardrails in the migration repository first, preserve the exact current canonical Tauri/Rust commit with permanent refs, then replace the canonical `main` tree with the validated Avalonia tree while keeping canonical repository history intact. Canonical CI/parity/final-validation/release evidence gates the final retirement of `butchi-fake`.

**Tech Stack:** .NET 10, C# 14, Avalonia, LLamaSharp, xUnit, PowerShell, GitHub Actions, GitHub Git data/refs, MSIX/MSIXBundle/MSIXUpload.

**Spec:** `docs/superpowers/specs/2026-08-26-task13-production-cutover-design.md`

## Global Constraints

- `dhhieu113pro/butchi` remains the permanent public repository for Butchi.
- Preserve the final Tauri/Rust state through both `legacy-tauri` and a permanent pre-cutover tag before changing canonical `main`.
- Never delete existing canonical tags or releases.
- Never archive `butchi-fake` before canonical validation is green.
- Preserve `STORE_PACKAGE_IDENTITY_NAME`, `STORE_PACKAGE_PUBLISHER`, and `STORE_PUBLISHER_DISPLAY_NAME` semantics.
- Tagged releases must continue producing x64 `.msix`, ARM64 `.msix`, one `.msixbundle`, and one `.msixupload`.
- No production workflow or documentation may depend on a mutable `butchi-fake` branch after cutover.
- The cutover must remain reversible through preserved git refs and history.

---

### Task 1: Add production-cutover contract guardrails

**Files:**
- Create: `tests/Butchi.App.Tests/ProductionCutoverContractTests.cs`
- Create: `scripts/verify-production-cutover.ps1`
- Modify: `Butchi.slnx` only if the existing test project is not already included.

**Interfaces:**
- Consumes: repository root files and workflow text.
- Produces: `verify-production-cutover.ps1 -RepositoryRoot <path>` with non-zero exit on legacy/mutable migration references.

- [ ] **Step 1: Write the failing xUnit contract test**

```csharp
using Xunit;

namespace Butchi.App.Tests;

public sealed class ProductionCutoverContractTests
{
    [Fact]
    public void Canonical_cutover_contract_forbids_mutable_butchi_fake_dependencies()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var finalValidation = File.ReadAllText(Path.Combine(root, ".github", "workflows", "final-validation.yml"));

        Assert.DoesNotContain("successor implementation", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("butchi-fake/main", release, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("butchi-fake/main", finalValidation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win-x64", release, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win-arm64", release, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.msixbundle", finalValidation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.msixupload", finalValidation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cutover_procedure_documents_permanent_legacy_anchors()
    {
        var root = FindRepositoryRoot();
        var procedure = File.ReadAllText(Path.Combine(root, "docs", "production-cutover.md"));
        Assert.Contains("legacy-tauri", procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pre-cutover", procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", procedure, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Butchi repository root.");
    }
}
```

- [ ] **Step 2: Run the contract tests and verify RED**

Run: `dotnet test Butchi.slnx -c Release --filter ProductionCutoverContractTests`

Expected: FAIL because the README still describes this repository as the successor and `docs/production-cutover.md` does not yet exist.

- [ ] **Step 3: Add the PowerShell verifier**

```powershell
param([string]$RepositoryRoot = (Resolve-Path "$PSScriptRoot/..").Path)
$ErrorActionPreference = 'Stop'

$readme = Get-Content (Join-Path $RepositoryRoot 'README.md') -Raw
if ($readme -match '(?i)successor implementation|butchi-fake') {
    throw 'README still identifies the production implementation as a migration/successor repository.'
}

$workflowPaths = @(
    '.github/workflows/ci.yml',
    '.github/workflows/parity.yml',
    '.github/workflows/final-validation.yml',
    '.github/workflows/release.yml'
)
foreach ($relative in $workflowPaths) {
    $path = Join-Path $RepositoryRoot $relative
    if (-not (Test-Path $path)) { throw "Missing canonical workflow: $relative" }
    $text = Get-Content $path -Raw
    if ($text -match '(?i)github\.com/dhhieu113pro/butchi-fake/(main|master)|raw\.githubusercontent\.com/dhhieu113pro/butchi-fake/(main|master)') {
        throw "Mutable butchi-fake dependency remains in $relative"
    }
}

Write-Host 'Production cutover repository contract passed.'
```

- [ ] **Step 4: Commit the RED contract slice**

```bash
git add tests/Butchi.App.Tests/ProductionCutoverContractTests.cs scripts/verify-production-cutover.ps1
git commit -m "test: define Task 13 production cutover contract"
```

---

### Task 2: Make the Avalonia tree canonical-ready

**Files:**
- Modify: `README.md`
- Create: `docs/production-cutover.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/parity.yml`
- Modify: `.github/workflows/final-validation.yml`
- Modify: `.github/workflows/release.yml`
- Test: `tests/Butchi.App.Tests/ProductionCutoverContractTests.cs`

**Interfaces:**
- Consumes: Task 1 contract.
- Produces: a repository tree safe to place at canonical `dhhieu113pro/butchi/main`.

- [ ] **Step 1: Rewrite README as canonical Butchi documentation**

Use this opening text exactly:

```markdown
# Butchi

Windows-first local Translate & Rewrite utility built with .NET 10, Avalonia, and LLamaSharp.

Butchi runs local GGUF inference on Windows and provides global Double-Ctrl activation, Windows UI Automation with guarded clipboard fallback, local SQLite history, and x64/ARM64 packaging.
```

Remove language that calls the code a successor, candidate, clean-room replacement, or references `butchi-fake` as the production location.

- [ ] **Step 2: Add the permanent cutover/rollback procedure**

Create `docs/production-cutover.md` containing:

```markdown
# Butchi Production Cutover

Before canonical `main` changes, preserve its exact pre-cutover Tauri/Rust commit as:

- branch: `legacy-tauri`
- tag: `pre-avalonia-cutover-2026-08-26`

These refs are permanent and must not be deleted. They are the source-level rollback/reference anchors for the former implementation.

The .NET/Avalonia tree becomes canonical only after the anchors exist. If canonical validation fails, fix forward on the .NET branch or restore `main` from the preserved legacy anchor; never rewrite history to erase either implementation.

`butchi-fake` may be archived only after canonical CI, parity/performance, final migration validation, and release packaging evidence are green.
```

- [ ] **Step 3: Ensure workflows are repository-relative**

Search all four production workflows for mutable `butchi-fake` URLs. Replace any mutable source dependency with repository-local paths or immutable canonical commit URLs. Keep the existing immutable historical canonical icon SHA reference in `release.yml` unchanged.

- [ ] **Step 4: Wire the production-cutover verifier into CI**

Add this Windows CI step after checkout/setup and before publish smoke checks:

```yaml
      - name: Verify production cutover contract
        shell: pwsh
        run: ./scripts/verify-production-cutover.ps1
```

- [ ] **Step 5: Run the focused contract tests**

Run: `dotnet test Butchi.slnx -c Release --filter ProductionCutoverContractTests`

Expected: PASS.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test Butchi.slnx -c Release`

Expected: all tests PASS.

- [ ] **Step 7: Run verifier directly**

Run: `pwsh -File scripts/verify-production-cutover.ps1`

Expected: exit code 0 and `Production cutover repository contract passed.`

- [ ] **Step 8: Commit canonical-ready changes**

```bash
git add README.md docs/production-cutover.md .github/workflows scripts tests
git commit -m "feat: make Avalonia tree canonical-ready"
```

---

### Task 3: Review canonical-ready source PR before repository mutation

**Files:**
- No new production files.

**Interfaces:**
- Consumes: canonical-ready branch from Tasks 1-2.
- Produces: reviewed, green source commit that will be imported into `dhhieu113pro/butchi`.

- [ ] **Step 1: Push/open a draft PR from `feat/task13-production-cutover` to `butchi-fake/main`**

PR title: `feat: prepare Task 13 production cutover`

PR body must state that merging this PR does not yet mutate canonical `dhhieu113pro/butchi`; it only freezes the exact source tree for cutover.

- [ ] **Step 2: Require fresh CI success on the PR head**

Verify GitHub Actions conclusion is `success` and no blocking review threads remain.

- [ ] **Step 3: Merge the source-preparation PR**

Use squash merge and capture the resulting `butchi-fake/main` commit SHA as `SOURCE_CUTOVER_SHA`.

---

### Task 4: Preserve the canonical Tauri/Rust implementation permanently

**Files:**
- Git refs only in `dhhieu113pro/butchi`.

**Interfaces:**
- Consumes: exact current `dhhieu113pro/butchi/main` SHA as `LEGACY_SHA`.
- Produces: `refs/heads/legacy-tauri` and `refs/tags/pre-avalonia-cutover-2026-08-26`, both pointing to `LEGACY_SHA`.

- [ ] **Step 1: Read current canonical main SHA**

Run equivalent GitHub API read for `repos/dhhieu113pro/butchi/git/ref/heads/main` and record the object SHA as `LEGACY_SHA`.

- [ ] **Step 2: Create `legacy-tauri` from exactly `LEGACY_SHA`**

Verify the created branch SHA equals `LEGACY_SHA`.

- [ ] **Step 3: Create permanent pre-cutover tag from exactly `LEGACY_SHA`**

Create lightweight tag ref `refs/tags/pre-avalonia-cutover-2026-08-26` pointing directly to `LEGACY_SHA`.

- [ ] **Step 4: Re-read both refs**

Expected: branch and tag both resolve to the same `LEGACY_SHA`. Stop if either differs.

---

### Task 5: Import the canonical-ready Avalonia tree into `dhhieu113pro/butchi/main`

**Files:**
- Entire canonical repository tree, replacing obsolete Tauri/Rust files on `main` while retaining them in history and legacy refs.

**Interfaces:**
- Consumes: source tree at `SOURCE_CUTOVER_SHA`, legacy parent `LEGACY_SHA`.
- Produces: a new canonical commit `CANONICAL_CUTOVER_SHA` whose parent is `LEGACY_SHA` and whose tree matches the source cutover tree.

- [ ] **Step 1: Enumerate the complete source tree at `SOURCE_CUTOVER_SHA` recursively**

Collect every blob path, mode, and UTF-8 content required by the source tree. Exclude no tracked file except repository-specific migration metadata explicitly forbidden by the spec; `docs/superpowers/` remains included for traceability.

- [ ] **Step 2: Recreate source blobs in `dhhieu113pro/butchi`**

For every source blob, create an identical target-repository blob and record its target SHA. Preserve executable modes for scripts where applicable.

- [ ] **Step 3: Build a root tree from scratch in canonical repository**

Do not use the legacy tree as the base tree. Creating the root tree from scratch ensures obsolete Tauri/Rust files disappear from canonical `main` while remaining reachable through history and preserved refs.

- [ ] **Step 4: Create canonical cutover commit**

Commit message: `feat: cut over Butchi to .NET Avalonia`

Parent: exactly `LEGACY_SHA`.

Tree: exactly the recreated Avalonia source tree.

- [ ] **Step 5: Move canonical `main` to `CANONICAL_CUTOVER_SHA`**

Use a normal ref update without force if GitHub recognizes the new commit as a direct child of `LEGACY_SHA`. Stop rather than force if the ref update is rejected unexpectedly.

- [ ] **Step 6: Verify canonical root contents**

Expected root includes `Butchi.slnx`, `Directory.Build.props`, `src`, `tests`, `store`, `scripts`, `.github`; obsolete `src-tauri`, Tauri package files, and frontend build artifacts are absent from canonical `main`.

---

### Task 6: Prove canonical CI and migration gates

**Files:**
- No new files unless failures reveal canonical-only path assumptions.

**Interfaces:**
- Consumes: `CANONICAL_CUTOVER_SHA`.
- Produces: fresh canonical CI/parity/final-validation evidence.

- [ ] **Step 1: Verify canonical CI run for `CANONICAL_CUTOVER_SHA`**

Expected: all required CI jobs `success`.

- [ ] **Step 2: Run/verify Task 12 parity and performance workflow in canonical repo**

Expected evidence includes `performance-summary.json` and `task12-parity-result.json` with passing x64 and ARM64 results.

- [ ] **Step 3: Run final migration validation in canonical repo**

Supply successful canonical parity/performance, release, and screenshot run IDs. Expected: final gate produces `migration-summary.md` with `Migration gate: PASSED`.

- [ ] **Step 4: Stop on any canonical-only failure**

Fix forward in `dhhieu113pro/butchi`; do not archive or mutate `butchi-fake` retirement state while any required canonical check is not successful.

---

### Task 7: Prove canonical release packaging and mark migration source retired

**Files:**
- Modify later: `dhhieu113pro/butchi-fake/README.md` only after canonical proof is green.

**Interfaces:**
- Consumes: green canonical cutover and successful release workflow.
- Produces: canonical release packaging evidence and migration-source retirement notice.

- [ ] **Step 1: Run canonical release workflow without publishing a production tag if possible**

Use `workflow_dispatch` to verify package creation with test identity fallback where supported.

Expected artifacts:

```text
Butchi_<version>_x64.msix
Butchi_<version>_arm64.msix
Butchi_<version>.msixbundle
Butchi_<version>.msixupload
```

- [ ] **Step 2: Verify the package workflow ran in `dhhieu113pro/butchi`**

Reject evidence originating from `butchi-fake`.

- [ ] **Step 3: Update `butchi-fake/README.md` with migration notice**

Use this message:

```markdown
# Butchi migration repository

Development has moved to `dhhieu113pro/butchi`.

This repository contains the temporary clean-room migration history used to rebuild Butchi with .NET 10, Avalonia, and LLamaSharp. The canonical application, CI, releases, and future development now live in `dhhieu113pro/butchi`.
```

- [ ] **Step 4: Verify no canonical workflows point back to mutable `butchi-fake` refs**

Run the production-cutover verifier in canonical repo and search workflows for `butchi-fake/main` and `butchi-fake/master`.

Expected: no matches.

- [ ] **Step 5: Archive `butchi-fake` only after all previous steps are green**

If repository archive mutation is unavailable through the connected GitHub tool, leave the migration notice merged and report archival as the only manual repository-setting action remaining; do not falsely claim it is archived.

- [ ] **Step 6: Record Task 13 completion evidence**

Document `LEGACY_SHA`, `SOURCE_CUTOVER_SHA`, `CANONICAL_CUTOVER_SHA`, canonical CI run, parity run, final-validation run, and release run in the Task 13 completion PR or migration note.
