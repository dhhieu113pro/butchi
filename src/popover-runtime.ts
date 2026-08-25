import "@fontsource-variable/space-grotesk";
import "@fontsource-variable/ibm-plex-sans";
import { Channel, invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { isScreenshotMode } from "./screenshot-mode";

const status = document.querySelector<HTMLElement>(".status");
const selection = document.querySelector<HTMLElement>(".selection");
const manualInput = document.querySelector<HTMLTextAreaElement>(".manual-input");
const popover = document.querySelector<HTMLElement>(".popover");
const closeBtn = document.querySelector<HTMLButtonElement>("#closeBtn");
const favoriteLanguageButtons = document.querySelector<HTMLElement>("#favoriteLanguageButtons");
const resultCards = {
  translate: document.querySelector<HTMLElement>('.result-card[data-kind="translate"]'),
  rewrite: document.querySelector<HTMLElement>('.result-card[data-kind="rewrite"]'),
};

const currentWindow = getCurrentWindow();
let hideSecondsMs = 6_000;
const interactedLeaveDelay = 3_000;
let hideTimer: number | undefined;
let hasInteraction = false;
let isManualInput = false;
let pointerInside = false;
let isProcessing = false;
let currentText = "";
let sourceText = "";
let translateEnabled = true;
let rewriteEnabled = true;
let resultAction = "copy";
let targetLanguage = "Vietnamese";
let favoriteLanguages: string[] = ["Vietnamese", "English"];
let autoRunId = 0;
let manualRunTimer: number | undefined;
let resizeFrame: number | undefined;
let lastRequestedHeight = 0;

type ResultKind = "translate" | "rewrite";

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
  popoverHideSeconds?: number;
};

function cancelScheduledHide() {
  if (hideTimer !== undefined) {
    window.clearTimeout(hideTimer);
    hideTimer = undefined;
  }
}

function scheduleHide(delay: number = hideSecondsMs) {
  if (isScreenshotMode || isProcessing) return;
  cancelScheduledHide();
  hideTimer = window.setTimeout(() => {
    hideTimer = undefined;
    if (isProcessing || pointerInside || popover?.matches(":hover")) return;
    void currentWindow.hide();
  }, delay);
}

function keepOpen() {
  hasInteraction = true;
  cancelScheduledHide();
}

function closePopover() {
  cancelScheduledHide();
  if (manualRunTimer !== undefined) {
    window.clearTimeout(manualRunTimer);
    manualRunTimer = undefined;
  }
  autoRunId += 1;
  isProcessing = false;
  void currentWindow.hide();
}

function requestPopoverResize() {
  if (isScreenshotMode || !popover) return;
  if (resizeFrame !== undefined) window.cancelAnimationFrame(resizeFrame);

  resizeFrame = window.requestAnimationFrame(() => {
    resizeFrame = undefined;
    const availableHeight = Math.max(240, Math.floor(window.screen.availHeight * 0.78));
    const targetHeight = Math.min(
      availableHeight,
      Math.max(180, Math.ceil(popover.scrollHeight + 2)),
    );

    if (Math.abs(targetHeight - lastRequestedHeight) < 3) return;
    lastRequestedHeight = targetHeight;
    void invoke("resize_popover", { height: targetHeight }).catch(() => undefined);
  });
}

if (popover && "ResizeObserver" in window) {
  const observer = new ResizeObserver(requestPopoverResize);
  observer.observe(popover);
}

function visibleResult(text: string): string {
  return text
    .replace(/<think>[\s\S]*?<\/think>/gi, "")
    .replace(/<think>[\s\S]*$/gi, "")
    .trim();
}

function cardParts(kind: ResultKind) {
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
  requestPopoverResize();
}

function showResultLoading(kind: ResultKind, label: string, clear = true) {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "loading";
  if (parts.state) parts.state.textContent = label;
  if (clear && parts.text) parts.text.textContent = "";
  requestPopoverResize();
}

function showResultQueued(kind: ResultKind) {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "queued";
  if (parts.state) parts.state.textContent = "Queued";
  if (parts.text) parts.text.textContent = "";
  requestPopoverResize();
}

function showResultOk(kind: ResultKind, text: string, message: string) {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "success";
  if (parts.state) parts.state.textContent = "Done";
  if (parts.text) {
    parts.text.textContent = visibleResult(text);
    parts.text.title = message;
  }
  requestPopoverResize();
}

function showResultError(kind: ResultKind, message: string) {
  const parts = cardParts(kind);
  if (!parts) return;
  parts.card.hidden = false;
  parts.card.dataset.state = "error";
  if (parts.state) parts.state.textContent = "Error";
  if (parts.text) {
    parts.text.textContent = message;
    parts.text.title = message;
  }
  requestPopoverResize();
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
    button.setAttribute("aria-pressed", String(language === targetLanguage));
    favoriteLanguageButtons.append(button);
  }
  favoriteLanguageButtons.hidden = favoriteLanguages.length === 0;
  requestPopoverResize();
}

function showSelection() {
  isManualInput = false;
  if (selection) selection.hidden = false;
  if (manualInput) manualInput.hidden = true;
}

async function runProcessStreaming(
  action: ResultKind,
  text: string,
  copy: boolean,
  onProgress: () => void,
): Promise<ProcessResult> {
  let sawProgress = false;
  const onEvent = new Channel<string>();
  onEvent.onmessage = () => {
    if (sawProgress) return;
    sawProgress = true;
    onProgress();
  };
  return invoke<ProcessResult>("process_text_stream", { action, text, copy, onEvent });
}

async function replaceSelection(text: string): Promise<void> {
  if (isManualInput) {
    throw new Error("Replace is available when Butchi was opened from selected text.");
  }
  await invoke("replace_selected_text", { text });
}

async function applyResultAction(result: ProcessResult): Promise<string> {
  if (resultAction === "replace" && !isManualInput) {
    await replaceSelection(result.text);
    return "Selected text replaced.";
  }
  if (resultAction === "copy") {
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

async function refreshConfig() {
  try {
    const cfg = await invoke<AppConfig>("get_config");
    translateEnabled = cfg.translateEnabled;
    rewriteEnabled = cfg.rewriteEnabled;
    resultAction = cfg.resultAction || "copy";
    targetLanguage = cfg.targetLanguage || "Vietnamese";
    favoriteLanguages = cfg.favoriteLanguages ?? ["Vietnamese", "English"];
    const seconds = Number(cfg.popoverHideSeconds ?? 6);
    hideSecondsMs = Math.min(30, Math.max(2, Number.isFinite(seconds) ? seconds : 6)) * 1_000;
    renderFavoriteLanguages();
  } catch {
    // Keep safe defaults when configuration cannot be loaded.
  }
}

async function autoRunEnabled(text: string) {
  const source = text.trim();
  if (!source) {
    if (status) status.textContent = "No text to process.";
    return;
  }

  const runId = ++autoRunId;
  cancelScheduledHide();
  isProcessing = true;
  const enabledCount = Number(translateEnabled) + Number(rewriteEnabled);
  const applyAutomation = enabledCount === 1;
  const shouldCopy = applyAutomation && resultAction === "copy";
  let firstOk: ProcessResult | null = null;

  try {
    if (!translateEnabled && !rewriteEnabled) {
      if (status) status.textContent = "Both Translate and Rewrite are disabled in Settings.";
      return;
    }

    if (translateEnabled) {
      showResultLoading("translate", "Translating…");
      if (rewriteEnabled) showResultQueued("rewrite");
      if (status) status.textContent = "Translating…";

      try {
        const result = await runProcessStreaming("translate", source, shouldCopy, () => {
          if (runId !== autoRunId) return;
          showResultLoading("translate", "Generating…", false);
        });
        if (runId !== autoRunId) return;
        result.text = visibleResult(result.text);
        showResultOk("translate", result.text, result.message);
        firstOk ??= result;
      } catch (error) {
        if (runId !== autoRunId) return;
        showResultError("translate", error instanceof Error ? error.message : String(error));
      }
    }

    if (rewriteEnabled) {
      showResultLoading("rewrite", "Rewriting…");
      if (status) status.textContent = translateEnabled ? "Rewriting…" : "Auto-rewriting…";

      try {
        const result = await runProcessStreaming("rewrite", source, shouldCopy, () => {
          if (runId !== autoRunId) return;
          showResultLoading("rewrite", "Generating…", false);
        });
        if (runId !== autoRunId) return;
        result.text = visibleResult(result.text);
        showResultOk("rewrite", result.text, result.message);
        firstOk ??= result;
      } catch (error) {
        if (runId !== autoRunId) return;
        showResultError("rewrite", error instanceof Error ? error.message : String(error));
      }
    }

    if (runId !== autoRunId) return;
    if (applyAutomation && firstOk) {
      try {
        const applied = await applyResultAction(firstOk);
        if (status) status.textContent = applied;
      } catch (error) {
        if (status) status.textContent = error instanceof Error ? error.message : String(error);
      }
    } else if (firstOk && status) {
      status.textContent = "Results ready.";
    } else if (status) {
      status.textContent = "No result was produced.";
    }
  } finally {
    if (runId === autoRunId) {
      isProcessing = false;
      requestPopoverResize();
      scheduleHide(hideSecondsMs);
    }
  }
}

async function rerunTranslation(language: string) {
  const text = sourceText.trim() || currentText.trim();
  if (!text || !language) return;

  keepOpen();
  const runId = ++autoRunId;
  isProcessing = true;
  targetLanguage = language;
  renderFavoriteLanguages();
  showResultLoading("translate", "Translating…");
  if (status) status.textContent = `Translating to ${language}…`;

  try {
    const saved = await invoke<AppConfig>("set_target_language", { language });
    if (runId !== autoRunId) return;
    targetLanguage = saved.targetLanguage;
    favoriteLanguages = saved.favoriteLanguages ?? favoriteLanguages;
    renderFavoriteLanguages();

    const shouldCopy = resultAction === "copy";
    const result = await runProcessStreaming("translate", text, shouldCopy, () => {
      if (runId !== autoRunId) return;
      showResultLoading("translate", "Generating…", false);
    });
    if (runId !== autoRunId) return;
    result.text = visibleResult(result.text);
    showResultOk("translate", result.text, result.message);
    const applied = await applyResultAction(result);
    if (status) status.textContent = `Translated to ${targetLanguage}. ${applied}`;
  } catch (error) {
    if (runId !== autoRunId) return;
    const message = error instanceof Error ? error.message : String(error);
    showResultError("translate", message);
    if (status) status.textContent = message;
  } finally {
    if (runId === autoRunId) {
      isProcessing = false;
      requestPopoverResize();
      scheduleHide(hideSecondsMs);
    }
  }
}

closeBtn?.addEventListener("click", (event) => {
  event.preventDefault();
  event.stopPropagation();
  closePopover();
});

popover?.addEventListener("pointerenter", () => {
  pointerInside = true;
  keepOpen();
});
popover?.addEventListener("pointerdown", keepOpen);
popover?.addEventListener("focusin", keepOpen);
popover?.addEventListener("pointerleave", () => {
  pointerInside = false;
  if (isProcessing || document.activeElement === manualInput) return;
  scheduleHide(hasInteraction ? Math.max(interactedLeaveDelay, hideSecondsMs / 2) : hideSecondsMs);
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    event.preventDefault();
    closePopover();
    return;
  }
  keepOpen();
});

favoriteLanguageButtons?.addEventListener("click", (event) => {
  const button = (event.target as HTMLElement).closest<HTMLButtonElement>("[data-language]");
  const language = button?.dataset.language;
  if (!language) return;
  void rerunTranslation(language);
});

manualInput?.addEventListener("input", () => {
  keepOpen();
  currentText = manualInput.value;
  sourceText = manualInput.value;
  autoRunId += 1;
  isProcessing = false;

  if (manualRunTimer !== undefined) {
    window.clearTimeout(manualRunTimer);
    manualRunTimer = undefined;
  }

  const value = manualInput.value.trim();
  if (!value) {
    hideResults();
    if (status) status.textContent = "Type or paste text. Butchi runs automatically.";
    return;
  }

  if (status) status.textContent = "Waiting for input…";
  manualRunTimer = window.setTimeout(() => {
    manualRunTimer = undefined;
    void autoRunEnabled(value);
  }, 550);
});

window.addEventListener("blur", () => {
  if (isManualInput && !isProcessing) scheduleHide(hideSecondsMs);
});

if (!isScreenshotMode) {
  void refreshConfig();

  void listen<string>("selection-captured", ({ payload }) => {
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
      requestPopoverResize();
      void autoRunEnabled(payload.trim());
    });
  });

  void listen<string>("selection-capture-failed", ({ payload }) => {
    cancelScheduledHide();
    hasInteraction = false;
    currentText = "";
    sourceText = "";
    autoRunId += 1;
    isProcessing = false;
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
    requestPopoverResize();
    scheduleHide(hideSecondsMs);
  });

  void listen<string>("manual-input-requested", () => {
    cancelScheduledHide();
    hasInteraction = true;
    isManualInput = true;
    currentText = "";
    sourceText = "";
    autoRunId += 1;
    isProcessing = false;
    void refreshConfig();
    hideResults();
    if (selection) selection.hidden = true;
    if (manualInput) {
      manualInput.hidden = false;
      manualInput.value = "";
    }
    if (status) {
      status.textContent = "Type or paste text. Butchi runs automatically.";
      status.removeAttribute("title");
    }
    requestPopoverResize();
    window.requestAnimationFrame(() => manualInput?.focus());
  });
}
