using System.Collections.Generic;

namespace TagFilter
{
    public enum AppLanguage { Japanese, English }

    public static class Strings
    {
        public static AppLanguage Language { get; set; } = AppLanguage.Japanese;

        private static string J(string ja, string en)
            => Language == AppLanguage.Japanese ? ja : en;

        // ── ツールバー ───────────────────────────────────────────────
        public static string BtnOpenFolder => J("フォルダを開く", "Open Folder");
        public static string BtnSaveAll => J("全て保存", "Save All");
        public static string BtnAutoTag => J("自動タグ付け", "Auto Tag");
        public static string LabelModel => J("モデル:", "Model:");
        public static string LabelInfer => J("推論:", "Infer:");
        public static string LabelThreshold => J("閾値:", "Threshold:");
        public static string LabelDevice => J("デバイス:", "Device:");
        public static string LabelParallel => J("並列数:", "Parallel:");
        public static string BtnUnderscoreOn => J("保存: _ あり", "Save: _");
        public static string BtnUnderscoreOff => J("保存: スペース", "Save: space");

        // デバイス選択肢
        public static string DeviceAuto => J("自動", "Auto");
        public static string DeviceGpu => "GPU";
        public static string DeviceCpu => "CPU";

        // GPU状態
        public static string GpuChecking => J("確認中...", "Checking...");
        public static string GpuAvailable => J("GPU(DirectML) 使用可", "GPU(DirectML) Ready");
        public static string GpuCpuOnly => J("CPU のみ", "CPU Only");

        // ── モデルステータス ──────────────────────────────────────────
        public static string ModelInUse => J("使用中", "In Use");
        public static string ModelDownloaded => J("DL済", "Ready");
        public static string ModelNotDL => J("未DL", "Not DL");

        // ── タグ集計パネル ────────────────────────────────────────────
        public static string TagSummaryTitle => J("タグ集計", "Tag Stats");
        public static string SortByCount => J("件数順", "By Count");
        public static string SortByName => J("名前順", "By Name");
        public static string BtnDeleteSelected => J("選択タグを全削除", "Delete Selected");
        public static string TxtSummaryInfo(int tagCount, int imgCount)
            => J($"（{tagCount} 種類 / {imgCount} 枚）",
             $"({tagCount} types / {imgCount} imgs)");

        // ── 表示フィルタ ──────────────────────────────────────────────
        public static string FilterAll => J("すべて", "All");
        public static string FilterFace => J("顔", "Face");
        public static string FilterBody => J("体", "Body");
        public static string FilterOutfit => J("服装", "Outfit");
        public static string FilterPose => J("ポーズ", "Pose");
        public static string FilterBackground => J("背景", "BG");
        public static string FilterStyle => J("スタイル", "Style");
        public static string FilterExpression => J("表現", "Expr");
        public static string FilterCharacter => J("キャラ", "Chara");
        public static string FilterCopyright => J("作品名", "Title");
        public static string FilterArtist => J("作者", "Artist");
        public static string FilterOther => J("その他", "Other");

        // ── 一括操作 ──────────────────────────────────────────────────
        public static string LabelBulkInsert => J("一括挿入（全画像）:", "Bulk Insert:");
        public static string BtnInsert => J("挿入", "Insert");
        public static string BtnDeleteCategory => J("選択中カテゴリを削除", "Delete Category");
        public static string LabelUnwantedTag => J("不要タグ削除（全画像）:", "Unwanted Tag:");
        public static string BtnRegister => J("登録", "Register");
        public static string BtnDeleteAllTag => J("このタグを全削除", "Delete All Tags");

        // ── タグ追加 ──────────────────────────────────────────────────
        public static string LabelAddTag => J("選択画像にタグを追加:", "Add Tag to Selected:");
        public static string BtnAdd => J("追加", "Add");


        public static string LblThreshold => J("閾値:", "Threshold:");
        public static string LblModel => J("モデル:", "Model:");
        public static string LblInfer => J("推論:", "Infer:");
        public static string LblDevice => J("デバイス:", "Device:");
        public static string LblParallel => J("並列数:", "Parallel:");
        public static string LblTagSummary => J("タグ集計", "Tag Stats");
        public static string LblSortOrder => J("並び順:", "Sort:");
        public static string LblFilter => J("表示フィルタ:", "Filter:");
        public static string LblBulkInsert => J("一括挿入（全画像）:", "Bulk Insert:");
        public static string LblUnwantedTag => J("不要タグ削除（全画像）:", "Unwanted Tag:");
        public static string LblAddTag => J("選択画像にタグを追加:", "Add Tag:");
        // ── LoRAモード名 ──────────────────────────────────────────────
        public static string[] LoraModeLabels
            => Language == AppLanguage.Japanese
            ? new[] { "全タグ（絞り込みなし）", "顔 LoRA", "服装 LoRA", "体 LoRA",
                       "ポーズ LoRA", "背景 LoRA", "スタイル LoRA", "表現 LoRA",
                       "作者名", "キャラクター", "作品名" }
            : new[] { "All Tags", "Face LoRA", "Outfit LoRA", "Body LoRA",
                       "Pose LoRA", "BG LoRA", "Style LoRA", "Expr LoRA",
                       "Artist", "Character", "Title" };

        // ── モデル名 ──────────────────────────────────────────────────
        public static string[] ModelNames
            => Language == AppLanguage.Japanese
            ? new[] {
        "WD ViT v2（標準・軽量）",
        "WD SwinV2 v2（高精度）",
        "WD ViT v3（実写向け改善）",
        "WD ViT Large v3（高精度）",
        "WD Eva02 Large v3（最高精度）" }
            : new[] {
        "WD ViT v2 (Standard)",
        "WD SwinV2 v2 (High Accuracy)",
        "WD ViT v3 (Photo Improved)",
        "WD ViT Large v3 (Large)",
        "WD Eva02 Large v3 (Best)" };

        // ── 初期ステータス ────────────────────────────────────────────
        public static string StatusReady => J("準備完了", "Ready");
        // ── カテゴリラベル ────────────────────────────────────────────
        public static Dictionary<BodyPartCategory, string> CategoryLabels
            => Language == AppLanguage.Japanese
            ? new Dictionary<BodyPartCategory, string>
            {
                { BodyPartCategory.Face,       "顔"      },
                { BodyPartCategory.Body,       "体"      },
                { BodyPartCategory.Outfit,     "服装"    },
                { BodyPartCategory.Pose,       "ポーズ"  },
                { BodyPartCategory.Background, "背景"    },
                { BodyPartCategory.Style,      "スタイル"},
                { BodyPartCategory.Expression, "表現"    },
                { BodyPartCategory.Artist,     "作者"    },
                { BodyPartCategory.Copyright,  "作品名"  },
                { BodyPartCategory.Character,  "キャラ"  },
                { BodyPartCategory.Other,      "その他"  },
            }
            : new Dictionary<BodyPartCategory, string>
            {
                { BodyPartCategory.Face,       "Face"   },
                { BodyPartCategory.Body,       "Body"   },
                { BodyPartCategory.Outfit,     "Outfit" },
                { BodyPartCategory.Pose,       "Pose"   },
                { BodyPartCategory.Background, "BG"     },
                { BodyPartCategory.Style,      "Style"  },
                { BodyPartCategory.Expression, "Expr"   },
                { BodyPartCategory.Artist,     "Artist" },
                { BodyPartCategory.Copyright,  "Title"  },
                { BodyPartCategory.Character,  "Chara"  },
                { BodyPartCategory.Other,      "Other"  },
            };

        // ── ステータスメッセージ ──────────────────────────────────────
        public static string StatusLoading => J("読み込み中...", "Loading...");
        public static string StatusLoadFailed => J("フォルダの読み込みに失敗しました", "Failed to load folder");
        public static string StatusNoFolder => J("⚠ フォルダを開いてください", "⚠ Open a folder first");
        public static string StatusNoImage => J("⚠ 左の一覧から画像を選択してください", "⚠ Select an image");
        public static string StatusNoTagInput => J("⚠ 挿入するタグを入力してください", "⚠ Enter a tag to insert");
        public static string StatusNoDelInput => J("⚠ 削除するタグを入力してください", "⚠ Enter a tag to delete");
        public static string StatusModelFailed => J("モデルのロードに失敗しました", "Failed to load model");
        public static string StatusModelSwFailed => J("モデルの切り替えに失敗しました", "Failed to switch model");

        public static string StatusLoaded(int n)
            => J($"{n} 件の画像を読み込みました", $"Loaded {n} images");
        public static string StatusInferring(string dev, int par, int cur, int total)
            => J($"推論中 [{dev} x{par}]  {cur}/{total} 枚",
                 $"Tagging [{dev} x{par}]  {cur}/{total}");
        public static string StatusDone(string mode, string dev, int par, int total)
            => J($"完了: [{mode} / {dev} x{par}]  {total} 枚",
                 $"Done: [{mode} / {dev} x{par}]  {total}");
        public static string StatusModelSwitching(string name)
            => J($"「{name}」に切り替え中...", $"Switching to {name}...");
        public static string StatusModelSwitched(string name)
            => J($"「{name}」に切り替えました", $"Switched to {name}");
        public static string StatusModelLoaded(string name)
            => J($"「{name}」のロード完了", $"{name} loaded");
        public static string StatusTagAdded(string tag, int count)
            => J($"追加: {tag}  ({count} tags)", $"Added: {tag}  ({count} tags)");
        public static string StatusTagExists(string tag)
            => J($"「{tag}」は既に存在します", $"'{tag}' already exists");
        public static string StatusTagDeleted(string tag, int count)
            => J($"「{tag}」を {count} 件削除しました", $"Deleted '{tag}' ({count})");
        public static string StatusTagNotFound(string tag)
            => J($"「{tag}」は見つかりませんでした", $"'{tag}' not found");
        public static string StatusInserted(string tag, string scope)
            => J($"「{tag}」を {scope} に挿入しました", $"Inserted '{tag}' to {scope}");
        public static string StatusCategoryDeleted(string label, string scope)
            => J($"「{label}」タグを {scope} から削除しました",
                 $"Deleted '{label}' tags from {scope}");
        public static string StatusInsertDone(string tags)
            => J($"挿入完了: {tags}", $"Inserted: {tags}");
        public static string StatusSavedAll(int n)
            => J($"全 {n} 件を保存しました", $"Saved {n} files");
        public static string StatusDeleted(string tag) // タグ集計パネルから削除後
            => J($"「{tag}」を全削除しました", $"Deleted all '{tag}'");

        // スコープ
        public static string ScopeSelected(int n) => J($"選択中 {n} 枚", $"Selected {n}");
        public static string ScopeAll(int n) => J($"全 {n} 枚", $"All {n}");

        // 動的ボタンラベル
        public static string BtnDeleteCategoryLabel(string label)
            => J($"「{label}」タグを全削除", $"Delete '{label}'");
        public static string BtnDeleteSelectedLabel(string tag)
            => J($"「{tag}」を全削除", $"Delete '{tag}'");
        public static string BtnDeleteCategoryDefault
            => J("選択中カテゴリを削除", "Delete Category");
        public static string BtnDeleteSelectedDefault
            => J("選択タグを全削除", "Delete Selected");

        // ── MessageBox ───────────────────────────────────────────────
        public static string MsgOneDriveTitle => J("OneDriveファイルの警告", "OneDrive Warning");
        public static string MsgOneDrive(int n)
            => J($"{n} 件のファイルがOneDriveのオンライン専用状態です。\n\n" +
                  "自動タグ付け前に全ファイルを右クリック→\n「常にこのデバイスに保存」でダウンロードしてください。",
                 $"{n} file(s) are OneDrive online-only.\n\n" +
                  "Right-click → 'Always keep on this device' before tagging.");

        public static string MsgDownloadTitle => J("モデルが未ダウンロード", "Model Not Downloaded");
        public static string MsgDownload(string name)
            => J($"モデル「{name}」がダウンロードされていません。\n\n" +
                  "Hugging Face から自動ダウンロードしますか？\n（約350〜700MB）",
                 $"Model '{name}' is not downloaded.\n\n" +
                  "Download from Hugging Face?\n(~350-700MB)");

        public static string MsgDownloadFailed => J("ダウンロードに失敗しました:", "Download failed:");
        public static string MsgInferError => J("推論エラー:", "Inference error:");
        public static string MsgModelSwitchError => J("モデル切り替えエラー", "Model Switch Error");
        public static string MsgModelReloadError => J("モデル再ロードエラー", "Model Reload Error");
        public static string MsgFolderNotFound => J("フォルダが見つかりません:", "Folder not found:");
        public static string MsgArgError => J("引数エラー", "Argument Error");
        public static string MsgFolderLoadError => J("フォルダ読み込みエラー:", "Folder load error:");
        public static string MsgThumbnailWarning => J("サムネイル警告", "Thumbnail Warning");
        public static string MsgThumbnailFailed => J("以下の画像でサムネイル生成に失敗しました:\n\n",
                                                       "Failed to generate thumbnails:\n\n");
        public static string MsgTagError => J("タグ付けエラー", "Tagging Error");
        public static string MsgError => J("エラー", "Error");

        public static string MsgBulkDeleteTitle => J("一括削除の確認", "Confirm Delete");
        public static string MsgBulkDeleteCategory(string scope, string label)
            => J($"{scope} の画像から\n「{label}」カテゴリのタグを全て削除します。\n\nよろしいですか？",
                 $"Delete all '{label}' tags\nfrom {scope}?\n\nAre you sure?");
        public static string MsgBulkDeleteTag(string scope, string tag, int count)
            => J($"{scope} の画像から\n「{tag}」を {count} 件削除します。\n\nよろしいですか？",
                 $"Delete '{tag}' ({count} item(s))\nfrom {scope}?\n\nAre you sure?");
    }
}
