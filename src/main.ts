import "@fontsource-variable/space-grotesk";
import "@fontsource-variable/ibm-plex-sans";
import { listen } from "@tauri-apps/api/event";
import { invoke } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";

const status = document.querySelector<HTMLElement>(".status");
const actions = document.querySelectorAll<HTMLButtonElement>(".action");
const selection = document.querySelector<HTMLElement>(".selection");
const manualInput = document.querySelector<HTMLTextAreaElement>(".manual-input");
const popover = document.querySelector<HTMLElement>(".popover");
const resultCards = {
  translate: document.querySelector<HTMLElement>('.result-card[data-kind="translate"]'),
  rewrite: document.querySelector<HTMLElement>('.result-card[data-kind="rewrite"]'),
};

const currentWindow = getCurrentWindow();
const untouchedHideDelay = 4_000;
const interactedLeaveDelay = 3_000;
let hideTimer: number | undefined;
let hasInteraction = false;
let isManualInput = false;
let pointerInside = false;
let currentText = "";
let translateEnabled = true;
let rewriteEnabled = true;
let autoRunId = 0;

type ProcessResult = {
  text: string;
  message: string;
  copied: boolean;
};

type AppConfig = {
  translateEnabled: boolean;
  rewriteEnabled: boolean;
};

function cancelScheduledHide() {
  if (hideTimer !== undefined) {
    window.clearTimeout(hideTimer);
    hideTimer = undefined;
  }
}

function scheduleHide(delay: number) {
  cancelScheduledHide();
  hideTimer = window.setTimeout(() => {
    hideTimer = undefined;
    if (pointerInside || popover?.matches(":hover")) return;
    void currentWindow.hide();
  }, delay);
}

function keepOpen() {
  hasInteraction = true;
  cancelScheduledHide();
}

popover?.addEventListener("pointerenter", () => {
  pointerInside = true;
  keepOpen();
});
popover?.addEventListener("pointerdown", keepOpen);
popover?.addEventListener("focusin", keepOpen);
document.addEventListener("keydown", keepOpen);
popover?.addEventListener("pointerleave", () => {
  pointerInside = false;
  if (document.activeElement === manualInput) return;
  scheduleHide(hasInteraction ? interactedLeaveDelay : untouchedHideDelay);
});

function applyActionVisibility() {
  actions.forEach((button) => {
    const action = button.dataset.action;
    const enabled =
      action === "translate" ? translateEnabled : action === "rewrite" ? rewriteEnabled : true;
    button.hidden = !enabled;
  });
}

function setActionAvailability(enabled: boolean) {
  actions.forEach((button) => {
    if (button.hidden) return;
    button.dataset.state = "default";
    button.disabled = !enabled;
  });
}

function showSelection() {
  isManualInput = false;
  if (selection) selection.hidden = false;
  if (manualInput) manualInput.hidden = true;
}

function activeText(): string {
  if (isManualInput) {
    return manualInput?.value.trim() ?? "";
  }
  return currentText.trim();
}

function cardParts(kind: "translate" | "rewrite") {
  const card = resultCards[kind];
  if (!card) return null;
  return {
    card,
    state: card.querySelector<HTMLElement>('[data-role="state"]'),
    text: card.querySelector<HTMLElement>('[data-role="text"]'),
  };
}

function hideResults() {
  for (const kind of ["translate", "rewrite"] as const) {
    const parts = cardParts(kind);
    if (!parts) continue;
    parts.card.hidden = true;
    parts.card.dataset.state = "idle";
    if (parts.state) parts.state.textContent = "";
    if (parts.text) parts.text.textContent = "";
  }
}

function showResultLoading(kind: "translate" | "rewrite") {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "loading";
  if (parts.state) parts.state.textContent = "Running…";
  if (parts.text) parts.text.textContent = "";
}

function showResultOk(kind: "translate" | "rewrite", text: string, message: string) {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "success";
  if (parts.state) parts.state.textContent = "Done";
  if (parts.text) {
    parts.text.textContent = text;
    parts.text.title = message;
  }
}

function showResultError(kind: "translate" | "rewrite", message: string) {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "error";
  if (parts.state) parts.state.textContent = "Error";
  if (parts.text) {
    parts.text.textContent = message;
    parts.text.title = message;
  }
}

async function runProcess(
  action: "translate" | "rewrite",
  text: string,
  copy: boolean,
): Promise<ProcessResult> {
  return invoke<ProcessResult>("process_text", { action, text, copy });
}

async function autoRunEnabled(text: string) {
  const runId = ++autoRunId;
  const jobs: Array<Promise<void>> = [];

  if (translateEnabled) {
    showResultLoading("translate");
    jobs.push(
      runProcess("translate", text, false)
        .then((result) => {
          if (runId !== autoRunId) return;
          showResultOk("translate", result.text, result.message);
        })
        .catch((error) => {
          if (runId !== autoRunId) return;
          showResultError("translate", error instanceof Error ? error.message : String(error));
        }),
    );
  }

  if (rewriteEnabled) {
    showResultLoading("rewrite");
    jobs.push(
      runProcess("rewrite", text, false)
        .then((result) => {
          if (runId !== autoRunId) return;
          showResultOk("rewrite", result.text, result.message);
        })
        .catch((error) => {
          if (runId !== autoRunId) return;
          showResultError("rewrite", error instanceof Error ? error.message : String(error));
        }),
    );
  }

  if (!jobs.length) {
    if (status) status.textContent = "Both Translate and Rewrite are disabled in Settings.";
    return;
  }

  if (status) {
    status.textContent =
      translateEnabled && rewriteEnabled
        ? "Auto-running Translate + Rewrite…"
        : translateEnabled
          ? "Auto-translating…"
          : "Auto-rewriting…";
  }

  await Promise.all(jobs);
  if (runId !== autoRunId) return;

  if (status) status.textContent = "Results ready — hover to keep open, or use Copy.";
  scheduleHide(untouchedHideDelay + 2_000);
}

async function refreshConfig() {
  try {
    const cfg = await invoke<AppConfig>("get_config");
    translateEnabled = cfg.translateEnabled;
    rewriteEnabled = cfg.rewriteEnabled;
    applyActionVisibility();
  } catch {
    /* keep defaults */
  }
}

void refreshConfig();

listen<string>("selection-captured", ({ payload }) => {
  cancelScheduledHide();
  hasInteraction = false;
  currentText = payload;
  void refreshConfig().then(() => {
    showSelection();
    hideResults();
    if (selection) {
      selection.textContent = payload;
      selection.title = payload;
    }
    if (status) {
      status.textContent = "Working…";
      status.removeAttribute("title");
    }
    setActionAvailability(true);
    scheduleHide(untouchedHideDelay + 4_000);
    void autoRunEnabled(payload.trim());
  });
});

listen<string>("selection-capture-failed", ({ payload }) => {
  cancelScheduledHide();
  hasInteraction = false;
  currentText = "";
  autoRunId += 1;
  showSelection();
  hideResults();
  if (selection) {
    selection.textContent = "No text selected";
    selection.title = payload;
  }
  if (status) {
    status.textContent = payload;
    status.title = payload;
  }
  setActionAvailability(false);
  scheduleHide(untouchedHideDelay);
});

listen<string>("manual-input-requested", () => {
  cancelScheduledHide();
  hasInteraction = true;
  isManualInput = true;
  currentText = "";
  autoRunId += 1;
  void refreshConfig();
  hideResults();
  if (selection) selection.hidden = true;
  if (manualInput) {
    manualInput.hidden = false;
    manualInput.value = "";
  }
  if (status) {
    status.textContent = "Type or paste text, then choose an action.";
    status.removeAttribute("title");
  }
  setActionAvailability(false);
  window.requestAnimationFrame(() => manualInput?.focus());
});

manualInput?.addEventListener("input", () => {
  keepOpen();
  currentText = manualInput.value;
  setActionAvailability(manualInput.value.trim().length > 0);
});

window.addEventListener("blur", () => {
  if (isManualInput) scheduleHide(untouchedHideDelay);
});

function resetActions(active: HTMLButtonElement) {
  actions.forEach((button) => {
    if (button !== active && !button.hidden) {
      button.dataset.state = "default";
      button.disabled = false;
    }
  });
}

actions.forEach((button) => {
  button.dataset.state = "default";
  button.addEventListener("click", async () => {
    const text = activeText();
    if (!text) return;

    const action = (button.dataset.action ?? "rewrite") as "translate" | "rewrite";
    resetActions(button);
    button.dataset.state = "loading";
    button.disabled = true;
    keepOpen();
    showResultLoading(action);

    if (status) {
      status.textContent = action === "rewrite" ? "Rewriting…" : "Translating…";
      status.removeAttribute("title");
    }

    try {
      const result = await runProcess(action, text, true);
      currentText = result.text;
      if (selection && !isManualInput) {
        selection.textContent = result.text;
        selection.title = result.text;
      }
      if (manualInput && isManualInput) {
        manualInput.value = result.text;
      }
      showResultOk(action, result.text, result.message);
      if (status) {
        status.textContent = result.message;
        status.title = result.message;
      }
      button.dataset.state = "success";
      button.disabled = false;
      setActionAvailability(true);
      scheduleHide(interactedLeaveDelay + 1_500);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      showResultError(action, message);
      if (status) {
        status.textContent = message;
        status.title = message;
      }
      button.dataset.state = "error";
      button.disabled = false;
      setActionAvailability(true);
    }
  });
});

document.querySelectorAll<HTMLButtonElement>("[data-copy]").forEach((button) => {
  button.addEventListener("click", async () => {
    const kind = button.dataset.copy as "translate" | "rewrite" | undefined;
    if (!kind) return;
    const parts = cardParts(kind);
    const text = parts?.text?.textContent?.trim() ?? "";
    if (!text) return;
    keepOpen();
    try {
      await navigator.clipboard.writeText(text);
      if (status) status.textContent = `Copied ${kind} result.`;
    } catch {
      if (status) status.textContent = "Clipboard copy failed.";
    }
  });
});
