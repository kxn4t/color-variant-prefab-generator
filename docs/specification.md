# 仕様書

Color Variant Prefab Generatorのプロダクト仕様書。各ツールの機能、UIの構成、期待される動作を記述する。

コードアーキテクチャと実装の詳細は [architecture.md](architecture.md) を参照。

---

## 目次

- [概要](#概要)
- [基本概念](#基本概念)
- [ツール構成](#ツール構成)
- [ワークフロー](#ワークフロー)
- [CV Creator — 詳細仕様](#cv-creator--詳細仕様)
  - [ベースPrefab指定](#ベースprefab指定)
  - [マテリアルブラウザー](#マテリアルブラウザー)
  - [マテリアルスロットUI](#マテリアルスロットui)
  - [Sceneプレビュー](#sceneプレビュー)
  - [Prefabからのインポート](#prefabからのインポート)
  - [出力と生成](#出力と生成)
- [Batch Generator — 詳細仕様](#batch-generator--詳細仕様)
  - [入力](#入力)
  - [マッチングと差分検出](#マッチングと差分検出)
  - [マッチング結果UI](#マッチング結果ui)
  - [出力と一括生成](#出力と一括生成)
- [命名テンプレート](#命名テンプレート)
- [バリデーション](#バリデーション)
- [エラーハンドリング](#エラーハンドリング)

---

## 概要

Color Variant Prefab Generatorは、カラーバリエーションの **Prefab Variant** 作成を自動化するUnityエディター拡張。色ごとにPrefabを丸ごと複製する代わりに、**マテリアルのオーバーライドのみ**を保持するPrefab Variantを生成する。ベースPrefabへの変更はすべてのVariantに自動反映される。

本ツールは2つのエディターウィンドウで構成される:

| ツール | 用途 |
|---|---|
| **CV Creator** | マテリアルブラウザーとSceneプレビューを使い、カラバリを1色ずつ作成 |
| **Batch Generator** | ベースPrefabとソースPrefabのマテリアル差分を検出し、Variantを一括生成 |

## 基本概念

- **Prefab Variant**: ベースPrefabを継承し、差分（オーバーライド）のみを保存するUnity Prefab。ベースへの変更は全Variantに自動反映される。
- **マテリアルオーバーライド**: マテリアルスロット1つ分の差し替え情報。本ツールが生成するオーバーライドはマテリアルのみで、Transform・コンポーネント等は含まない。
- **ソースPrefab**: マテリアルの取得元となる既存Prefab（VariantでもRegularでも可）。Prefab Variantである必要は**ない**。
- **レンダラーマッチング**: 階層構造が異なる2つのPrefab間でマテリアルスロットを対応付ける処理。[4段階マッチングアルゴリズム](architecture.md#レンダラーマッチングアルゴリズム)を使用する。

## ツール構成

### CV Creator

カラバリを1色ずつ作成するビジュアルエディター。最初のカラバリセット作成に適している。

**メニュー**: `Tools > Color Variant Prefab Generator > Creator`

**主な特徴**:
- 左右2ペインレイアウト: 左にマテリアルスロット、右にマテリアルブラウザー
- Sceneビューでのリアルタイムプレビュー（Undo/Redo対応）
- ブラウザーからスロットへのドラッグ＆ドロップによるマテリアル割り当て
- 既存Prefabからのマテリアル設定インポート

### Batch Generator

既存のカラバリPrefabをもとにVariantを一括生成するツール。あるアバター用に作ったカラバリを別のアバターに展開する場面を想定している。

**メニュー**: `Tools > Color Variant Prefab Generator > Batch Generator`

**主な特徴**:
- ソースPrefabとベースPrefabを直接比較し、マテリアル差分を自動検出
- ソースPrefabは任意のPrefabを指定可能（Prefab Variantに限定されない）
- マッチング結果のレビューと未マッチスロットの手動割り当て
- プログレスバーとサマリーダイアログ付きの一括生成

---

## ワークフロー

2つのツールはパイプラインとして連携する設計だが、それぞれ単独でも使用可能。

```
┌─────────────────────────────────────────────────────────┐
│ CV Creator（最初のアバター用）                            │
│                                                          │
│ 1. ベースPrefabのインスタンスをHierarchyに配置            │
│ 2. Rendererとマテリアルスロットをスキャン                  │
│ 3. ブラウザーでマテリアルフォルダーを設定                   │
│ 4. D&Dでマテリアルを差し替え                              │
│ 5. Sceneビューで変更をプレビュー                          │
│ 6. バリアント名を付けてPrefab Variantを生成               │
│ 7. 各色について繰り返し                                   │
│                                                          │
│ 出力: カラバリPrefab Variant                              │
│       （例: HonmeiKnit_Black.prefab）                     │
└────────────────────┬────────────────────────────────────┘
                     │ 作成したVariantをソースとして使用
                     ▼
┌─────────────────────────────────────────────────────────┐
│ Batch Generator（追加アバター用）                         │
│                                                          │
│ 1. 新しいベースPrefab(A)を設定                           │
│ 2. ソースPrefab(B)を追加 — 既存のカラバリ                 │
│ 3. AとBを比較 → マテリアル差分を自動検出                   │
│ 4. 未マッチスロットを確認・手動修正                        │
│ 5. 出力設定をして一括生成                                 │
│                                                          │
│ 出力: Bのマテリアルを適用したAのPrefab Variant群           │
└─────────────────────────────────────────────────────────┘
```

---

## CV Creator — 詳細仕様

### ベースPrefab指定

**Hierarchyのインスタンス**（Projectアセットではなく）を指定する。

- **ObjectField**: `GameObject`, `allowSceneObjects = true`
- Sceneプレビューのためにシーン上のインスタンスが必要
- `PrefabUtility.GetCorrespondingObjectFromSource()`でPrefabアセットを取得
- 指定時に全Rendererとマテリアルスロットを自動スキャン
- Projectアセットを指定した場合は警告を表示

**Optionsメニュー (▼)**: ObjectFieldの右に配置。「Import from Prefab」機能へのアクセスを提供（[Prefabからのインポート](#prefabからのインポート)参照）。

### マテリアルブラウザー

フォルダー内のマテリアルを閲覧・選択するためのパネル（右ペイン）。

```
┌─ Material Browser ─────────────────────────────────┐
│ Root Folder: [Assets/.../Materials/] [▼]           │
│                                                     │
│ グリッド表示:                                        │
│ ─── 01_Hat ──────────────────────────              │
│ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐               │
│ │[サム]│ │[サム]│ │[サム]│ │[サム]│               │
│ │Brown │ │Black │ │Blue  │ │Pink  │               │
│ └──────┘ └──────┘ └──────┘ └──────┘               │
│                                                     │
│ リスト表示:                                          │
│ ─── 01_Hat ──────────────────────────              │
│ [■] Brown                                           │
│ [■] Black                                           │
│ [■] Blue                                            │
│                                                     │
│ (マテリアルをスロットにD&D可能)                       │
│ [↻]                    [═══════════════●] ← スライダー│
│  ↑ 更新ボタン                                        │
└─────────────────────────────────────────────────────┘
```

**動作**:

- **ルートフォルダー**: 1つ指定すると、配下の全マテリアルをサブフォルダーごとにグループ化して表示
  - Projectウィンドウからフォルダーをドラッグ＆ドロップでも指定可能
- **セクションヘッダー**: ルートフォルダーからの相対パスを表示
- **更新ボタン**（左下）: フォルダーを再スキャンし、全マテリアルプレビューを再読み込み
- **表示モードスライダー**（右下、UnityのProjectウィンドウと同じUX）:
  - 左端 → リスト表示（20×20アイコン + マテリアル名）
  - 右方向 → グリッド表示（サムネイルサイズがスライダー値に連動）
  - デフォルト: 48pxグリッド表示
- **サムネイル**: `AssetPreview.GetAssetPreview`で生成
- **クリック**: Inspectorでマテリアルを選択（`Selection.activeObject`）。D&D開始時は発火しない（MouseUpで判定）
- **ドラッグ**: `DragAndDrop`操作を開始し、マテリアルスロットへの割り当てが可能

### マテリアルスロットUI

スキャンされた全Rendererのマテリアルスロットを、Rendererごとにグループ化して表示。

```
┌─ Material Slots ──────────────────────────────────────────────┐
│ ▼ Hat [SkinnedMeshRenderer]     ← クリックでHierarchy選択     │
│   [0] [■サムネ] Hat_Brown  →  [■サムネ] [ObjectField: Hat_Black] [×]│
│   [1] [■サムネ] Metal      →  (変更なし)                      │
│ ▼ Knit [SkinnedMeshRenderer]                                   │
│   [0] [■サムネ] Knit       →  [■サムネ] [ObjectField: Knit_Black][×]│
│ ▼ Skirt [SkinnedMeshRenderer]                                  │
│   [0] [■サムネ] Skirt      →  [■サムネ] [ObjectField: _________ ][×]│
│   [1] [■サムネ] Skirt_In   →  (変更なし)                      │
└────────────────────────────────────────────────────────────────┘
```

**各スロット行の構成**:
- **左側**（読み取り専用）: ベースマテリアルのサムネイル + 名前
- **矢印** (`→`): Before/Afterの視覚的な区切り
- **右側**: オーバーライドマテリアルのサムネイル + ObjectField（またはD&Dターゲット）
- **クリアボタン** (`×`): そのスロットのオーバーライドをリセット

**インタラクション**:
- **Rendererヘッダークリック**: 対応するGameObjectをHierarchyで選択
- **スロット行クリック**: ベースマテリアルをInspectorで選択。ブラウザーに該当マテリアルが存在する場合は自動スクロール＋約2秒間ハイライト
- **オーバーライドサムネイルクリック**: オーバーライドマテリアルをInspectorで選択（同様にブラウザーでハイライト）
- **ドラッグ＆ドロップ**: 行全体がマテリアルのドロップターゲット（ブラウザーまたはProjectウィンドウから）

### Sceneプレビュー

マテリアル変更をHierarchyのインスタンスにリアルタイム反映し、視覚的に確認できる機能。

**仕組み**:
- マテリアル割り当てのたびに`Renderer.sharedMaterials`を即座に変更
- すべての変更は`Undo.RecordObject`で記録（Undo/Redo対応）
- `Undo.undoRedoPerformed`コールバックでRendererの実状態からオーバーライド辞書とUIを同期
- Sceneビューは自動的に再描画

**Clear Overridesボタン**（スプリットボタン）:
- **メインボタン**: 全オーバーライドをクリアし、プレビューをリセット
- **ドロップダウン (▾)**: Prefab Revert Modeを選択:

| モード | 動作 |
|---|---|
| **Visual Only** | マテリアル値を視覚的に復元するのみ。Prefabオーバーライドマークは残る。 |
| **Selective Revert** | 本ツールが追加したPrefabオーバーライドのみをRevert（`PrefabUtility.RevertPropertyOverride`）。ツール使用前から存在していたオーバーライドは保持。 |
| **Full Revert** | 変更対象の全Rendererのオーバーライドをすべてrevert（`PrefabUtility.RevertObjectOverride`）。 |

- 選択モードは`EditorPrefs`に保存（マシン単位で永続化）
- ボタンラベルは選択モードに応じて動的に変化（例:「Clear & Selective Revert」）

**ウィンドウを閉じた場合の動作**: マテリアル変更はHierarchyのインスタンス上に残る（`Undo.RecordObject`で記録済みのため、Ctrl+Zで元に戻せる）。プレビューは自動的にはリセット**されない**。

### Prefabからのインポート

既存のPrefab（VariantまたはRegular）からマテリアル設定をCV Creatorに読み込む機能。

**UI**: Base PrefabフィールドのOptionsメニュー (▼) からアクセス。「Import from Prefab」をトグルするとインポートセクションが表示/非表示になる。

```
Base Prefab (Hierarchy Instance)
[ObjectField                       ] [▼]

┌─ Import from Prefab ──────────────────────────────┐
│ "Set a Base Prefab first to enable import."       │  ← Base Prefab未設定時
│ [ObjectField: Prefab asset (D&D対応)] [Apply]     │
└───────────────────────────────────────────────────┘
```

**状態制御**:
- Optionsメニューは常に有効（Base Prefab未設定でも開ける）
- Base Prefab未設定時: ObjectFieldとApplyボタンが非活性、メッセージを表示
- Base Prefab設定後: メッセージが消え、操作可能になる
- Base Prefab変更/クリア時: セクションの表示状態は維持し、活性/非活性だけ切り替え

**Applyの処理**:
1. ソースPrefabを`PrefabScanner.ScanRenderers()`でスキャン
2. `RendererMatcher.CompareRenderers()`で4段階マッチングを使いスロットを照合
3. マテリアルが異なるスロットをオーバーライドとして設定
4. ソースPrefab名からバリアント名を自動推測（ベース名のプレフィックスを除去）
5. Sceneプレビューに反映

### 出力と生成

出力設定を行い、単一のPrefab Variantを生成する。

- **Variant Name**: テキストフィールド（例:「Black」）
- **Output Path**: デフォルトはベースPrefabと同じフォルダー。ProjectウィンドウからフォルダーをD&Dでも指定可能。
- **Naming Template**: `{BaseName}_{VariantName}`（カスタマイズ可能）
- **Output Preview**: 生成前にファイルパスのプレビューを表示

**Generateボタンの動作**:
1. 入力バリデーション（ベースPrefab、バリアント名、不正文字、出力パス）
2. オーバーライドが0件の場合は確認ダイアログ
3. 同名ファイルが存在する場合は上書き確認
4. Prefab Variantを生成
5. 成功時: 生成アセットをPingし、次のアクション選択ダイアログを表示（2ボタン）:
   - **Keep Current Overrides**（OKボタン）— 現在のオーバーライドを維持して一部変更
   - **Clear Overrides**（Cancelボタン）— オーバーライド・バリアント名をクリアし、プレビューをリセットして次の色へ

**生成プロセス**（重要 — プレビューインスタンスは使用しない）:
1. `PrefabUtility.InstantiatePrefab(basePrefabAsset)`でベースPrefabから新規インスタンスを生成
2. エディターで定義したマテリアルオーバーライド**のみ**を適用
3. `PrefabUtility.SaveAsPrefabAsset()`でPrefab Variantとして保存
4. 一時インスタンスを破棄

これにより、プレビュー中にHierarchy上で意図せず変更された他のプロパティ（Transform位置など）がVariantに混入することを防ぐ。

---

## Batch Generator — 詳細仕様

### 入力

入力順序が重要 — 先にベースPrefabを設定する。

1. **ベースPrefab (A)**: Variantの生成先となるPrefab（ObjectField、Projectアセットのみ）
   - 指定時に全Rendererスロットをスキャン

2. **ソースPrefab (B)群**: マテリアルの取得元となるPrefab
   - **Prefab Variantに限定されない** — 任意のPrefabを指定可能
   - **追加方法**:
     - `+ Add Variant`ボタンで空行を追加し、ObjectFieldでPrefabを指定
     - 「Color Variants」セクションにPrefabをD&D（複数同時対応）
     - 既存行のObjectFieldへのD&D
   - Prefab追加時に即座にマッチングを実行

**各行のレイアウト**: `[バリアント名 (Label)] [✎] [ObjectField] [N overrides] [×]`

- **バリアント名の自動導出**:
  - Prefab Variantの場合: `GetCorrespondingObjectFromSource()`で親を取得し、`DeriveVariantName(parent.name, B.name)`
  - 通常のPrefabの場合: Prefab名をそのまま使用
- **✎ボタン**: ラベルを編集可能なTextFieldに切り替え
  - 手動編集後は自動導出を無効化（手動入力を尊重）
  - フォーカスが外れるとラベル表示に戻る
  - カスタム名は出力ファイル名の`{VariantName}`に反映

### マッチングと差分検出

`RendererMatcher.CompareRenderers(sourceSlots, targetSlots)`を使い、各ソースPrefab (B)のマテリアルスロットをベースPrefab (A)と直接比較する。

マッチングアルゴリズムの詳細は [architecture.md — レンダラーマッチングアルゴリズム](architecture.md#レンダラーマッチングアルゴリズム) を参照。

**比較対象**: マッチしたスロットペアについて、マテリアルが異なる場合のみ結果に含める。同じマテリアルのスロットは除外される。

### マッチング結果UI

結果はVariant単位でグルーピングし、サマリーFoldoutとともに表示する。

```
┌─ Matching Results ─────────────────────────────────────────────┐
│                                                                │
│ (結果なし: "No matching results yet..." テキスト)               │
│                                                                │
│ (結果あり: Foldoutが出現、デフォルト折りたたみ)                  │
│ ▶ 3/5 variants fully matched  ← クリックで詳細展開             │
│                                                                │
│   ▼ Red (2/3 matched) ⚠       ← 未マッチあり: 自動展開+警告色 │
│     ⚠ Unmatched (1)                                           │
│       Skirt/0 (Skirt_Red) → [ドロップダウン▼]     [Exclude]   │
│     ▶ Matched (2)              ← 折りたたみサブFoldout          │
│       Hat/0 → Hat/0 [Hat_Red] (P1)                             │
│       Knit/0 → Knit/0 [Knit_Red] (P1)                         │
│                                                                │
│   ▶ Blue (3/3 matched)        ← 全マッチ: デフォルト閉じ       │
│     Hat/0 → Hat/0 [Hat_Blue] (P1)                              │
│     Knit/0 → Knit/0 [Knit_Blue] (P1)                          │
│     Skirt/0 → Skirt/0 [Skirt_Blue] (P2)                       │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**サマリーFoldout**:
- ラベル: `"{fullyMatched}/{total} variants fully matched"`
- 全Variantがマッチ完了していない場合、警告色（オレンジ）

**Variant毎のFoldout**:
- ラベル: `"{variantName} ({matched}/{total} matched)"`
- 未マッチスロットがある場合: 自動展開 + 警告スタイル
- 全マッチの場合: デフォルト折りたたみ

**Foldout内の構造**:
- **未マッチスロットがある場合**:
  - `⚠ Unmatched (N)` ヘッダー + 未マッチ行を直接表示
  - マッチ済み行は折りたたみ可能な `Matched (N)` サブFoldout内に格納
- **全マッチの場合**:
  - マッチ済み行を直接表示（サブFoldoutなし）

**未マッチ行**: ソーススロット情報 + 全ベーススロットから手動割り当て可能なドロップダウン +「Exclude」ボタン（生成対象から除外）

**マッチ済み行**: `{ソースDisplayName} → {ターゲットDisplayName} [{ターゲットベースマテリアル → ソースマテリアル}] (P{優先度})`

### 出力と一括生成

- **Output Path**: デフォルトはベースPrefabと同じフォルダー。フォルダーのD&D対応。
- **Naming Template**: `{BaseName}_{VariantName}`（カスタマイズ可能）
- **同名バリアント自動採番**: バッチ内で同じバリアント名が複数存在する場合、すべての重複に`_1`, `_2`, `_3` …サフィックスを付与。一意な名前はそのまま。
- **「マテリアル差分がないVariantも作成する」トグル**: 有効にすると、マテリアル差分がなくてもPrefab Variantを生成する。ベースPrefabを別フォルダーに分けて管理したい場合に有用。
- **Output Preview**: 全Variantの出力ファイル名を表示（自動採番の結果も反映）
- **Generate All Variantsボタン**: プログレスバー付きで全Variantを一括生成
- **完了サマリーダイアログ**: 成功/失敗件数を表示

---

## 命名テンプレート

両ツールとも、以下のプレースホルダーを使った命名テンプレートに対応:

| プレースホルダー | 説明 |
|---|---|
| `{BaseName}` | ベースPrefabのファイル名（拡張子なし、末尾の`_Base`は自動除去） |
| `{VariantName}` | ユーザーが指定したバリアント名 |

デフォルトテンプレート: `{BaseName}_{VariantName}`

例: ベース `Airi_HonmeiKnit` + Variant `Black` → `Airi_HonmeiKnit_Black.prefab`

---

## バリデーション

### CV Creator

| チェック項目 | レベル | 説明 |
|---|---|---|
| ベースインスタンスが未指定 | Error | スキャン・生成不可 |
| 選択したGameObjectがPrefabインスタンスでない | Error | Prefab Variant生成にはPrefabインスタンスが必要 |
| Rendererが存在しない | Error | マテリアルスロットがない |
| バリアント名が空 | Error | ファイル名を生成できない |
| バリアント名に無効な文字 | Error | パス区切り文字等 |
| 全スロットが「変更なし」 | Warning | ベースと同一のVariantが生成される |
| 出力パスが`Assets/`または`Packages/`以下でない | Error | Unity AssetDatabaseの制約 |
| 出力先に同名ファイルが存在 | Warning | 上書き確認 |

### Batch Generator

| チェック項目 | レベル | 説明 |
|---|---|---|
| ソースPrefabが未指定 | Error | 生成対象がない |
| ベースPrefabが未指定 | Error | 生成先がない |
| マッチング結果がない | Error | マテリアル差分が未検出（「マテリアル差分がないVariantも作成」オプション無効時） |
| 未マッチスロットがある | Warning | 一部のマテリアルが適用されない |
| 出力パスが`Assets/`または`Packages/`以下でない | Error | Unity AssetDatabaseの制約 |

---

## エラーハンドリング

| ケース | 動作 |
|---|---|
| 出力先フォルダーが存在しない | ディレクトリを自動作成 |
| バッチ内で同名バリアント | `_1`, `_2`, `_3` …の自動採番で衝突を回避 |
| 同名ファイルが存在（単一生成） | 確認ダイアログ（上書き / キャンセル） |
| 同名ファイルが存在（一括生成） | 該当ファイル一覧を表示し、確認ダイアログ（すべて上書き / キャンセル） |
| 生成時にRendererパスが見つからない | 警告ログ＋スキップ、結果サマリーに含める |
| 出力パスが`Assets/`・`Packages/`外 | エラーダイアログ表示、生成を中止 |
| 生成中の例外 | 個別にcatchし、サマリーダイアログに表示 |
| プレビュー中にインスタンスが削除された | `_baseInstance`のnullチェックで安全にスキップ |
