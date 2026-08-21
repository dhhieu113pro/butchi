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
const currentWindow = getCurrentWindow();
const untouchedHideDelay = 2_000;
const interactedLeaveDelay = 2_000;
let hideTimer: number | undefined;
let hasInteraction = false;
let isManualInput = false;
let pointerInside = false;
let currentText = "";

type ProcessResult = {
  text: string;
  message: string;
  copied: boolean;
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

function setActionAvailability(enabled: boolean) {
  actions.forEach((button) => {
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

listen<string>("selection-captured", ({ payload }) => {
  cancelScheduledHide();
  hasInteraction = false;
  currentText = payload;
  showSelection();
  if (selection) {
    selection.textContent = payload;
    selection.title = payload;
  }
  if (status) {
    status.textContent = "Choose an action for the selected text.";
    status.removeAttribute("title");
  }
  setActionAvailability(true);
  scheduleHide(untouchedHideDelay);
});

listen<string>("selection-capture-failed", ({ payload }) => {
  cancelScheduledHide();
  hasInteraction = false;
  currentText = "";
  showSelection();
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
    if (button !== active) {
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

    const action = button.dataset.action ?? "rewrite";
    resetActions(button);
    button.dataset.state = "loading";
    button.disabled = true;
    keepOpen();

    if (status) {
      status.textContent = action === "rewrite" ? "Rewriting…" : "Preparing translation…";
      status.removeAttribute("title");
    }

    try {
      const result = await invoke<ProcessResult>("process_text", { action, text });
      currentText = result.text;
      if (selection && !isManualInput) {
        selection.textContent = result.text;
        selection.title = result.text;
      }
      if (manualInput && isManualInput) {
        manualInput.value = result.text;
      }
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
