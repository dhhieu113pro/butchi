# Task 12 parity and performance validation

Use the same Windows machine, model, prompt set, and GPU/backend settings for the legacy Butchi reference build and the .NET/Avalonia candidate build. Warm both applications before collecting measurements. Record at least 5 equivalent runs per application and compare medians.

## Required measurements

- Startup: process start to usable UI.
- Working set: steady-state RAM after warm startup.
- Inference: first-token latency for the same model and prompt.
- Preserve the JSON output from `scripts/benchmark-parity.ps1` as validation evidence.

## Windows x64 checklist

- Launch the packaged x64 build successfully.
- Double-Ctrl opens the popover from another application.
- Selected text is captured without modifying the clipboard unexpectedly.
- Translate produces a streamed result and can apply/copy it.
- Rewrite produces a streamed result and can apply/copy it.
- Cancel stops active generation cleanly.
- Settings, History, Models, and Status pages open and remain responsive.
- Repeat the old-vs-new benchmark for at least 5 warm runs and retain the median JSON result.

## Windows ARM64 checklist

- Launch the packaged ARM64 build successfully on Windows ARM64 hardware.
- Double-Ctrl opens the popover from another application.
- Selected text is captured without modifying the clipboard unexpectedly.
- Translate produces a streamed result and can apply/copy it.
- Rewrite produces a streamed result and can apply/copy it.
- Cancel stops active generation cleanly.
- Settings, History, Models, and Status pages open and remain responsive.
- Repeat the old-vs-new benchmark for at least 5 warm runs and retain the median JSON result.

## Evidence

Attach the workflow artifact named `task12-validation`. A final migration sign-off requires the automated contract/build/publish checks plus the x64 and ARM64 manual validation evidence above.
