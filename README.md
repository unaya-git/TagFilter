# TagFilter - LoRA Dataset Tag Editor

A tool for auto-tagging images with WD14 Tagger and efficiently creating/editing LoRA training datasets.

[日本語版はこちら](README.ja.md)

---

![Main Screen](docs/screen_main.png)

---

## Requirements

- Windows 10 / 11
- .NET Framework 4.8.1 (included in Windows)
- DirectX 12 compatible GPU recommended (NVIDIA / AMD / Intel), CPU also supported

---

## Main Features

### Auto Tagging

Analyzes images with WD14 Tagger (ONNX) and auto-generates tags. Results are saved to `.txt` files automatically.

- Selecting images in the left list tags only those images; otherwise all images are tagged
- **Threshold slider** (default 0.35) adjusts detection sensitivity

### Drag & Drop

Drag and drop an image folder directly onto the window to load it.

![Drag and Drop](docs/screen_dragdrop.png)

### Model Selection

| Model | Size | Description |
|---|---|---|
| WD ViT v2 (Standard) | ~365MB | Standard anime/illustration model |
| WD SwinV2 v2 (High Accuracy) | ~365MB | High accuracy v2 |
| WD ViT v3 (Photo Improved) | ~365MB | Better for real photos |
| WD ViT Large v3 (Large) | ~1.2GB | Large v3, high accuracy |
| WD Eva02 Large v3 (Best) | ~2.5GB | Best accuracy, slowest |

Models not yet downloaded are auto-downloaded on first selection.

### LoRA Mode (Inference Category)

Filter tag results by category:

| # | Mode |
|---|---|
| 0 | All Tags |
| 1 | Face LoRA |
| 2 | Outfit LoRA |
| 3 | Body LoRA |
| 4 | Pose LoRA |
| 5 | BG LoRA |
| 6 | Style LoRA |
| 7 | Expr LoRA |
| 8 | Artist |
| 9 | Character |
| 10 | Title |

### GPU / Parallel Processing

- **Device**: Auto / GPU (DirectML) / CPU
- **Parallel**: Increasing parallel count speeds up CPU inference
- GPU (DirectML) is fixed at parallel=1 (DirectML limitation)

### Language Switch

Click the **Language** button (top right) to toggle between Japanese and English.

![Language Switch](docs/screen_language.png)

### Underscore Setting

The **Under Score** button controls how tags are saved to `.txt` files.

| Setting | Saved Format |
|---|---|
| Save: _ | `brown_hair, long_hair` |
| Save: space | `brown hair, long hair` |

> Note: Tags are stored internally with underscores. The display in the app always shows underscores; only the saved `.txt` file is affected.

![Underscore Setting](docs/screen_underscore.png)

### Tag Stats Panel

Displays all tags with counts. Sortable by count or name. Click a tag then **Delete Selected** to bulk delete.

### Display Filter

`All` / `Face` / `Body` / `Outfit` / `Pose` / `BG` / `Style` / `Expr` / `Chara` / `Title` / `Artist` / `Other`

Filter the display by category and optionally bulk-delete all tags in that category.

### Bulk Operations

- **Bulk Insert**: Add the same tag to all (or selected) images
- **Delete All Tags**: Remove a specified tag from all images
- **Unwanted Tag Register**: Save frequently-deleted tags as chips for one-click deletion

### Auto Save Settings

Model, device, threshold, language, underscore setting, and unwanted tag list are auto-saved to `settings.xml` on exit.

---

## Batch Processing (Command Line)

Run auto-tagging automatically and exit when done — useful for batch processing multiple folders.

```
TagFilter.exe <folder> [LoRA mode #] [insert tags]
```

| Arg | Required | Description |
|---|---|---|
| Arg 1 | ✓ | Image folder path |
| Arg 2 | - | LoRA mode number (0–10, default: settings.xml value) |
| Arg 3 | - | Bulk insert tags (comma-separated, optional) |

When any argument is provided, the app exits automatically after tagging.

**Example batch file:**

```bat
@echo off
TagFilter.exe "E:\dataset\chara_A" 1 "masterpiece,best_quality"
TagFilter.exe "E:\dataset\chara_B" 1 "masterpiece,best_quality"
TagFilter.exe "E:\dataset\chara_C" 2
```

---

## Installation

1. Download the latest `TagFilter_vX.X.X.zip` from [Releases](https://github.com/unaya-git/TagFilter/releases/latest)
2. Extract to any folder and run `TagFilter.exe`

No installer required. No registry writes. Uninstall by deleting the folder.

---

## Model Credits

Uses WD14 Tagger models published by [SmilingWolf](https://huggingface.co/SmilingWolf). Models are auto-downloaded from Hugging Face on first use.

---

## License

[MIT License](LICENSE.txt)

## Disclaimer
This tool is for personal dataset management only.
Users are responsible for ensuring their use of this tool
complies with applicable laws and third-party rights.
The author is not responsible for any misuse.

Libraries: [Microsoft.ML.OnnxRuntime.DirectML](https://github.com/microsoft/onnxruntime) (MIT) / [OpenCvSharp4](https://github.com/shimat/opencvsharp) (Apache 2.0) / [Costura.Fody](https://github.com/Fody/Costura) (MIT)
