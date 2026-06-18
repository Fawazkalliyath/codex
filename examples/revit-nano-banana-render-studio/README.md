# Revit Nano Banana Pro Render Studio (Free Prototype)

This prototype demonstrates a practical architecture for a Revit-connected rendering panel:

- Sync the currently active or selected Revit 3D view (mocked JSON in this prototype).
- Accept user recommendations in natural text, in any language.
- Convert user intent into a finalized Nano Banana Pro prompt via **AI Recommend Final Prompt**.
- If no user text is provided, auto-generate a view-aware recommendation prompt from model metadata.
- Pick ultra-realistic presets, ratios, style family, entourage assets, and smart/futuristic options.
- Generate a high-quality preview render payload for a `nano-banana-pro` backend.
- Preview and download the generated output image.

## Run locally

```bash
cd examples/revit-nano-banana-render-studio
python -m http.server 4173
```

Then open <http://localhost:4173>.

## Production integration outline

1. Replace `mockRevitView` with real data from a Revit plugin bridge (REST/WebSocket/local IPC).
2. Route `updatePromptFromAi()` to your LLM recommendation endpoint for stronger multilingual understanding.
3. Send the generated payload from `buildPayload()` to your Nano Banana Pro rendering endpoint.
4. Replace `generatePreviewImage()` with server-side generated image URLs and job status polling.
5. Keep preset and smart-option catalogs server-driven so rendering teams can tune quality without redeploying.
