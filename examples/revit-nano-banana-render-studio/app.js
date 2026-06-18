const presets = [
  "Golden Hour Exterior",
  "Overcast Soft Light",
  "Night + Interior Glow",
  "Rainy Reflections",
  "Drone Hero Shot",
  "Street-Level Eye View",
  "Luxury Materials",
  "Ultra Sharp Facade",
  "Future Neon Atmosphere",
  "Hyper-Real Landscape",
];

const promptLexicon = {
  lluvia: "rain-kissed pavement with reflective highlights",
  lluvias: "rain-kissed pavement with reflective highlights",
  noche: "blue-hour to night transition with warm interior glow",
  atardecer: "golden sunset with long cinematic shadows",
  edificio: "architectural hero composition",
  moderno: "contemporary and premium material palette",
  amanecer: "soft dawn light with atmospheric haze",
  pluie: "soft rain atmosphere and wet ground specular response",
  coucher: "golden hour sunlight with elevated contrast",
  ville: "urban context with active streets",
  futuriste: "future-forward urban mood",
  futurista: "future-forward urban mood",
  verde: "lush planting bands and contextual greenery",
  people: "diverse photoreal pedestrians",
  human: "diverse photoreal pedestrians",
  cinematic: "cinematic color grading and lens behavior",
  luxury: "high-end finishes and polished surfaces",
  fog: "light volumetric fog for depth",
};

const presetList = document.querySelector("#presetList");
const connectionStatus = document.querySelector("#connectionStatus");
const viewMetadata = document.querySelector("#viewMetadata");
const userIntent = document.querySelector("#userIntent");
const promptBox = document.querySelector("#promptBox");
const ratio = document.querySelector("#ratio");
const style = document.querySelector("#style");
const quality = document.querySelector("#quality");
const previewImage = document.querySelector("#previewImage");
const payloadOutput = document.querySelector("#payloadOutput");
const recommendationNotes = document.querySelector("#recommendationNotes");
const downloadButton = document.querySelector("#download");

let activePresets = new Set([presets[0], presets[6]]);
let generatedImageHref = "";

for (const preset of presets) {
  const chip = document.createElement("button");
  chip.type = "button";
  chip.className = "preset-chip";
  chip.textContent = preset;
  if (activePresets.has(preset)) {
    chip.classList.add("active");
  }

  chip.addEventListener("click", () => {
    if (activePresets.has(preset)) {
      activePresets.delete(preset);
    } else {
      activePresets.add(preset);
    }
    chip.classList.toggle("active");
  });

  presetList.append(chip);
}

document.querySelector("#syncView").addEventListener("click", () => {
  const mockRevitView = {
    projectName: "Downtown Mixed Use Complex",
    modelId: "model-9082",
    active3DViewId: "view-3d-22",
    viewName: "Tower-A South East",
    camera: {
      position: [32.1, 7.6, 12.4],
      target: [18.2, 5.3, 0.9],
      focalLengthMm: 28,
    },
    geolocation: "37.789,-122.401",
    weatherHint: "clear",
    context: "mixed-use urban district",
  };

  connectionStatus.textContent = "Connected";
  connectionStatus.classList.remove("disconnected");
  connectionStatus.classList.add("connected");
  viewMetadata.value = JSON.stringify(mockRevitView, null, 2);
});

function parsedViewMetadata() {
  if (!viewMetadata.value.trim()) {
    return {};
  }

  try {
    return JSON.parse(viewMetadata.value);
  } catch {
    throw new Error("View metadata must be valid JSON before generating.");
  }
}

function selectedValues(selector) {
  return [...document.querySelectorAll(selector)].map((input) => input.value);
}

function normalizeIntentToPrompt(intent) {
  const normalized = intent.trim().toLowerCase();
  if (!normalized) {
    return [];
  }

  return Object.entries(promptLexicon)
    .filter(([token]) => normalized.includes(token))
    .map(([, phrase]) => phrase);
}

function smartOptionFragments(activeOptions) {
  const smartMap = {
    smartLighting: "physically-accurate daylight balancing and bounce lighting",
    smartMaterials: "high-fidelity PBR materials with realistic roughness variation",
    smartComposition: "architectural-leading-line composition with strong depth",
    smartVegetation: "procedural native trees and planting density tuned to scale",
    futuristicMood: "subtle futuristic city ambience with advanced facade glow",
    futureTransit: "integrated autonomous transit elements in the urban scene",
  };

  return activeOptions.map((option) => smartMap[option]).filter(Boolean);
}

function buildRecommendedPrompt(view, intent, selectedAssets, activeOptions) {
  const viewName = view.viewName || "architectural hero view";
  const context = view.context || "urban design context";
  const weather = view.weatherHint || "clear conditions";
  const lexiconHints = normalizeIntentToPrompt(intent);
  const smartHints = smartOptionFragments(activeOptions);

  const assetText = selectedAssets.length > 0 ? selectedAssets.join(", ") : "minimal entourage";
  const baseHints = [
    `Ultra photoreal architectural render of ${viewName}`,
    `Context: ${context}`,
    `Weather mood: ${weather}`,
    `Aspect ratio ${ratio.value}, style ${style.value}`,
    `Entourage: ${assetText}`,
    "4k detail, global illumination, realistic shadows, accurate reflections",
  ];

  if (intent.trim()) {
    baseHints.push(`User intent interpreted from multilingual text: ${intent.trim()}`);
  }

  return [...baseHints, ...lexiconHints, ...smartHints].join(" | ");
}

function updatePromptFromAi() {
  const view = parsedViewMetadata();
  const intent = userIntent.value;
  const selectedAssets = selectedValues(".asset:checked");
  const activeSmartOptions = selectedValues(".smart-option:checked");
  const recommendedPrompt = buildRecommendedPrompt(view, intent, selectedAssets, activeSmartOptions);

  promptBox.value = recommendedPrompt;
  recommendationNotes.textContent = intent.trim()
    ? "AI converted your natural language input into a production-ready Nano Banana Pro prompt."
    : "AI generated a view-aware prompt using synced model metadata and smart options.";
}

function buildPayload() {
  const parsedMetadata = parsedViewMetadata();
  const selectedAssets = selectedValues(".asset:checked");
  const activeSmartOptions = selectedValues(".smart-option:checked");
  const optimizedPrompt = promptBox.value.trim();

  if (!optimizedPrompt) {
    throw new Error("Final prompt is empty. Use AI Recommend or type a custom prompt.");
  }

  return {
    provider: "nano-banana-pro",
    mode: "revit-sync-render",
    quality: quality.value.toLowerCase(),
    render: {
      aspectRatio: ratio.value,
      style: style.value,
      presets: [...activePresets],
      entourage: selectedAssets,
      smartOptions: activeSmartOptions,
      prompt: optimizedPrompt,
      userIntent: userIntent.value.trim(),
      guidance: {
        realism: "ultra",
        denoise: 0.22,
        preserveGeometry: true,
        consistencyBoost: true,
      },
    },
    source: {
      app: "revit-plugin",
      activeView: parsedMetadata,
    },
  };
}

function generatePreviewImage(payload) {
  const canvas = document.createElement("canvas");
  canvas.width = 1600;
  canvas.height = 900;
  const ctx = canvas.getContext("2d");
  const gradient = ctx.createLinearGradient(0, 0, canvas.width, canvas.height);
  gradient.addColorStop(0, "#1f3358");
  gradient.addColorStop(0.5, "#172236");
  gradient.addColorStop(1, "#101924");

  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  ctx.fillStyle = "#e4edff";
  ctx.font = "bold 52px Inter, sans-serif";
  ctx.fillText("Nano Banana Pro — Render Preview", 64, 120);

  ctx.font = "30px Inter, sans-serif";
  ctx.fillText(`View: ${payload.source.activeView.viewName || "Unspecified"}`, 64, 200);
  ctx.fillText(`Style: ${payload.render.style}`, 64, 250);
  ctx.fillText(`Ratio: ${payload.render.aspectRatio}`, 64, 300);
  ctx.fillText(`Assets: ${payload.render.entourage.join(", ") || "none"}`, 64, 350);
  ctx.fillText(`Smart: ${payload.render.smartOptions.join(", ") || "none"}`, 64, 400);

  ctx.fillStyle = "#9bb2df";
  ctx.font = "24px Inter, sans-serif";
  const promptLabel = payload.render.prompt || "No custom prompt provided.";
  ctx.fillText(`Prompt: ${promptLabel.slice(0, 105)}`, 64, 470);

  return canvas.toDataURL("image/png");
}

document.querySelector("#aiRecommend").addEventListener("click", () => {
  try {
    updatePromptFromAi();
  } catch (error) {
    recommendationNotes.textContent = `Recommendation failed: ${error.message}`;
  }
});

document.querySelector("#clearIntent").addEventListener("click", () => {
  userIntent.value = "";
  promptBox.value = "";
  recommendationNotes.textContent = "Prompt and intent cleared.";
});

document.querySelector("#generate").addEventListener("click", () => {
  try {
    const payload = buildPayload();
    payloadOutput.textContent = JSON.stringify(payload, null, 2);
    generatedImageHref = generatePreviewImage(payload);
    previewImage.src = generatedImageHref;
    downloadButton.disabled = false;
  } catch (error) {
    payloadOutput.textContent = `Error: ${error.message}`;
    downloadButton.disabled = true;
  }
});

downloadButton.addEventListener("click", () => {
  if (!generatedImageHref) {
    return;
  }

  const anchor = document.createElement("a");
  anchor.href = generatedImageHref;
  anchor.download = "nano-banana-render-preview.png";
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
});
