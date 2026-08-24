import "@fontsource-variable/space-grotesk";
import "@fontsource-variable/ibm-plex-sans";
import { Channel, invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { isScreenshotMode } from "./screenshot-mode";

const status = document.querySelector<HTMLElement>(".status");
const actions = document.querySelectorAll<HTMLButtonElement>(".action");
const selection = document.querySelector<HTMLElement>(".selection");
const manualInput = document.querySelector<HTMLTextAreaElement>(".manual-input");
const popover = document.querySelector<HTMLElement>(".popover");
const favoriteLanguageButtons = document.querySelector<HTMLElement>("#favoriteLanguageButtons");
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
let sourceText = "";
let translateEnabled = true;
let rewriteEnabled = true;
let resultAction = "copy";
let targetLanguage = "Vietnamese";
let favoriteLanguages: string[] = ["Vietnamese", "English"];
let autoRunId = 0;

type ProcessResult = {
  text: string;
  message: string;
  copied: boolean;
};

type AppConfig = {
  translateEnabled: boolean;
  rewriteEnabled: boolean;
  resultAction: string;
  targetLanguage: string;
  favoriteLanguages: string[];
};

function cancelScheduledHide() {
  if (hideTimer !== undefined) {
    window.clearTimeout(hideTimer);
    hideTimer = undefined;
  }
}

function scheduleHide(delay: number) {
  // Keep the capture window open for the full CI screenshot session.
  if (isScreenshotMode) return;
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

function renderFavoriteLanguages() {
  if (!favoriteLanguageButtons) return;
  favoriteLanguageButtons.innerHTML = "";
  for (const language of favoriteLanguages.slice(0, 5)) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "language-target";
    button.dataset.language = language;
    button.textContent = language;
    button.title = `Translate again to ${language}`;
    button.setAttribute("aria-pressed", language === targetLanguage ? "true" : "false");
    favoriteLanguageButtons.append(button);
  }
  favoriteLanguageButtons.hidden = favoriteLanguages.length === 0;
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

function showResultChunk(kind: "translate" | "rewrite", chunk: string) {
  const parts = cardParts(kind);
  if (!parts || !chunk) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "loading";
  if (parts.state) parts.state.textContent = "Generating…";
  if (parts.text) {
    parts.text.textContent = (parts.text.textContent ?? "") + chunk;
  }
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

async function runProcessStreaming(
  action: "translate" | "rewrite",
  text: string,
  copy: boolean,
  onChunk: (chunk: string) => void,
): Promise<ProcessResult> {
  const onEvent = new Channel<string>();
  onEvent.onmessage = onChunk;
  return invoke<ProcessResult>("process_text_stream", { action, text, copy, onEvent });
}

async function replaceSelection(text: string): Promise<void> {
  if (isManualInput) {
    throw new Error("Replace is available when Butchi was opened from a selected text.");
  }
  await invoke("replace_selected_text", { text });
}

/** Apply Settings → result action (copy / replace / none) to a finished result. */
async function applyResultAction(result: ProcessResult): Promise<string> {
  if (resultAction === "replace" && !isManualInput) {
    await replaceSelection(result.text);
    return "Selected text replaced.";
  }
  if (resultAction === "copy") {
    // Backend already copied when `copy: true` was passed; otherwise copy here.
    if (!result.copied) {
      try {
        await navigator.clipboard.writeText(result.text);
      } catch {
        return "Result ready (clipboard copy failed).";
      }
    }
    return "Result copied.";
  }
  return "Result ready.";
}

async function rerunTranslation(language: string) {
  const text = sourceText.trim() || activeText();
  if (!text || !language) return;

  keepOpen();
  const runId = ++autoRunId;
  targetLanguage = language;
  renderFavoriteLanguages();
  showResultLoading("translate");
  if (status) status.textContent = `Translating to ${language}…`;

  try {
    const saved = await invoke<AppConfig>("set_target_language", { language });
    if (runId !== autoRunId) return;
    targetLanguage = saved.targetLanguage;
    favoriteLanguages = saved.favoriteLanguages ?? favoriteLanguages;
    renderFavoriteLanguages();

    const shouldCopy = resultAction === "copy";
    const result = await runProcessStreaming("translate", text, shouldCopy, (chunk) => {
      if (runId !== autoRunId) return;
      showResultChunk("translate", chunk);
    });
    if (runId !== autoRunId) return;
    showResultOk("translate", result.text, result.message);
    const applied = await applyResultAction(result);
    if (status) status.textContent = `Translated to ${targetLanguage}. ${applied}`;
    scheduleHide(interactedLeaveDelay + 2_000);
  } catch (error) {
    if (runId !== autoRunId) return;
    const message = error instanceof Error ? error.message : String(error);
    showResultError("translate", message);
    if (status) status.textContent = message;
  }
}

async function autoRunEnabled(text: string) {
  const runId = ++autoRunId;
  const jobs: Array<Promise<ProcessResult | null>> = [];
  const enabledCount = Number(translateEnabled) + Number(rewriteEnabled);
  // Only auto-apply copy/replace when a single action is enabled (avoid fighting over clipboard/selection).
  const applyAutomation = enabledCount === 1;
  const shouldCopy = applyAutomation && resultAction === "copy";

  if (translateEnabled) {
    showResultLoading("translate");
    jobs.push(
      runProcessStreaming("translate", text, shouldCopy, (chunk) => {
        if (runId !== autoRunId) return;
        showResultChunk("translate", chunk);
      })
        .then((result) => {
          if (runId !== autoRunId) return null;
          showResultOk("translate", result.text, result.message);
          return result;
        })
        .catch((error) => {
          if (runId !== autoRunId) return null;
          showResultError("translate", error instanceof Error ? error.message : String(error));
          return null;
        }),
    );
  }

  if (rewriteEnabled) {
    showResultLoading("rewrite");
    jobs.push(
      runProcessStreaming("rewrite", text, shouldCopy, (chunk) => {
        if (runId !== autoRunId) return;
        showResultChunk("rewrite", chunk);
      })
        .then((result) => {
          if (runId !== autoRunId) return null;
          showResultOk("rewrite", result.text, result.message);
          return result;
        })
        .catch((error) => {
          if (runId !== autoRunId) return null;
          showResultError("rewrite", error instanceof Error ? error.message : String(error));
          return null;
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

  const results = await Promise.all(jobs);
  if (runId !== autoRunId) return;

  const firstOk = results.find((r): r is ProcessResult => r !== null);
  if (applyAutomation && firstOk) {
    try {
      const applied = await applyResultAction(firstOk);
      if (status) status.textContent = applied;
    } catch (error) {
      if (status) status.textContent = error instanceof Error ? error.message : String(error);
    }
  } else if (status) {
    status.textContent = "Results ready.";
  }
  scheduleHide(untouchedHideDelay + 2_000);
}

async function refreshConfig() {
  try {
    const cfg = await invoke<AppConfig>("get_config");
    translateEnabled = cfg.translateEnabled;
    rewriteEnabled = cfg.rewriteEnabled;
    resultAction = cfg.resultAction || "copy";
    targetLanguage = cfg.targetLanguage || "Vietnamese";
    favoriteLanguages = cfg.favoriteLanguages ?? ["Vietnamese", "English"];
    applyActionVisibility();
    renderFavoriteLanguages();
  } catch {
    /* keep defaults */
  }
}

void refreshConfig();

listen<string>("selection-captured", ({ payload }) => {
  cancelScheduledHide();
  hasInteraction = false;
  currentText = payload;
  sourceText = payload;
  void invoke("remember_selection_target").catch(() => undefined);
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
  sourceText = "";
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
  sourceText = "";
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
  sourceText = manualInput.value;
  setActionAvailability(manualInput.value.trim().length > 0);
});

favoriteLanguageButtons?.addEventListener("click", (event) => {
  const button = (event.target as HTMLElement).closest<HTMLButtonElement>("[data-language]");
  const language = button?.dataset.language;
  if (!language) return;
  void rerunTranslation(language);
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
      const shouldCopy = resultAction === "copy";
      const result = await runProcessStreaming(action, text, shouldCopy, (chunk) => {
        showResultChunk(action, chunk);
      });

      currentText = result.text;
      if (selection && !isManualInput) {
        selection.textContent = result.text;
        selection.title = result.text;
      }
      if (manualInput && isManualInput) {
        manualInput.value = result.text;
      }
      showResultOk(action, result.text, result.message);
      const applied = await applyResultAction(result);
      if (status) {
        status.textContent = applied;
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
