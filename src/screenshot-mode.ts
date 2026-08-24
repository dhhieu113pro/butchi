const mode = new URLSearchParams(window.location.search).get("screenshot");

/** True when the real app is running under BUTCHI_SCREENSHOT_MODE / ?screenshot=. */
export const isScreenshotMode = Boolean(mode);

function forceTheme(dark: boolean): void {
  const theme = dark ? "dark" : "light";
  document.documentElement.dataset.theme = theme;
  localStorage.setItem("butchi.theme", theme);
}

function seedPopover(): void {
  const source = document.querySelector<HTMLElement>(".selection");
  const status = document.querySelector<HTMLElement>(".status");
  const translate = document.querySelector<HTMLElement>('.result-card[data-kind="translate"]');
  const rewrite = document.querySelector<HTMLElement>('.result-card[data-kind="rewrite"]');
  const favorites = document.querySelector<HTMLElement>("#favoriteLanguageButtons");

  if (source) {
    source.hidden = false;
    source.textContent = "Could you send the updated report by Friday?";
    source.title = source.textContent;
  }

  const setResult = (card: HTMLElement | null, text: string): void => {
    if (!card) return;
    card.hidden = false;
    card.dataset.state = "success";
    const state = card.querySelector<HTMLElement>('[data-role="state"]');
    const result = card.querySelector<HTMLElement>('[data-role="text"]');
    if (state) state.textContent = "";
    if (result) result.textContent = text;
  };

  setResult(translate, "Bạn có thể gửi báo cáo đã cập nhật trước thứ Sáu không?");
  setResult(rewrite, "Could you please send the updated report by Friday?");

  if (favorites) {
    favorites.hidden = false;
    favorites.innerHTML =
      '<button type="button" class="language-target" aria-pressed="true">Vietnamese</button>' +
      '<button type="button" class="language-target" aria-pressed="false">English</button>';
  }

  if (status) status.textContent = "Results ready — use Copy or Replace.";
}

function seedSettings(): void {
  document.querySelectorAll<HTMLElement>("[data-settings-panel]").forEach((panel) => {
    panel.hidden = panel.dataset.settingsPanel !== "general";
  });
  document.querySelectorAll<HTMLButtonElement>("[data-settings-page]").forEach((button) => {
    button.setAttribute("aria-selected", String(button.dataset.settingsPage === "general"));
  });

  const theme = document.querySelector<HTMLSelectElement>("#themePreference");
  const translateEnabled = document.querySelector<HTMLInputElement>("#translateEnabled");
  const rewriteEnabled = document.querySelector<HTMLInputElement>("#rewriteEnabled");
  const targetLanguage = document.querySelector<HTMLSelectElement>("#targetLanguage");
  const resultAction = document.querySelector<HTMLSelectElement>("#resultAction");

  if (theme) theme.value = mode?.endsWith("dark") ? "dark" : "light";
  if (translateEnabled) translateEnabled.checked = true;
  if (rewriteEnabled) rewriteEnabled.checked = true;
  if (targetLanguage) targetLanguage.value = "Vietnamese";
  if (resultAction) resultAction.value = "copy";
}

if (mode) {
  forceTheme(mode.endsWith("dark"));
  document.documentElement.dataset.screenshot = mode;

  // Keep the document title aligned with the native window title used by CI FindWindow.
  if (mode.startsWith("popover-")) {
    document.title = "Butchi — Text actions";
  } else if (mode.startsWith("settings-")) {
    document.title = "Butchi — Settings";
  }

  const run = (): void => {
    if (mode.startsWith("popover-")) seedPopover();
    if (mode.startsWith("settings-")) {
      seedSettings();
      // Settings binds async config; re-seed shortly after load.
      window.setTimeout(seedSettings, 500);
      window.setTimeout(seedSettings, 1200);
    }
  };

  if (document.readyState === "loading") {
    window.addEventListener("DOMContentLoaded", run);
  } else {
    run();
  }
}
