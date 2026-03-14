# TagFilter - LoRA Dataset Tag Editor

WD14 Tagger を使って画像に自動でタグを付け、LoRA学習用データセットを効率よく作成・編集するツールです。

---

## 動作環境

- Windows 10 / 11
- .NET Framework 4.8.1（Windows標準搭載）
- DirectX 12 対応GPU推奨（NVIDIA / AMD / Intel 全対応）、CPUでも動作可

---

## 主な機能

### 自動タグ付け

WD14 Tagger（ONNX）で画像を解析し、タグを自動生成します。結果は同名の `.txt` ファイルに自動保存されます。

- 左側の一覧で複数選択した場合は選択画像のみが対象、未選択なら全画像が対象
- **閾値スライダー**（デフォルト 0.35）でタグの検出感度を調整

### フォルダのドラッグ&ドロップ

画像フォルダをウィンドウに直接ドラッグ&ドロップして読み込めます。

### モデル選択

| モデル | サイズ | 特徴 |
|---|---|---|
| WD ViT v2（標準・軽量） | 約365MB | 標準的なアニメ・イラスト向け |
| WD SwinV2 v2（高精度） | 約365MB | v2系の高精度版 |
| WD ViT v3（実写向け改善） | 約365MB | 実写写真にも対応 |
| WD ViT Large v3（高精度） | 約1.2GB | v3大型・高精度 |
| WD Eva02 Large v3（最高精度） | 約2.5GB | v3最大・最高精度 |

未ダウンロードのモデルは初回選択時に自動ダウンロードされます。

### 推論カテゴリ（LoRAモード）

タグ付け結果を目的のカテゴリに絞り込めます。

| 番号 | モード |
|---|---|
| 0 | 全タグ（絞り込みなし） |
| 1 | 顔 LoRA |
| 2 | 服装 LoRA |
| 3 | 体 LoRA |
| 4 | ポーズ LoRA |
| 5 | 背景 LoRA |
| 6 | スタイル LoRA |
| 7 | 表現 LoRA |
| 8 | 作者名 |
| 9 | キャラクター |
| 10 | 作品名 |

### GPU / 並列処理

- **デバイス**：自動 / GPU（DirectML）/ CPU を切替可能
- **並列数**：CPU使用時はコア数に合わせて並列処理数を増やすと高速化できます
- GPU（DirectML）使用時は並列数1固定（DirectMLの仕様）

### タグ集計パネル

フォルダ内の全タグを件数付きで一覧表示。件数順・名前順の並び替えが可能。タグをクリックして「**選択タグを全削除**」で一括削除できます。

### 表示フィルタ

`すべて` / `顔` / `体` / `服装` / `ポーズ` / `背景` / `スタイル` / `表現` / `キャラ` / `作品名` / `作者` / `その他`

カテゴリで表示を絞り込み、特定カテゴリのタグをまとめて削除することもできます。

### タグの一括操作

- **一括挿入**：全画像（または選択画像）に同じタグを追加
- **一括削除**：指定タグを全画像から削除
- **不要タグ登録**：よく削除するタグをチップとして保存。次回からワンクリックで削除対象にセット

### 設定の自動保存

モデル・デバイス・閾値・不要タグ一覧などは終了時に `settings.xml` へ自動保存され、次回起動時に復元されます。

---

## コマンドライン・バッチ処理

引数を指定して起動することで、タグ付けを自動実行して終了するバッチ処理ができます。  
モデル・閾値・デバイスは `settings.xml` の設定値が使用されます。

```
TagFilter.exe <フォルダパス> [LoRAモード番号] [挿入タグ]
```

| 引数 | 必須 | 説明 |
|---|---|---|
| 引数1 | ○ | 画像フォルダのパス |
| 引数2 | △ | LoRAモード番号（0〜10、省略時はsettings.xml値） |
| 引数3 | △ | 一括挿入タグ（カンマ区切りで複数指定可、省略可） |

引数を1つでも指定した場合、タグ付け完了後に自動終了します。

**使用例：**

```bat
@echo off
rem 顔LoRA用・挿入タグあり
TagFilter.exe "E:\dataset\chara_A" 1 "chr"

rem 服装LoRA用・挿入タグなし
TagFilter.exe "E:\dataset\chara_A" 2

rem 複数フォルダを連続処理
TagFilter.exe "E:\dataset\chara_A" 1 "ch_a"
TagFilter.exe "E:\dataset\chara_B" 1 "ch_b"
TagFilter.exe "E:\dataset\chara_C" 1 "ch_c"
```

---

## インストール

1. [Releases](https://github.com/unaya-git/TagFilter/releases/latest) から最新の `TagFilter_vX.X.X.zip` をダウンロード
2. 任意のフォルダに展開して `TagFilter.exe` を実行

インストール不要・レジストリ書き込みなし。アンインストールはフォルダごと削除するだけです。

---

## 利用モデルについて

[SmilingWolf](https://huggingface.co/SmilingWolf) 氏が公開している WD14 Tagger モデルを使用しています。モデルは初回使用時にHugging Faceから自動ダウンロードされます。

---

## ライセンス

[MIT License](LICENSE.txt)

使用ライブラリ：[Microsoft.ML.OnnxRuntime.DirectML](https://github.com/microsoft/onnxruntime)（MIT）／[OpenCvSharp4](https://github.com/shimat/opencvsharp)（Apache 2.0）／[Costura.Fody](https://github.com/Fody/Costura)（MIT）
