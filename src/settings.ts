import "@fontsource-variable/space-grotesk";
import "@fontsource-variable/ibm-plex-sans";
import { getVersion } from "@tauri-apps/api/app";
import { invoke } from "@tauri-apps/api/core";

type SettingsPage = "general" | "prompts" | "model" | "history" | "about";
type PromptMode = "translate" | "rewrite";
type AppConfig = {
  translateEnabled: boolean; rewriteEnabled: boolean; targetLanguage: string; favoriteLanguages: string[];
  rewriteSystemPrompt: string; translateSystemPrompt: string; resultAction: string; backendPreference: string;
  modelRepo: string; modelFile: string; maxTokens: number; temperature: number; gpuLayers: number; historyRetentionDays: number;
};
type ModelOption = { id: string; label: string; repo: string; file: string; sizeHint: string };
type BackendDevice = { id: string; name: string; backend: string; description: string };
type HistoryEntry = { id: string; ts: number; action: string; source: string; result: string; message: string; targetLanguage: string | null };
type ModelStatus = { downloaded: boolean; loaded: boolean; localPath: string | null; repo: string; file: string; gpuFeature: string; backend: string; devices: BackendDevice[]; gpuOffloadAvailable: boolean; maxDevices: number };

const $ = <T extends Element>(selector: string) => document.querySelector<T>(selector);
const translateEnabled = $("#translateEnabled") as HTMLInputElement | null;
const rewriteEnabled = $("#rewriteEnabled") as HTMLInputElement | null;
const targetLanguage = $("#targetLanguage") as HTMLSelectElement | null;
const favoriteLanguages = $("#favoriteLanguages") as HTMLSelectElement | null;
const resultAction = $("#resultAction") as HTMLSelectElement | null;
const backendPreference = $("#backendPreference") as HTMLSelectElement | null;
const translateSystemPrompt = $("#translateSystemPrompt") as HTMLTextAreaElement | null;
const rewriteSystemPrompt = $("#rewriteSystemPrompt") as HTMLTextAreaElement | null;
const modelSelect = $("#modelSelect") as HTMLSelectElement | null;
const modelStatus = $("#modelStatus") as HTMLElement | null;
const gettingStartedCard = $("#gettingStartedCard") as HTMLElement | null;
const appVersion = $("#appVersion") as HTMLElement | null;
const backendPills = document.querySelectorAll<HTMLElement>("#backendPills .pill");
const deviceList = $("#deviceList") as HTMLUListElement | null;
const backendHint = $("#backendHint") as HTMLElement | null;
const downloadBtn = $("#downloadBtn") as HTMLButtonElement | null;
const loadBtn = $("#loadBtn") as HTMLButtonElement | null;
const clearLocalDataBtn = $("#clearLocalDataBtn") as HTMLButtonElement | null;
const maxTokens = $("#maxTokens") as HTMLInputElement | null;
const temperature = $("#temperature") as HTMLInputElement | null;
const gpuLayers = $("#gpuLayers") as HTMLInputElement | null;
const saveStatus = $("#saveStatus") as HTMLElement | null;
const historyList = $("#historyList") as HTMLElement | null;
const historySearch = $("#historySearch") as HTMLInputElement | null;
const historyAction = $("#historyAction") as HTMLSelectElement | null;
const historyRetentionDays = $("#historyRetentionDays") as HTMLSelectElement | null;
const refreshHistoryBtn = $("#refreshHistoryBtn") as HTMLButtonElement | null;
const clearHistoryBtn = $("#clearHistoryBtn") as HTMLButtonElement | null;
const themePreference = $("#themePreference") as HTMLSelectElement | null;
const modelAdvancedToggle = $("#modelAdvancedToggle") as HTMLButtonElement | null;
const modelAdvanced = $("#modelAdvanced") as HTMLElement | null;

let models: ModelOption[] = [];
let config: AppConfig | null = null;
let historySearchTimer: number | undefined;
let saveTimer: number | undefined;
let savedMessageTimer: number | undefined;
let initializing = true;

const translationProfiles: Record<string, string> = {
  natural: "You are a precise translation assistant. Translate the user's text into the target language. Keep meaning and tone. Output only the translation with no quotes or explanation.",
  literal: "Translate the user's text into the target language as literally and accurately as possible. Preserve names, numbers, terminology, sentence structure, and formatting when practical. Do not add, omit, explain, or rewrite ideas. Output only the translation.",
  professional: "Translate the user's text into the target language using polished, professional, natural wording suitable for workplace communication. Preserve the original meaning, intent, terminology, and level of formality. Output only the translation.",
  concise: "Translate the user's text into the target language naturally and concisely. Preserve all important meaning while removing unnecessary verbosity only when it does not change intent. Output only the translation.",
};
const rewriteProfiles: Record<string, string> = {
  natural: "You are a precise writing assistant. Rewrite the user's text so it is clear, natural, and grammatically correct. Keep the original meaning and language. Output only the rewritten text with no quotes or explanation.",
  grammar: "Correct only grammar, spelling, punctuation, capitalization, and obvious word-choice errors in the user's text. Preserve the original wording, structure, tone, meaning, formatting, and language as much as possible. Output only the corrected text.",
  professional: "Rewrite the user's text in a clear, polished, professional tone suitable for workplace communication. Preserve the original meaning and language. Avoid unnecessary jargon or extra detail. Output only the rewritten text.",
  shorter: "Rewrite the user's text to be shorter and more direct while preserving the important meaning, intent, tone, and language. Remove repetition and unnecessary words. Output only the rewritten text.",
  polite: "Rewrite the user's text to sound more polite, respectful, and natural while preserving its meaning, intent, and language. Do not make it overly formal unless the original context requires it. Output only the rewritten text.",
  simple: "Rewrite the user's text using simple, clear, easy-to-understand language while preserving the original meaning and language. Prefer short sentences and common words. Output only the rewritten text.",
};

function setSettingsPage(page: SettingsPage): void {
  document.querySelectorAll<HTMLElement>("[data-settings-panel]").forEach((panel) => { panel.hidden = panel.dataset.settingsPanel !== page; });
  document.querySelectorAll<HTMLButtonElement>("[data-settings-page]").forEach((button) => { button.setAttribute("aria-selected", String(button.dataset.settingsPage === page)); });
}

function setPromptMode(mode: PromptMode): void {
  document.querySelectorAll<HTMLElement>("[data-prompt-panel]").forEach((panel) => { panel.hidden = panel.dataset.promptPanel !== mode; });
  document.querySelectorAll<HTMLButtonElement>("[data-prompt-mode]").forEach((button) => { button.setAttribute("aria-selected", String(button.dataset.promptMode === mode)); });
}

function selectedModel(): ModelOption | undefined {
  return models.find((model) => model.id === modelSelect?.value) ?? models[0];
}
function selectedFavorites(): string[] {
  if (!favoriteLanguages) return config?.favoriteLanguages ?? ["Vietnamese", "English"];
  return [...favoriteLanguages.selectedOptions].map((option) => option.value).slice(0, 5);
}

function applyConfig(cfg: AppConfig): void {
  config = cfg;
  if (translateEnabled) translateEnabled.checked = cfg.translateEnabled;
  if (rewriteEnabled) rewriteEnabled.checked = cfg.rewriteEnabled;
  if (targetLanguage) {
    if (![...targetLanguage.options].some((option) => option.value === cfg.targetLanguage)) targetLanguage.add(new Option(cfg.targetLanguage, cfg.targetLanguage));
    targetLanguage.value = cfg.targetLanguage;
  }
  if (favoriteLanguages) {
    const favorites = new Set(cfg.favoriteLanguages ?? []);
    favoriteLanguages.querySelectorAll<HTMLOptionElement>("option").forEach((option) => { option.selected = favorites.has(option.value); });
  }
  if (resultAction) resultAction.value = cfg.resultAction || "copy";
  if (backendPreference) backendPreference.value = cfg.backendPreference || "auto";
  if (translateSystemPrompt) translateSystemPrompt.value = cfg.translateSystemPrompt;
  if (rewriteSystemPrompt) rewriteSystemPrompt.value = cfg.rewriteSystemPrompt;
  if (maxTokens) maxTokens.value = String(cfg.maxTokens);
  if (temperature) temperature.value = String(cfg.temperature);
  if (gpuLayers) gpuLayers.value = String(cfg.gpuLayers);
  if (historyRetentionDays) historyRetentionDays.value = String(cfg.historyRetentionDays);
  const match = models.find((model) => model.repo === cfg.modelRepo && model.file === cfg.modelFile) ?? models[0];
  if (modelSelect && match) modelSelect.value = match.id;
}

function readForm(): AppConfig {
  const model = selectedModel();
  return {
    translateEnabled: translateEnabled?.checked ?? true,
    rewriteEnabled: rewriteEnabled?.checked ?? true,
    targetLanguage: targetLanguage?.value ?? "Vietnamese",
    favoriteLanguages: selectedFavorites(),
    rewriteSystemPrompt: rewriteSystemPrompt?.value ?? config?.rewriteSystemPrompt ?? "",
    translateSystemPrompt: translateSystemPrompt?.value ?? config?.translateSystemPrompt ?? "",
    resultAction: resultAction?.value ?? config?.resultAction ?? "copy",
    backendPreference: backendPreference?.value ?? config?.backendPreference ?? "auto",
    modelRepo: model?.repo ?? config?.modelRepo ?? "",
    modelFile: model?.file ?? config?.modelFile ?? "",
    maxTokens: Number(maxTokens?.value || 256),
    temperature: Number(temperature?.value || 0.3),
    gpuLayers: Number(gpuLayers?.value || 999),
    historyRetentionDays: Number(historyRetentionDays?.value ?? config?.historyRetentionDays ?? 30),
  };
}

function showSaveStatus(message: string, clear = false): void {
  if (!saveStatus) return;
  saveStatus.textContent = message;
  if (savedMessageTimer !== undefined) window.clearTimeout(savedMessageTimer);
  if (clear) savedMessageTimer = window.setTimeout(() => { if (saveStatus?.textContent === message) saveStatus.textContent = ""; }, 1800);
}

async function persistSettingsWithStatus(): Promise<void> {
  if (initializing) return;
  showSaveStatus("Saving…");
  try {
    const saved = await invoke<AppConfig>("save_config", { config: readForm() });
    config = saved;
    showSaveStatus("Saved", true);
  } catch (error) { showSaveStatus(`Could not save: ${String(error)}`); }
}

function scheduleSettingsSave(): void {
  if (initializing) return;
  if (saveTimer !== undefined) window.clearTimeout(saveTimer);
  saveTimer = window.setTimeout(() => void persistSettingsWithStatus(), 300);
}

function escapeHtml(value: string): string {
  const amp = String.fromCharCode(38);
  return value.replace(/&/g, amp + "amp;").replace(/</g, amp + "lt;").replace(/>/g, amp + "gt;").replace(/"/g, amp + "quot;");
}

function renderBackend(status: ModelStatus): void {
  const backend = (status.backend || status.gpuFeature || "cpu").toLowerCase();
  backendPills.forEach((pill) => { const active = pill.dataset.backend === backend; pill.classList.toggle("pill--active", active); pill.setAttribute("aria-current", active ? "true" : "false"); });
  if (deviceList) {
    deviceList.innerHTML = "";
    const devices = status.devices?.length ? status.devices : [{ id: "cpu", name: "CPU", backend: "cpu", description: "Host CPU" }];
    for (const device of devices) {
      const li = document.createElement("li");
      li.innerHTML = `<strong>${escapeHtml(device.name)}</strong> <span class="device-backend">(${escapeHtml(device.backend)})</span><br /><span class="device-desc">${escapeHtml(device.description)}</span>`;
      deviceList.append(li);
    }
  }
  if (backendHint) {
    const pref = backendPreference?.value || config?.backendPreference || "auto";
    backendHint.textContent = status.gpuOffloadAvailable ? `GPU backend compiled in (${status.gpuFeature}). Preference: ${pref}. Auto falls back to CPU if GPU loading fails.` : `This binary is CPU-only. Preference: ${pref}. Install/build with Vulkan or CUDA to enable GPU inference.`;
  }
}

function renderStatus(status: ModelStatus): void {
  renderBackend(status);
  if (gettingStartedCard) gettingStartedCard.hidden = status.downloaded;
  if (!modelStatus) return;
  const state = status.loaded ? "loaded in memory" : status.downloaded ? "downloaded (not loaded)" : "not downloaded — internet is only needed for this one-time model download";
  modelStatus.textContent = `${status.file} — ${state}. Backend: ${status.backend}.${status.localPath ? ` Path: ${status.localPath}` : ""}`;
}
async function refreshStatus(): Promise<void> { renderStatus(await invoke<ModelStatus>("get_model_status")); }

function formatTs(ts: number): string { try { return new Date(ts).toLocaleString(); } catch { return String(ts); } }
function historyEntryHtml(entry: HistoryEntry): string {
  const language = entry.targetLanguage ? ` · ${escapeHtml(entry.targetLanguage)}` : "";
  return `<article class="history-item" data-id="${escapeHtml(entry.id)}"><div class="history-meta"><span class="history-action">${escapeHtml(entry.action)}${language}</span><span>${escapeHtml(formatTs(entry.ts))}</span></div><div class="history-source">${escapeHtml(entry.source)}</div><div class="history-result">${escapeHtml(entry.result)}</div><div class="history-item-actions"><button type="button" class="btn btn--tiny" data-history-copy="source">Copy source</button><button type="button" class="btn btn--tiny" data-history-copy="result">Copy result</button><button type="button" class="btn btn--tiny" data-history-reuse>Reuse</button><button type="button" class="btn btn--tiny" data-history-delete>Delete</button></div></article>`;
}
async function refreshHistory(): Promise<void> {
  if (!historyList) return;
  try {
    const entries = await invoke<HistoryEntry[]>("search_history", { query: historySearch?.value.trim() || null, action: historyAction?.value || null, limit: 200 });
    historyList.innerHTML = entries.length ? entries.map(historyEntryHtml).join("") : `<p class="history-empty">No matching history.</p>`;
  } catch (error) { historyList.innerHTML = `<p class="history-empty">${escapeHtml(String(error))}</p>`; }
}
function scheduleHistoryRefresh(): void {
  if (historySearchTimer !== undefined) window.clearTimeout(historySearchTimer);
  historySearchTimer = window.setTimeout(() => void refreshHistory(), 180);
}

function wireProfile(selectId: string, textareaId: string, profiles: Record<string, string>): void {
  const select = document.getElementById(selectId);
  const textarea = document.getElementById(textareaId);
  if (!(select instanceof HTMLSelectElement) || !(textarea instanceof HTMLTextAreaElement)) return;
  const sync = () => { const match = Object.entries(profiles).find(([, prompt]) => prompt === textarea.value.trim()); select.value = match ? match[0] : "custom"; };
  select.addEventListener("change", () => { if (select.value !== "custom" && profiles[select.value]) { textarea.value = profiles[select.value]; scheduleSettingsSave(); } });
  textarea.addEventListener("input", () => { sync(); scheduleSettingsSave(); });
  window.setTimeout(sync, 0);
  window.setTimeout(sync, 150);
  window.setTimeout(sync, 500);
}

function wireUi(): void {
  document.querySelectorAll<HTMLButtonElement>("[data-settings-page]").forEach((button) => button.addEventListener("click", () => setSettingsPage(button.dataset.settingsPage as SettingsPage)));
  document.querySelectorAll<HTMLButtonElement>("[data-prompt-mode]").forEach((button) => button.addEventListener("click", () => setPromptMode(button.dataset.promptMode as PromptMode)));
  modelAdvancedToggle?.addEventListener("click", () => { const expanded = modelAdvancedToggle.getAttribute("aria-expanded") === "true"; modelAdvancedToggle.setAttribute("aria-expanded", String(!expanded)); if (modelAdvanced) modelAdvanced.hidden = expanded; });

  themePreference?.addEventListener("change", () => {
    const theme = themePreference.value;
    if (theme === "light" || theme === "dark") { localStorage.setItem("butchi.theme", theme); document.documentElement.dataset.theme = theme; }
    else { localStorage.removeItem("butchi.theme"); delete document.documentElement.dataset.theme; }
  });

  [translateEnabled, rewriteEnabled, targetLanguage, resultAction, backendPreference, modelSelect, maxTokens, temperature, gpuLayers, historyRetentionDays].forEach((control) => control?.addEventListener("change", scheduleSettingsSave));
  favoriteLanguages?.addEventListener("change", () => {
    const selected = [...favoriteLanguages.selectedOptions];
    if (selected.length > 5) { selected[selected.length - 1].selected = false; showSaveStatus("Choose up to 5 favorite languages."); }
    scheduleSettingsSave();
  });
  backendPreference?.addEventListener("change", () => void refreshStatus());
  modelSelect?.addEventListener("change", () => void refreshStatus());
  refreshHistoryBtn?.addEventListener("click", () => void refreshHistory());
  historySearch?.addEventListener("input", scheduleHistoryRefresh);
  historyAction?.addEventListener("change", () => void refreshHistory());
  wireProfile("translatePromptProfile", "translateSystemPrompt", translationProfiles);
  wireProfile("rewritePromptProfile", "rewriteSystemPrompt", rewriteProfiles);
}

async function init(): Promise<void> {
  wireUi();
  setSettingsPage("general");
  setPromptMode("translate");
  const savedTheme = localStorage.getItem("butchi.theme");
  if (themePreference) themePreference.value = savedTheme === "light" || savedTheme === "dark" ? savedTheme : "system";
  models = await invoke<ModelOption[]>("list_models");
  if (modelSelect) {
    modelSelect.innerHTML = "";
    for (const model of models) modelSelect.add(new Option(`${model.label} (${model.sizeHint})`, model.id));
  }
  try { if (appVersion) appVersion.textContent = await getVersion(); } catch { /* keep static fallback */ }
  applyConfig(await invoke<AppConfig>("get_config"));
  await refreshStatus();
  await refreshHistory();
  initializing = false;
}

downloadBtn?.addEventListener("click", async () => {
  const model = selectedModel();
  if (!model || !downloadBtn) return;
  downloadBtn.disabled = true; downloadBtn.textContent = "Downloading…"; showSaveStatus("Downloading the model from Hugging Face. Selected text is not uploaded.");
  try { renderStatus(await invoke<ModelStatus>("download_model", { repo: model.repo, file: model.file })); showSaveStatus("Model downloaded. Click Load model to make Butchi ready."); }
  catch (error) { showSaveStatus(String(error)); }
  finally { downloadBtn.disabled = false; downloadBtn.textContent = "Download"; }
});

loadBtn?.addEventListener("click", async () => {
  if (!loadBtn) return;
  try { await invoke("save_config", { config: readForm() }); } catch { /* loading can still report its own error */ }
  loadBtn.disabled = true; loadBtn.textContent = "Loading…";
  try { const status = await invoke<ModelStatus>("load_model"); renderStatus(status); showSaveStatus(`Ready. Model loaded with ${status.backend}.`, true); }
  catch (error) { showSaveStatus(String(error)); }
  finally { loadBtn.disabled = false; loadBtn.textContent = "Load model"; }
});

clearLocalDataBtn?.addEventListener("click", async () => {
  if (!confirm("Delete all Butchi history and downloaded GGUF models from this device? Your settings will be kept.")) return;
  clearLocalDataBtn.disabled = true;
  try { await invoke("clear_local_ai_data"); await refreshStatus(); await refreshHistory(); showSaveStatus("Local history and downloaded models deleted.", true); }
  catch (error) { showSaveStatus(String(error)); }
  finally { clearLocalDataBtn.disabled = false; }
});

historyList?.addEventListener("click", async (event) => {
  const target = event.target as HTMLElement;
  const card = target.closest<HTMLElement>(".history-item");
  const id = card?.dataset.id;
  if (!card || !id) return;
  const source = card.querySelector<HTMLElement>(".history-source")?.textContent ?? "";
  const result = card.querySelector<HTMLElement>(".history-result")?.textContent ?? "";
  if (target.closest("[data-history-copy='source']")) { await navigator.clipboard.writeText(source); showSaveStatus("History source copied.", true); return; }
  if (target.closest("[data-history-copy='result']")) { await navigator.clipboard.writeText(result); showSaveStatus("History result copied.", true); return; }
  if (target.closest("[data-history-reuse]")) { await navigator.clipboard.writeText(source); showSaveStatus("Source copied — paste it anywhere and run Butchi again.", true); return; }
  if (target.closest("[data-history-delete]")) { await invoke("delete_history_entry", { id }); await refreshHistory(); showSaveStatus("History entry deleted.", true); }
});

clearHistoryBtn?.addEventListener("click", async () => {
  if (!confirm("Clear all history?")) return;
  try { await invoke("clear_history"); await refreshHistory(); showSaveStatus("History cleared.", true); }
  catch (error) { showSaveStatus(String(error)); }
});

void init();
