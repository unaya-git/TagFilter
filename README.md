# TagFilter - LoRA Dataset Tag Editor

A tool for auto-tagging images with WD14 Tagger and efficiently creating/editing LoRA training datasets.

[日本語版はこちら](README.ja.md)

---

![Main Screen](docs/screen_main.png)

---

## Changelog

### v1.2.3
- Overhauled tag classification categories

### v1.2.2
- Added **Copy Tags to Clipboard** button per image
- Added **Save .txt ON/OFF** toggle
- Added support for drag & drop of individual image files (in addition to folders)

### v1.2.1
- Improved error messages: tagging failures now show exception type and stack trace

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

Drag and drop an image **folder** or individual **image files** directly onto the window to load them. When a single image file is dropped, the entire folder containing that file is loaded.

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

### Copy Tags to Clipboard

Each image row has a **Copy Tags** button. Clicking it copies all tags for that image to the clipboard as a comma-separated string, respecting the current underscore setting.

### Save .txt Toggle

The **Save .txt** toggle in the toolbar controls whether tag data is written to `.txt` files on save. When OFF, tags are held in memory only and no files are written.

### Auto Save Settings

Model, device, threshold, language, underscore setting, save .txt setting, and unwanted tag list are auto-saved to `settings.xml` on exit.

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

---

## Train LoRA

Built-in LoRA training via kohya_ss. Switch to the **Train LoRA** tab in the right panel.

### Requirements

- kohya_ss installed locally (tested with standard install and StabilityMatrix)
- SDXL base model (.safetensors)
- VRAM 12 GB recommended (RTX 4070 / 4080 / 5070 etc.)

### Setup (Paths tab)

| Field | Description |
|---|---|
| kohya_ss folder | Root folder of kohya_ss (contains `venv/` and `sd-scripts/`) |
| pretrained_model | Path to SDXL base model (.safetensors) |
| output_dir | Folder where the trained .safetensors will be saved |
| output_name | Output filename (without extension) |
| repeats | Number of times each image is repeated per epoch |

Training images are taken from the folder currently open in the main window. No need to set up a separate folder structure — the app automatically creates the required `repeats_name/` subfolder for kohya_ss.

### Presets

| Preset | Target | VRAM |
|---|---|---|
| Anime SDXL | Illustration / anime style | ~16 GB |
| Photo SDXL | Real photos | ~16 GB |

Both presets have `gradient_checkpointing=true` set, so training should be possible with 16GB of VRAM or less. (My PC only has 12GB of VRAM, so I could only test the bare minimum.)

### Parameter Tabs

- **Network** — network_dim, network_alpha, epochs, batch size, resolution
- **LR** — learning_rate, unet_lr, text_encoder_lr, scheduler
- **Optimizer** — optimizer_type, mixed_precision, save_precision
- **Advanced** — noise_offset, min_snr_gamma, shuffle_caption, keep_tokens, clip_skip

Settings are auto-saved on close and restored on next launch.

### Batch Processing with LoRA Training

Add a 5th argument to trigger LoRA training automatically after tagging:

```
TagFilter.exe <folder> [LoRA mode] [insert tags] [""] [LoRA output name]
```

| Arg | Description |
|---|---|
| Arg 5 | LoRA output name — triggers training after tagging. Paths/params loaded from `lora_settings.xml`. |

**Example:**
```bat
@echo off
TagFilter.exe "E:\dataset\chara_A" 1 "" "" "chara_a_lora"
TagFilter.exe "E:\dataset\chara_B" 1 "" "" "chara_b_lora"
```

---

## License

[MIT License](LICENSE.txt)

Libraries: [Microsoft.ML.OnnxRuntime.DirectML](https://github.com/microsoft/onnxruntime) (MIT) / [OpenCvSharp4](https://github.com/shimat/opencvsharp) (Apache 2.0) / [Costura.Fody](https://github.com/Fody/Costura) (MIT)
