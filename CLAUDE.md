# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **Unity dynamic texture atlas** system. It packs runtime-loaded textures into large atlas textures to reduce draw calls and texture swapping in Unity UI. The project targets the Unity UI system (`UnityEngine.UI`) and uses `Resources.Load` / `Resources.LoadAsync` for asset loading.

## Build & Editor

- **Unity version**: 2022+ (confirmed by `.sln`/`.csproj` structure; open `dynamicAtlasDemo.sln` or the project folder in Unity Hub)
- Open the project in Unity Editor, then use **File → Build Settings** to build
- No CI/CD pipeline or automated test suite exists in this repo

## Architecture

### Core Atlas Engines

There are two competing atlas implementations, both managed by `DynamicAtlasManager` (singleton):

1. **`DynamicAtlas`** (`Assets/DynamicAtlas/DynamicAtlas.cs`) — Binary-space-partition (BSP) packing. Splits free space top-first or right-first. Supports both Texture2D (CopyTexture API) and RenderTexture (Blit API) output, controlled by `AtlasConfig.kUsingCopyTexture`.

2. **`PackingAtlas`** (`Assets/DynamicAtlas/PackingAtlas/PackingAtlas.cs`) — More sophisticated packer. Uses an "outside rectangle" heuristic to pack textures left-to-bottom, avoiding padding overhead at the right edge. Generates free areas by subtracting the placed rectangle from all overlapping free rectangles, then filters subsumed areas.

Both engines share:
- The `DynamicAtlasGroup` enum (`Size_256`, `Size_512`, `Size_1024`, `Size_2048`) to define atlas dimensions
- `IntegerRectangle` as the internal representation of rectangles in the atlas
- `SaveImageData` for tracking placed textures with reference counting
- `GetImageData` as a request queue item during async loading
- Object pooling via `DynamicAtlasManager` for `IntegerRectangle`, `SaveImageData`, and `GetImageData` to reduce GC pressure

### UI Components

- **`UIDynamicImage`** (`Assets/DynamicAtlas/UIDynamicImage.cs`) — Extends `UnityEngine.UI.Image`. Uses `DynamicAtlas`. Creates sprites from atlas texture regions. RenderTexture (Blit) path is disabled here since Sprite cannot easily consume RenderTextures.
- **`UIDynamicRawImage`** (`Assets/DynamicAtlas/UIDynamicRawImage.cs`) — Extends `UnityEngine.UI.RawImage`. Uses `DynamicAtlas`. Supports both CopyTexture and Blit (Material) callbacks.
- **`UIPackingImage`**, **`UIPackingRawImage`** (`Assets/DynamicAtlas/PackingAtlas/`) — Same patterns but backed by `PackingAtlas` instead of `DynamicAtlas`.

All UI components follow the same lifecycle: `Start()` → `OnPreDoImage()` (if a texture was pre-assigned in the editor) or `SetImage(path)` (async load from Resources) → callback updates the UI material/texture and UV rect → `OnDestroy()` / `OnDispose()` decrements the reference count and potentially returns the atlas region to free space.

### Configuration

`AtlasConfig` (`Assets/DynamicAtlas/AtlasConfig.cs`):
- `kUsingCopyTexture` **(static, mutable at runtime)** — `true` = Texture2D + `Graphics.CopyTexture`; `false` = RenderTexture + `Graphics.Blit` with custom shader
- `kTextureFormat` — `ARGB32` on all platforms (platform-specific branches exist but are identical)
- `kRenderTextureFormat` — `ARGB32` on all platforms

### Supporting Utilities (`Assets/Other/`)

- **`Singleton<T>`** — Thread-safe generic singleton with `new()` constraint (not a Unity `MonoBehaviour` singleton)
- **`UnityHelpCenter`** — MonoBehaviour that wraps `Resources.Load` / `Resources.LoadAsync` with a request queue; attached to a GameObject at runtime
- **`ResourcesManager`** — Static proxy forwarding to `UnityHelpCenter`
- **`CommonUtils`** — RenderTexture factory and file extension stripper
- **`AnoUtil`** — Single extension method: `List<T>.Pop()` (returns and removes last element)
- **`UITools`** — GameObject/Transform helpers (`AddChild`, `SetActiveVirtual`, `ResetRectTransform`)
- **`UnityForDelegateBase.cs`** — All delegate type definitions (`OnCallBack`, `OnCallBackMetRect`, `OnCallBackTexRect`, etc.)
- **`PathUtil`** — Asset path ↔ full disk path conversion, Resources path extraction

### Editor Tools (`Assets/DynamicAtlas/Editor/`)

- **`DynamicAtlasWindow`** — Runtime editor window to visualize atlas textures and free areas (green/yellow overlay). Only works in Play mode.
- **`PackingAtlasWindow`** — Same for PackingAtlas.
- Custom `Editor` classes for `UIDynamicImage`, `UIDynamicRawImage`, `UIPackingImage`, `UIPackingRawImage`.
- **`UIComponentMenuItemEditor`** — Adds "GameObject/UI/UIDynamicImage" and "UIDynamicRawImage" menu items to create these components in the scene.

### Custom Shader

`Assets/Resources/DynamicAtlasGraphicBlitShader.shader` — "DynamicAtlas/GraphicBlit" shader. Clips output by `_DrawRect` (xy = min, zw = max) and remaps UVs so only the targeted sub-region of the destination RT is drawn. Used by the Blit (RenderTexture) path.

## Key Patterns

- **Reference counting**: `SaveImageData.referenceCount` tracks how many UI components reference the same path. When it hits 0, the atlas region is freed.
- **Free area merging**: `DynamicAtlas` merges adjacent free rectangles when a texture is removed. `PackingAtlas` does not merge on removal.
- **Async loading**: `GetImage()` batches requests; only the first triggers a `Resources.LoadAsync` coroutine. Duplicate requests for the same path during the same load share the result.
- **Editor-only play guard**: UI components gate initialization behind `#if UNITY_EDITOR` + `Application.isPlaying` so they don't trigger atlas operations in edit mode.
