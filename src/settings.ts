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

let models: ModelOption[] = [];
let config: AppConfig | null = null;

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
  if (rewriteSystemPrompt) rewriteSystemPrompt.value = cfg.rewriteSystemPrompt;
  if (maxTokens) maxTokens.value = String(cfg.maxTokens);
  if (temperature) temperature.value = String(cfg.temperature);
  if (gpuLayers) gpuLayers.value = String(cfg.gpuLayers);

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
    rewriteSystemPrompt:
      rewriteSystemPrompt?.value ??
      config?.rewriteSystemPrompt ??
      "",
    translateSystemPrompt: config?.translateSystemPrompt ?? "",
    modelRepo: model?.repo ?? config?.modelRepo ?? "",
    modelFile: model?.file ?? config?.modelFile ?? "",
    maxTokens: Number(maxTokens?.value || 256),
    temperature: Number(temperature?.value || 0.3),
    gpuLayers: Number(gpuLayers?.value || 999),
  };
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
      : [
          {
            id: "cpu",
            name: "CPU",
            backend: "cpu",
            description: "Host CPU",
          },
        ];
    for (const d of devices) {
      const li = document.createElement("li");
      li.innerHTML = `<strong>${escapeHtml(d.name)}</strong> <span class="device-backend">(${escapeHtml(
        d.backend,
      )})</span><br /><span class="device-desc">${escapeHtml(d.description)}</span>`;
      deviceList.append(li);
    }
  }

  if (backendHint) {
    if (status.gpuOffloadAvailable) {
      backendHint.textContent = `GPU offload available (max_devices=${status.maxDevices}). Set GPU layers above 0 when loading the model.`;
    } else {
      backendHint.textContent =
        "CPU-only binary. Rebuild with: npm run tauri dev -- -- --features cuda   (or vulkan)";
    }
  }
}

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function renderStatus(status: ModelStatus) {
  renderBackend(status);
  if (!modelStatus) return;
  const state = status.loaded
    ? "loaded in memory"
    : status.downloaded
      ? "downloaded (not loaded)"
      : "not downloaded";
  modelStatus.textContent = `${status.file} — ${state}. Backend: ${status.backend || status.gpuFeature}.${
    status.localPath ? ` Path: ${status.localPath}` : ""
  }`;
}

async function refreshStatus() {
  const status = await invoke<ModelStatus>("get_model_status");
  renderStatus(status);
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
}

downloadBtn?.addEventListener("click", async () => {
  const model = selectedModel();
  if (!model || !downloadBtn) return;
  downloadBtn.disabled = true;
  downloadBtn.textContent = "Downloading…";
  if (saveStatus) saveStatus.textContent = "Downloading from Hugging Face — this can take a few minutes…";
  try {
    const status = await invoke<ModelStatus>("download_model", {
      repo: model.repo,
      file: model.file,
    });
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
  // Persist model selection first.
  try {
    const cfg = readForm();
    await invoke("save_config", { config: cfg });
  } catch {
    /* ignore and try load anyway */
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
    const cfg = readForm();
    const saved = await invoke<AppConfig>("save_config", { config: cfg });
    applyConfig(saved);
    await refreshStatus();
    if (saveStatus) saveStatus.textContent = "Settings saved.";
  } catch (error) {
    if (saveStatus) saveStatus.textContent = String(error);
  } finally {
    saveBtn.disabled = false;
  }
});

modelSelect?.addEventListener("change", () => {
  void refreshStatus();
});

void init();
