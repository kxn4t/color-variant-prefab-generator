# アーキテクチャ

Color Variant Prefab Generatorのコードアーキテクチャ、データモデル、アルゴリズム、内部設計を記述する。

プロダクトレベルの機能説明とUI仕様は [specification.md](specification.md) を参照。

---

## 目次

- [パッケージ構成](#パッケージ構成)
- [レイヤー概要](#レイヤー概要)
- [Core層](#core層)
  - [データモデル (ColorVariantData.cs)](#データモデル-colorvariantdatacs)
  - [PrefabScanner](#prefabscanner)
  - [RendererMatcher](#renderermatcher)
  - [レンダラーマッチングアルゴリズム](#レンダラーマッチングアルゴリズム)
  - [PrefabVariantGenerator](#prefabvariantgenerator)
  - [VariantAnalyzer](#variantanalyzer)
- [Localization層](#localization層)
- [UI層](#ui層)
  - [EditorUIUtility](#editoruiutility)
  - [CreatorWindow (CV Creator)](#creatorwindow-cv-creator)
  - [BatchGeneratorWindow (Batch Generator)](#batchgeneratorwindow-batch-generator)
  - [MaterialBrowserPanel](#materialbrowserpanel)
- [データフロー](#データフロー)
- [依存関係](#依存関係)
- [コーディング規約](#コーディング規約)

---

## パッケージ構成

```
Editor/
├── Core/                        # コアロジック（UIに依存しない）
│   ├── ColorVariantData.cs      # データモデル
│   ├── PrefabScanner.cs         # Renderer/マテリアルスロットのスキャン
│   ├── PrefabVariantGenerator.cs# Prefab Variant生成
│   ├── RendererMatcher.cs       # 4段階スロットマッチングアルゴリズム
│   └── VariantAnalyzer.cs       # 既存Variant解析
│
├── Localization/                # ローカライズ（NDMFオプショナル依存）
│   ├── Localization.cs          # #if CVG_HAS_NDMF で分岐するラッパー
│   ├── en-us.po                 # 英語翻訳
│   └── ja-jp.po                 # 日本語翻訳
│
└── UI/                          # UIToolkitベースのエディターウィンドウ
    ├── EditorUIUtility.cs       # 共通UIユーティリティ
    ├── Creator/                 # CV Creatorウィンドウ
    │   ├── CreatorWindow.cs               # メインウィンドウ（partial classの起点）
    │   ├── CreatorWindow.BasePrefab.cs    # Base Prefabセクション + インポート
    │   ├── CreatorWindow.MaterialSlots.cs # マテリアルスロットUI
    │   ├── CreatorWindow.Preview.cs       # Sceneプレビュー
    │   ├── CreatorWindow.Output.cs        # 出力設定 + 生成
    │   ├── CreatorWindow.uss              # スタイル
    │   └── MaterialBrowserPanel.cs        # マテリアルフォルダーブラウザー
    │
    └── BatchGenerator/          # Batch Generatorウィンドウ
        ├── BatchGeneratorWindow.cs              # メインウィンドウ（partial classの起点）
        ├── BatchGeneratorWindow.VariantList.cs  # バリアント一覧セクション
        ├── BatchGeneratorWindow.Matching.cs     # マッチング結果セクション
        ├── BatchGeneratorWindow.Output.cs       # 出力設定 + 一括生成
        └── BatchGeneratorWindow.uss             # スタイル
```

## レイヤー概要

コードベースは3つのレイヤーで構成され、依存方向は一方向:

```
UI層  →  Core層
 ↓
Localization層
```

| レイヤー | 責務 |
|---|---|
| **Core** | 純粋なデータ処理 — スキャン、マッチング、生成。UIに依存しない。 |
| **Localization** | 翻訳の抽象化。NDMFの有無でコンパイル時に分岐。 |
| **UI** | UIToolkitベースのエディターウィンドウ。ロジックはすべてCoreを呼び出す。 |

---

## Core層

### データモデル (ColorVariantData.cs)

ツール全体で使われる共通データ型。各フィールドの詳細はソースコードのXMLコメントを参照。

| クラス | 用途 |
|---|---|
| `MaterialSlotIdentifier` | Prefab内の特定Renderer・スロットの一意識別子。`rendererPath` + `slotIndex`で等価性を判定。`DisplayName`（UI表示用）と`GetLookupKey()`（辞書キー）を提供。 |
| `MaterialOverride` | 1スロットに対するマテリアル差し替え命令（スロット + 差し替え先マテリアル）。 |
| `MaterialOverrideInfo` | `MaterialOverride`にベースマテリアル情報を加えた拡張版。`RendererMatcher`が生成する。 |
| `ScannedMaterialSlot` | `PrefabScanner`が検出した1スロット（識別子 + 現在のマテリアル）。 |
| `RendererMatchResult` | ソース→ターゲットのスロットマッチング結果（優先度1–4、0は手動）。 |
| `VariantAnalysisResult` | 既存Prefab Variantの解析結果（バリアント名 + オーバーライド一覧）。 |
| `GenerationResult` | Prefab Variant生成の成否とファイルパス。 |

---

### PrefabScanner

`static class` — PrefabやHierarchyインスタンスからRendererとマテリアルスロットをスキャンする。

- `ScanRenderers(root)` — `GetComponentsInChildren<Renderer>(true)`で全Renderer（非アクティブ含む）を取得し、各`sharedMaterials`スロットを`ScannedMaterialSlot`として返す。
- `GetRelativePathFromRoot(transform, root)` — ルートからの相対パスを`/`区切りで算出する。

---

### RendererMatcher

`static class` — 4段階アルゴリズムで2つのPrefab間のマテリアルスロットを照合する。

#### `CompareRenderers(sourceSlots, targetSlots) → List<RendererMatchResult>`

Batch GeneratorとCV CreatorのImport機能の両方で使用される主要メソッド。各ソーススロットを`TryMatch()`で最適なターゲットに照合し、**マテリアルが異なるスロットのみ**を結果に含める（差分出力）。未マッチのソーススロットは`targetSlot = null`で含まれる。

> `MatchRenderers()`も公開APIとして存在するが、組み込みUIからは`CompareRenderers`を使用。

### レンダラーマッチングアルゴリズム

`TryMatch()`メソッドが実装する4段階の優先度システム:

| 優先度 | 条件 | 説明 |
|---|---|---|
| **P1** | `rendererPath`完全一致 + `objectName`一致 + `slotIndex`一致 | 完全パス一致 — 同じ位置のRenderer |
| **P2** | `hierarchyDepth`一致 + `objectName`一致 + `slotIndex`一致 | 同じ深さ・同名 — 親パスが異なる |
| **P3** | `objectName`一致 + `slotIndex`一致（深さ不問） | 名前のみで照合、もっとも近い深さを優先 |
| **P4** | `objectName`大文字小文字無視一致 + `slotIndex`一致 | 大文字小文字を無視したフォールバック |

各段階は順番に試行される。P1でマッチが見つかればP2–P4はスキップ。

#### タイブレーク (`SelectBestCandidate`)

同一優先度で候補が複数ある場合の絞り込み:

1. **ベースマテリアル名の一致** — ソースのベースマテリアル名と一致する候補を優先
2. **階層深度の近さ** — 深度差がもっとも小さい候補を優先
3. **パスの類似度** — パス文字列のレーベンシュタイン距離が最小の候補を優先

マッチ済みターゲットキーの`HashSet`により、重複マッチを防止する。

---

### PrefabVariantGenerator

`static class` — マテリアルのみのオーバーライドを持つPrefab Variantを生成する。

- `GenerateVariant(basePrefabAsset, overrides, variantName, outputPath, namingTemplate)` — ベースPrefabから新規インスタンスを生成し、マテリアルオーバーライドを適用して`SaveAsPrefabAsset()`で保存する。
- `GenerateVariantsBatch(...)` — `AssetDatabase.StartAssetEditing()`でインポートを一時停止しつつ、プログレスバー付きで`GenerateVariant()`を繰り返し呼び出す。

**重要**: プレビューインスタンスは使わず、常に新規インスタンスから生成する。これによりマテリアル変更のみがオーバーライドとして記録される。

---

### VariantAnalyzer

`static class` — 既存のPrefab Variantを解析する。

- `DeriveVariantName(baseName, variantName)` — ベースPrefab名をプレフィックスとして除去し、バリアント名部分を抽出する。CV Creator（Import）とBatch Generatorの両方で使用。
  - 例: `"Airi_HonmeiKnit"` + `"Airi_HonmeiKnit_Black"` → `"Black"`

> `AnalyzeVariant()`も公開APIとして存在するが、組み込みUIからは直接比較方式（`CompareRenderers`）に移行済み。

---

## Localization層

日本語UI対応のためのNDMFオプショナル依存。asmdefの`versionDefines`でNDMFを検出し、`CVG_HAS_NDMF`シンボルを定義する。

### Localization.cs

NDMFの有無にかかわらず統一APIを提供するstaticラッパー。NDMFのAPI呼び出しはすべてこのファイルに集約されており、UIコードは`#if CVG_HAS_NDMF`を使わない。

| メソッド | NDMFあり | NDMFなし |
|---|---|---|
| `S(key)` | `Localizer.TryGetLocalizedString`で現在の言語に応じた翻訳を返す | `LocalizationAsset.GetLocalizedString`で`en-us.po`から英語文字列を返す |
| `S(key, args)` | 上記 + `string.Format` | 同左 |
| `ShowLanguageUI()` | `LanguageSwitcher.DrawImmediate()`で言語切替ドロップダウンを描画 | no-op |
| `LocalizeUIElements(root)` | `Localizer.LocalizeUIElements()`を呼び出し（`ndmf-tr`CSSクラスの要素を自動翻訳） | `ndmf-tr`クラスの要素をリフレクションで走査し、英語文字列を設定 |
| `RegisterLanguageChangeCallback<T>()` | `LanguagePrefs`で弱参照コールバックを登録（自動クリーンアップ） | no-op |

**翻訳ファイル**: GNU gettext `.po`形式（`en-us.po`, `ja-jp.po`）。Unityが`LocalizationAsset`として自動認識する。

**NDMFあり（英語選択時）とNDMFなしでのUIテキスト**: 言語切替ドロップダウンの有無を除き、表示内容は同一。

---

## UI層

すべてのUIはUIToolkit（`VisualElement`, USS）で構築。IMGUIはNDMF言語切替のみ（`IMGUIContainer`でラップ）。

### EditorUIUtility

`static class` — 両ウィンドウで共有されるユーティリティと定数。

- `DefaultNamingTemplate` = `"{BaseName}_{VariantName}"`、UI記号定数（`Arrow`, `Cross`, `DropdownArrow`, `Pencil`, `Warning`等）
- `DeduplicateVariantNames(names)` — 名前リストの重複を検出し、重複する名前すべてに`_1`, `_2`, `_3` …サフィックスを付与（一意な名前はそのまま）
- `SetMaterialPreview(imageElement, material)` — マテリアルプレビュー画像を非同期で設定（初期遅延100ms、以降200ms間隔で最大20回リトライ）
- その他: `ResolveFileName`, `NormalizePath`, `TryGetDraggedFolderPath`, `ToProjectRelativePath`, `IsValidOutputPath`, `RegisterFolderDrop`等のユーティリティ

### CreatorWindow (CV Creator)

`partial class`で5つのファイルに分割。`TwoPaneSplitView`による左右2ペインレイアウト:
- **左ペイン**: Base Prefab → Material Slots → Output Settings → Actions
- **右ペイン**: Material Browserパネル

#### 主要な状態

| フィールド | 説明 |
|---|---|
| `_baseInstance` | HierarchyのPrefabインスタンス（シーンオブジェクト） |
| `_basePrefabAsset` | 元となるPrefabアセット |
| `_scannedSlots` | スキャンされた全マテリアルスロット |
| `_overrides` | ユーザーが設定したオーバーライド（スロット→マテリアルの辞書） |
| `_originalMaterials` | プレビューリセット用の元マテリアル |
| `_preExistingOverrides` | ツール使用前から存在していたPrefabオーバーライド |
| `_ancestorChain` | ベースPrefabの祖先チェーン（ルートから順、Variant Parent選択用） |
| `_selectedVariantParent` | 生成時に使用する親Prefab（ドロップダウンで選択） |
| `_previewActive` | Sceneプレビューが有効か |

#### ファイルごとの責務

| ファイル | セクション |
|---|---|
| `CreatorWindow.cs` | ウィンドウライフサイクル、`CreateGUI()`、Undo/Redoハンドリング |
| `CreatorWindow.BasePrefab.cs` | Base Prefab ObjectField、Import from Prefabセクション、祖先チェーン構築 |
| `CreatorWindow.MaterialSlots.cs` | スロット一覧UI、D&Dハンドリング、オーバーライド変更イベント |
| `CreatorWindow.Preview.cs` | `ApplyPreviewMaterial()`、`ResetPreview()`、Revertモード |
| `CreatorWindow.Output.cs` | 出力設定、Variant Parent選択ドロップダウン、バリデーション、Generateボタン、次アクションダイアログ |

### BatchGeneratorWindow (Batch Generator)

`partial class`で4つのファイルに分割。単一の`ScrollView`レイアウト。

#### VariantEntry（内部クラス）

バリアント一覧の1行分のデータ:

| フィールド | 説明 |
|---|---|
| `variantPrefab` | ユーザーが指定したソースPrefab（任意のPrefab） |
| `matchResults` | `CompareRenderers()`による`List<RendererMatchResult>` |
| `customVariantName` | ユーザーが手動編集したバリアント名 |
| `isNameManuallyEdited` | 名前が手動編集されたかフラグ |
| `autoVariantName` | 自動導出されたバリアント名 |
| `EffectiveVariantName` | `customVariantName`があればそれを、なければ`autoVariantName`を返す |

#### ファイルごとの責務

| ファイル | セクション |
|---|---|
| `BatchGeneratorWindow.cs` | ウィンドウライフサイクル、ベースPrefab管理、状態管理 |
| `BatchGeneratorWindow.VariantList.cs` | バリアント一覧UI、行の追加/削除、D&D、名前編集 |
| `BatchGeneratorWindow.Matching.cs` | `RunAllMatching()`、結果UI、手動割り当て、Exclude |
| `BatchGeneratorWindow.Output.cs` | 出力設定、一括生成、上書き確認、サマリーダイアログ |

### MaterialBrowserPanel

`VisualElement`サブクラス — Creatorの右ペインに配置されるマテリアルフォルダーブラウザー。

**主なメソッド**:
- `SetRootFolder(path)` — ルートフォルダーを設定し、マテリアル一覧を更新
- `RefreshMaterialList()` — スキャン、サブフォルダーごとにグループ化、アイテムを描画
- `CollectMaterialsBySubfolder(rootFolder)` — `AssetDatabase.FindAssets("t:Material")`でマテリアルを検索し、サブフォルダーパスをキーとした辞書に分類
- `HighlightMaterial(material)` — 指定マテリアルまでスクロールし、約2秒間ハイライト
- `RegisterMaterialInteraction(item, material)` — クリック（Inspector選択）とドラッグ（D&D開始）のインタラクションを設定

**表示モード**（スライダー0–96で制御）:
- `< 16` → リストモード（20×20アイコン + 名前）
- `≥ 16` → グリッドモード（サムネイルサイズがスライダー値に連動）

---

## データフロー

### CV Creator

```
Hierarchyインスタンス
    │
    ▼
PrefabScanner.ScanRenderers()
    │
    ▼
ユーザーがマテリアルを差し替え  ──→  ApplyPreviewMaterial()（Sceneプレビュー）
    │
    ▼
「Generate」ボタン
    │
    ▼
PrefabVariantGenerator.GenerateVariant()
    │
    ▼
Prefab Variantファイル (.prefab)
```

### CV Creator — Prefabからのインポート

```
ソースPrefab (B)                ベースインスタンス（スキャン済み）
    │                                  │
    ▼                                  ▼
PrefabScanner.ScanRenderers()       _scannedSlots
    │                                  │
    └──────────┬───────────────────────┘
               ▼
RendererMatcher.CompareRenderers(sourceSlots, baseSlots)
               │
               ▼
         差分のみの結果 → _overrides → Sceneプレビュー
```

### Batch Generator

```
ソースPrefab (B₁, B₂, B₃...)       新ベースPrefab (A)
    │                                      │
    ▼                                      ▼
PrefabScanner.ScanRenderers()       PrefabScanner.ScanRenderers()
    │                                      │
    └──────────────┬───────────────────────┘
                   ▼
    RendererMatcher.CompareRenderers(sourceSlots, baseSlots)
                   │
                   ▼
    RendererMatchResult[]（マテリアル差分のみ）
                   │
                   ▼
    ユーザーが未マッチを確認/修正
                   │
                   ▼
    「Generate All」ボタン
                   │
                   ▼
    PrefabVariantGenerator.GenerateVariantsBatch()
                   │
                   ▼
    AのPrefab Variantファイル群 (.prefab)
```

---

## 依存関係

| 依存 | 必須 | 用途 |
|---|---|---|
| Unity 2022.3+ | はい | 最低Unityバージョン |
| NDMF 1.11.0+ | いいえ（オプション） | 日本語ローカライズUI。asmdefの`versionDefines`でコンパイル時に検出 → `CVG_HAS_NDMF`シンボル |

VRChat SDK、Modular Avatar、その他パッケージへの依存はない。標準のUnity Editor API（`PrefabUtility`, `AssetDatabase`, `UIToolkit`）のみを使用。

---

## コーディング規約

| 項目 | 規約 |
|---|---|
| 名前空間 | `Kanameliser.ColorVariantGenerator` |
| メニューパス | `Tools/Color Variant Prefab Generator/Creator`, `Tools/Color Variant Prefab Generator/Batch Generator` |
| ログ接頭辞 | `[Color Variant Generator]` |
| UIフレームワーク | UIToolkit（VisualElement, USS） |
| USS配置 | 対応するウィンドウファイルと同ディレクトリ |
| エラーハンドリング | try-catchブロック + `Debug.LogError` / `Debug.LogWarning` |
| Undo対応 | `Undo.RecordObject` / `Undo.SetCurrentGroupName` |
| アセンブリ | `Kanameliser.ColorVariantGenerator.Editor`（Editor-only） |
