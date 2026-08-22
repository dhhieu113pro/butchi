import "@fontsource-variable/space-grotesk";
import "@fontsource-variable/ibm-plex-sans";
import { invoke } from "@tauri-apps/api/core";

type AppConfig = {
  translateEnabled: boolean;
  rewriteEnabled: boolean;
  targetLanguage: string;
  rewriteSystemPrompt: string;
  translateSystemPrompt: string;
  modelRepo: string;
  modelFile: string;
  maxTokens: number;
  temperature: number;
  gpuLayers: number;
  historyRetentionDays: number;
};

type ModelOption = {
  id: string;
  label: string;
  repo: string;
  file: string;
  sizeHint: string;
};

type BackendDevice = {
  id: string;
  name: string;
  backend: string;
  description: string;
};

type HistoryEntry = {
  id: string;
  ts: number;
  action: string;
  source: string;
  result: string;
  message: string;
  targetLanguage: string | null;
};

type ModelStatus = {
  downloaded: boolean;
  loaded: boolean;
  localPath: string | null;
  repo: string;
  file: string;
  gpuFeature: string;
  backend: string;
  devices: BackendDevice[];
  gpuOffloadAvailable: boolean;
  maxDevices: number;
};

const translateEnabled = document.querySelector<HTMLInputElement>("#translateEnabled");
const rewriteEnabled = document.querySelector<HTMLInputElement>("#rewriteEnabled");
const targetLanguage = document.querySelector<HTMLSelectElement>("#targetLanguage");
const translateSystemPrompt = document.querySelector<HTMLTextAreaElement>("#translateSystemPrompt");
const rewriteSystemPrompt = document.querySelector<HTMLTextAreaElement>("#rewriteSystemPrompt");
const modelSelect = document.querySelector<HTMLSelectElement>("#modelSelect");
const modelStatus = document.querySelector<HTMLElement>("#modelStatus");
const backendPills = document.querySelectorAll<HTMLElement>("#backendPills .pill");
const deviceList = document.querySelector<HTMLUListElement>("#deviceList");
const backendHint = document.querySelector<HTMLElement>("#backendHint");
const downloadBtn = document.querySelector<HTMLButtonElement>("#downloadBtn");
const loadBtn = document.querySelector<HTMLButtonElement>("#loadBtn");
const maxTokens = document.querySelector<HTMLInputElement>("#maxTokens");
const temperature = document.querySelector<HTMLInputElement>("#temperature");
const gpuLayers = document.querySelector<HTMLInputElement>("#gpuLayers");
const saveBtn = document.querySelector<HTMLButtonElement>("#saveBtn");
const saveStatus = document.querySelector<HTMLElement>("#saveStatus");
const historyList = document.querySelector<HTMLElement>("#historyList");
const historySearch = document.querySelector<HTMLInputElement>("#historySearch");
const historyAction = document.querySelector<HTMLSelectElement>("#historyAction");
const historyRetentionDays = document.querySelector<HTMLSelectElement>("#historyRetentionDays");
const refreshHistoryBtn = document.querySelector<HTMLButtonElement>("#refreshHistoryBtn");
const clearHistoryBtn = document.querySelector<HTMLButtonElement>("#clearHistoryBtn");

let models: ModelOption[] = [];
let config: AppConfig | null = null;
let historySearchTimer: number | undefined;

function selectedModel(): ModelOption | undefined {
  const id = modelSelect?.value;
  return models.find((m) => m.id === id) ?? models[0];
}

function applyConfig(cfg: AppConfig) {
  config = cfg;
  if (translateEnabled) translateEnabled.checked = cfg.translateEnabled;
  if (rewriteEnabled) rewriteEnabled.checked = cfg.rewriteEnabled;
  if (targetLanguage) {
    const exists = [...targetLanguage.options].some((o) => o.value === cfg.targetLanguage);
    if (!exists) {
      const opt = document.createElement("option");
      opt.value = cfg.targetLanguage;
      opt.textContent = cfg.targetLanguage;
      targetLanguage.append(opt);
    }
    targetLanguage.value = cfg.targetLanguage;
  }
  if (translateSystemPrompt) translateSystemPrompt.value = cfg.translateSystemPrompt;
  if (rewriteSystemPrompt) rewriteSystemPrompt.value = cfg.rewriteSystemPrompt;
  if (maxTokens) maxTokens.value = String(cfg.maxTokens);
  if (temperature) temperature.value = String(cfg.temperature);
  if (gpuLayers) gpuLayers.value = String(cfg.gpuLayers);
  if (historyRetentionDays) historyRetentionDays.value = String(cfg.historyRetentionDays);

  const match =
    models.find((m) => m.repo === cfg.modelRepo && m.file === cfg.modelFile) ?? models[0];
  if (modelSelect && match) modelSelect.value = match.id;
}

function readForm(): AppConfig {
  const model = selectedModel();
  return {
    translateEnabled: translateEnabled?.checked ?? true,
    rewriteEnabled: rewriteEnabled?.checked ?? true,
    targetLanguage: targetLanguage?.value ?? "Vietnamese",
    rewriteSystemPrompt: rewriteSystemPrompt?.value ?? config?.rewriteSystemPrompt ?? "",
    translateSystemPrompt: translateSystemPrompt?.value ?? config?.translateSystemPrompt ?? "",
    modelRepo: model?.repo ?? config?.modelRepo ?? "",
    modelFile: model?.file ?? config?.modelFile ?? "",
    maxTokens: Number(maxTokens?.value || 256),
    temperature: Number(temperature?.value || 0.3),
    gpuLayers: Number(gpuLayers?.value || 999),
    historyRetentionDays: Number(historyRetentionDays?.value ?? config?.historyRetentionDays ?? 30),
  };
}

function escapeHtml(value: string): string {
  const amp = String.fromCharCode(38);
  return value
    .replace(/&/g, amp + "amp;")
    .replace(/</g, amp + "lt;")
    .replace(/>/g, amp + "gt;")
    .replace(/"/g, amp + "quot;");
}

function renderBackend(status: ModelStatus) {
  const backend = (status.backend || status.gpuFeature || "cpu").toLowerCase();
  backendPills.forEach((pill) => {
    const active = pill.dataset.backend === backend;
    pill.classList.toggle("pill--active", active);
    pill.setAttribute("aria-current", active ? "true" : "false");
  });

  if (deviceList) {
    deviceList.innerHTML = "";
    const devices = status.devices?.length
      ? status.devices
      : [{ id: "cpu", name: "CPU", backend: "cpu", description: "Host CPU" }];
    for (const d of devices) {
      const li = document.createElement("li");
      li.innerHTML = `<strong>${escapeHtml(d.name)}</strong> <span class="device-backend">(${escapeHtml(d.backend)})</span><br /><span class="device-desc">${escapeHtml(d.description)}</span>`;
      deviceList.append(li);
    }
  }

  if (backendHint) {
    backendHint.textContent = status.gpuOffloadAvailable
      ? `GPU offload available (max_devices=${status.maxDevices}). Set GPU layers above 0 when loading the model.`
      : "CPU-only binary. Build with the Vulkan or CUDA feature to enable GPU offload.";
  }
}

function renderStatus(status: ModelStatus) {
  renderBackend(status);
  if (!modelStatus) return;
  const state = status.loaded
    ? "loaded in memory"
    : status.downloaded
      ? "downloaded (not loaded)"
      : "not downloaded";
  modelStatus.textContent = `${status.file} — ${state}. Backend: ${status.backend || status.gpuFeature}.${status.localPath ? ` Path: ${status.localPath}` : ""}`;
}

async function refreshStatus() {
  const status = await invoke<ModelStatus>("get_model_status");
  renderStatus(status);
}

function formatTs(ts: number): string {
  try {
    return new Date(ts).toLocaleString();
  } catch {
    return String(ts);
  }
}

function historyEntryHtml(e: HistoryEntry): string {
  const language = e.targetLanguage ? ` · ${escapeHtml(e.targetLanguage)}` : "";
  return `
    <article class="history-item" data-id="${escapeHtml(e.id)}">
      <div class="history-meta">
        <span class="history-action">${escapeHtml(e.action)}${language}</span>
        <span>${escapeHtml(formatTs(e.ts))}</span>
      </div>
      <div class="history-source">${escapeHtml(e.source)}</div>
      <div class="history-result">${escapeHtml(e.result)}</div>
      <div class="history-item-actions">
        <button type="button" class="btn btn--tiny" data-history-copy="source">Copy source</button>
        <button type="button" class="btn btn--tiny" data-history-copy="result">Copy result</button>
        <button type="button" class="btn btn--tiny" data-history-reuse>Reuse</button>
        <button type="button" class="btn btn--tiny" data-history-delete>Delete</button>
      </div>
    </article>`;
}

async function refreshHistory() {
  if (!historyList) return;
  try {
    const entries = await invoke<HistoryEntry[]>("search_history", {
      query: historySearch?.value.trim() || null,
      action: historyAction?.value || null,
      limit: 200,
    });
    if (!entries.length) {
      historyList.innerHTML = `<p class="history-empty">No matching history.</p>`;
      return;
    }
    historyList.innerHTML = entries.map(historyEntryHtml).join("");
  } catch (error) {
    historyList.innerHTML = `<p class="history-empty">${escapeHtml(String(error))}</p>`;
  }
}

function scheduleHistoryRefresh() {
  if (historySearchTimer !== undefined) window.clearTimeout(historySearchTimer);
  historySearchTimer = window.setTimeout(() => void refreshHistory(), 180);
}

async function init() {
  models = await invoke<ModelOption[]>("list_models");
  if (modelSelect) {
    modelSelect.innerHTML = "";
    for (const model of models) {
      const opt = document.createElement("option");
      opt.value = model.id;
      opt.textContent = `${model.label} (${model.sizeHint})`;
      modelSelect.append(opt);
    }
  }

  const cfg = await invoke<AppConfig>("get_config");
  applyConfig(cfg);
  await refreshStatus();
  await refreshHistory();
}

downloadBtn?.addEventListener("click", async () => {
  const model = selectedModel();
  if (!model || !downloadBtn) return;
  downloadBtn.disabled = true;
  downloadBtn.textContent = "Downloading…";
  if (saveStatus) saveStatus.textContent = "Downloading from Hugging Face — this can take a few minutes…";
  try {
    const status = await invoke<ModelStatus>("download_model", { repo: model.repo, file: model.file });
    renderStatus(status);
    if (saveStatus) saveStatus.textContent = "Model downloaded.";
  } catch (error) {
    if (saveStatus) saveStatus.textContent = String(error);
  } finally {
    downloadBtn.disabled = false;
    downloadBtn.textContent = "Download";
  }
});

loadBtn?.addEventListener("click", async () => {
  if (!loadBtn) return;
  try {
    await invoke("save_config", { config: readForm() });
  } catch {
    /* ignore */
  }
  loadBtn.disabled = true;
  loadBtn.textContent = "Loading…";
  try {
    const status = await invoke<ModelStatus>("load_model");
    renderStatus(status);
    if (saveStatus) saveStatus.textContent = "Model loaded into memory.";
  } catch (error) {
    if (saveStatus) saveStatus.textContent = String(error);
  } finally {
    loadBtn.disabled = false;
    loadBtn.textContent = "Load model";
  }
});

saveBtn?.addEventListener("click", async () => {
  if (!saveBtn) return;
  saveBtn.disabled = true;
  try {
    const saved = await invoke<AppConfig>("save_config", { config: readForm() });
    applyConfig(saved);
    await refreshStatus();
    await refreshHistory();
    if (saveStatus) saveStatus.textContent = "Settings saved.";
  } catch (error) {
    if (saveStatus) saveStatus.textContent = String(error);
  } finally {
    saveBtn.disabled = false;
  }
});

modelSelect?.addEventListener("change", () => void refreshStatus());
refreshHistoryBtn?.addEventListener("click", () => void refreshHistory());
historySearch?.addEventListener("input", scheduleHistoryRefresh);
historyAction?.addEventListener("change", () => void refreshHistory());

historyList?.addEventListener("click", async (event) => {
  const target = event.target as HTMLElement;
  const card = target.closest<HTMLElement>(".history-item");
  if (!card) return;
  const id = card.dataset.id;
  if (!id) return;

  const source = card.querySelector<HTMLElement>(".history-source")?.textContent ?? "";
  const result = card.querySelector<HTMLElement>(".history-result")?.textContent ?? "";

  if (target.closest("[data-history-copy='source']")) {
    await navigator.clipboard.writeText(source);
    if (saveStatus) saveStatus.textContent = "History source copied.";
    return;
  }
  if (target.closest("[data-history-copy='result']")) {
    await navigator.clipboard.writeText(result);
    if (saveStatus) saveStatus.textContent = "History result copied.";
    return;
  }
  if (target.closest("[data-history-reuse]")) {
    await navigator.clipboard.writeText(source);
    if (saveStatus) saveStatus.textContent = "Source copied — paste it anywhere and run Butchi again.";
    return;
  }
  if (target.closest("[data-history-delete]")) {
    await invoke("delete_history_entry", { id });
    await refreshHistory();
    if (saveStatus) saveStatus.textContent = "History entry deleted.";
  }
});

clearHistoryBtn?.addEventListener("click", async () => {
  if (!confirm("Clear all history?")) return;
  try {
    await invoke("clear_history");
    await refreshHistory();
    if (saveStatus) saveStatus.textContent = "History cleared.";
  } catch (error) {
    if (saveStatus) saveStatus.textContent = String(error);
  }
});

void init();
