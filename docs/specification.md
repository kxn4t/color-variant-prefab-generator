# 仕様書

Color Variant Prefab Generatorのプロダクト仕様書。UIの構成と期待される動作を記述する。

コードアーキテクチャと実装の詳細は [architecture.md](architecture.md) を参照。

---

## 目次

- [概要](#概要)
- [ワークフロー](#ワークフロー)
- [CV Creator — 詳細仕様](#cv-creator--詳細仕様)
- [Batch Generator — 詳細仕様](#batch-generator--詳細仕様)
- [命名テンプレート](#命名テンプレート)

---

## 概要

カラーバリエーションの **Prefab Variant** 作成を自動化するUnityエディター拡張。色ごとにPrefabを丸ごと複製する代わりに、**マテリアルのオーバーライドのみ**を保持するPrefab Variantを生成する。ベースPrefabへの変更はすべてのVariantに自動反映される。

| ツール | 用途 |
|---|---|
| **CV Creator** | マテリアルブラウザーとSceneプレビューを使い、カラバリを1色ずつ作成 |
| **Batch Generator** | ベースPrefabとソースPrefabのマテリアル差分を検出し、Variantを一括生成 |

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

### 生成モード（Standard / Strict）

CV Creatorは2つの生成モードを持ち、Base Prefabフィールド横のOptionsメニュー (▼) の「Strict Mode (Material Only)」チェックボックスで切り替える。選択は`EditorPrefs`に保存される。デフォルトは **Standard** モード。

| モード | 保存される内容 |
|---|---|
| **Standard**（デフォルト） | マテリアルオーバーライド＋Hierarchyインスタンス上で行ったGameObjectレベルの変更（GameObjectの追加・削除、および既存GameObject自体のプロパティ変更：rename・active state・Tag・Layer・StaticEditorFlags等）。オプションでHierarchyインスタンスをそのままPrefab Variantとして保存し、Unityが認識するoverride（Transform変更、コンポーネントのプロパティ変更、コンポーネントの追加・削除など）をすべて含めて保存できる |
| **Strict** | マテリアルオーバーライドのみ。Hierarchyインスタンス上の構造変更は一切反映されない |

**Standardモード固有のUI**:

- 追加したGameObject配下のRendererは、スロットUIの末尾に追加オブジェクト用の専用セクションとして表示され、ベースPrefabには存在しないスロットにもオーバーライドを割り当てられる
- Outputセクション内に「Include Transform/component changes」チェックボックスが表示され、有効時はHierarchyインスタンスをそのままPrefab Variantとして保存する経路に切り替わる（デフォルトOFF）
- 構造変更のみで生成可能（マテリアルオーバーライド0件でも、構造変更があれば警告ダイアログなしで生成される）

**Strictモード固有のUI**:

- Base Prefabフィールドの右側に「Strict Mode」バッジを表示し、構造変更が無視されることを示す
- Hierarchyインスタンス上で追加したGameObjectはスロットUIからフィルターされ、表示されない

### ベースPrefab指定

**Hierarchyのインスタンス**（Projectアセットではなく）を指定する。Sceneプレビューのためにシーン上のインスタンスが必要。Projectアセットを指定した場合は警告を表示。

**Optionsメニュー (▼)**: ObjectFieldの右に配置。「Import from Prefab」機能と「Strict Mode (Material Only)」トグルへのアクセスを提供。

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

- ルートフォルダー指定（D&D対応）で配下の全マテリアルをサブフォルダーごとにグループ化
- 表示モードスライダー: 左端=リスト、右方向=グリッド（デフォルト48px）
- クリックでInspector選択、ドラッグでD&D開始

### マテリアルスロットUI

**通常モード**と**一括モード**の2つの表示モードを持ち、トグルボタンで切り替え。切り替えてもデータは変更されない。ウィンドウを閉じるとリセット。

両モードとも折りたたみヘッダーを使用。Alt+クリックで全グループ一括展開/折りたたみ。

#### 通常モード

Rendererごとにグループ化して表示（デフォルト）。

```
Material Slots (4 renderers, 12 slots)              [通常 | 一括]
┌────────────────────────────────────────────────────────────────┐
│ ▼ Hat [SkinnedMeshRenderer]     ← クリックでHierarchy選択     │
│   [0] [■サムネ] Hat_Brown  →  [■サムネ] [ObjectField: Hat_Black] [×]│
│   [1] [■サムネ] Metal      →  (変更なし)                      │
│ ▼ Knit [SkinnedMeshRenderer]                                   │
│   [0] [■サムネ] Knit       →  [■サムネ] [ObjectField: Knit_Black][×]│
│ ▶ Skirt [SkinnedMeshRenderer]   ← 折りたたみ中                 │
└────────────────────────────────────────────────────────────────┘
```

- スロット行クリック: ベースマテリアルをInspector選択＋ブラウザーでハイライト（5秒間）＋スロット行も青い左ボーダーでハイライト
- オーバーライドサムネイルクリック: オーバーライドマテリアルをInspector選択（同様にハイライト）
- 行全体がD&Dターゲット

#### 一括モード

実効マテリアル（オーバーライドがあればオーバーライド、なければベース）でグルーピングし、一括でマテリアルを置き換える。

```
Material Slots (5 materials, 12 slots)              [通常 | 一括]
┌────────────────────────────────────────────────────────────────┐
│ ▼ [■] Material_B (5 slots) [3] → [ObjectField] [×]            │ ← [3]はオーバーライド数バッジ
│     Hat / Slot 0         (base: Material_A)              [×]  │ ← オーバーライドあり
│     Body / Slot 2        (base: Material_A)              [×]  │
│     Pants / Slot 1                                            │ ← ベース一致(薄表示)
│                                                               │
│ ▶ [■] Material_C (2 slots)              → [ObjectField] [×]  │ ← opacity:0.5, [×]disabled
│ ▶ [■] (None) (1 slot)                   → [ObjectField] [×]  │
└────────────────────────────────────────────────────────────────┘
```

- オーバーライドが0件のグループ: `opacity: 0.5`、クリアボタン無効化
- ヘッダーにD&D/ObjectField変更: グループ内全スロットにオーバーライド適用（1 Undo）
- ヘッダーの［×］: 全オーバーライド一括クリア（1 Undo）
- 子行の［×］: 個別スロットのオーバーライドのみクリア → ベースマテリアルのグループに移動
- 子行クリック: 対象GameObjectをHierarchy選択
- 子行のベースマテリアル名クリック: ベースマテリアルをInspector選択＋ブラウザーでハイライト

### Sceneプレビュー

マテリアル割り当てのたびに`Renderer.sharedMaterials`を即座に変更し、リアルタイムプレビュー。すべて`Undo.RecordObject`で記録。

**Clear Overridesボタン**（スプリットボタン）— ドロップダウンでRevert Modeを選択:

| モード | 動作 |
|---|---|
| **Visual Only** | マテリアル値を視覚的に復元。Prefabオーバーライドマークは残る。 |
| **Selective Revert** | 本ツールが追加したPrefabオーバーライドのみをRevert。ツール使用前から存在していたオーバーライドは保持。 |
| **Full Revert** | 変更対象の全Rendererのオーバーライドをすべてrevert。 |

選択モードは`EditorPrefs`に保存。ウィンドウを閉じた場合、マテリアル変更はインスタンス上に残る（Ctrl+Zで元に戻せる）。

### Prefabからのインポート

既存Prefabからマテリアル設定をCV Creatorに読み込む機能。Base PrefabフィールドのOptionsメニュー (▼) からアクセス。

```
Base Prefab (Hierarchy Instance)
[ObjectField                       ] [▼]

┌─ Import from Prefab ──────────────────────────────┐
│ "Set a Base Prefab first to enable import."       │  ← Base Prefab未設定時
│ [ObjectField: Prefab asset (D&D対応)] [Apply]     │
└───────────────────────────────────────────────────┘
```

- Base Prefab未設定時: ObjectFieldとApplyボタンが非活性、メッセージを表示
- Base Prefab変更/クリア時: セクション表示状態は維持、活性/非活性だけ切り替え
- Apply: `CompareRenderers()`で5段階マッチング → 差分をオーバーライドに設定 → バリアント名を自動推測

### 出力と生成

- **Variant Parent**: ベースPrefabが多段Variantの場合に表示。祖先チェーンから親を選択可能。祖先以外を選択した場合、中間Variantの差分も含めてオーバーライドが自動再計算。`{BaseName}`は選択した親のPrefab名に追従。
- **Variant Name / Output Path / Naming Template / Output Preview**: 標準の出力設定
- 成功時: 次アクション選択ダイアログ（Keep Current Overrides / Clear Overrides）

---

## Batch Generator — 詳細仕様

### 入力

入力順序が重要 — 先にベースPrefabを設定する。

1. **ベースPrefab (A)**: Projectアセットのみ。指定時にスキャン。
2. **ソースPrefab (B)群**: 任意のPrefab（Prefab Variantに限定されない）
   - 追加方法: `+ Add Variant`ボタン / セクションへのD&D（複数対応）/ 既存行のObjectFieldへのD&D
   - 追加時に即座にマッチング実行

**各行のレイアウト**: `[バリアント名 (Label)] [✎] [ObjectField] [N overrides] [×]`

- バリアント名の自動導出: Variantなら親名から`DeriveVariantName()`、通常PrefabならPrefab名をそのまま使用
- ✎ボタンで手動編集可能。手動編集後は自動導出を無効化。

### マッチング結果UI

```
┌─ Matching Results ─────────────────────────────────────────────┐
│ ▶ 3/5 variants fully matched  ← サマリーFoldout               │
│                                                                │
│   ▼ Red (2/3 matched) ⚠       ← 未マッチあり: 自動展開+警告色 │
│     ⚠ Unmatched (1)                                           │
│       Skirt/0 (Skirt_Red) → [ドロップダウン▼]     [Exclude]   │
│     ▶ Matched (2)                                              │
│       Hat/0 → Hat/0 [Hat_Red] (P1)                             │
│       Knit/0 → Knit/0 [Knit_Red] (P1)                         │
│                                                                │
│   ▶ Blue (3/3 matched)        ← 全マッチ: デフォルト閉じ       │
└────────────────────────────────────────────────────────────────┘
```

- 未マッチスロット: ドロップダウンで手動割り当て +「Exclude」ボタン
- 未マッチありのVariant: 自動展開＋警告スタイル
- 未マッチありの場合、マッチ済み行は折りたたみサブFoldout内に格納

### 出力と一括生成

- **Output Path / Naming Template**: 標準の出力設定
- **同名バリアント自動採番**: バッチ内の重複に`_1`, `_2`, `_3` …サフィックスを付与（一意な名前はそのまま）
- **「マテリアル差分がないVariantも作成する」トグル**: ベースPrefabを別フォルダーに分けて管理したい場合に有用
- プログレスバー付き一括生成 + 完了サマリーダイアログ

---

## 命名テンプレート

| プレースホルダー | 説明 |
|---|---|
| `{BaseName}` | ベースPrefabのファイル名（拡張子なし、末尾の`_Base`は自動除去） |
| `{VariantName}` | ユーザーが指定したバリアント名 |

デフォルト: `{BaseName}_{VariantName}` — 例: `Airi_HonmeiKnit` + `Black` → `Airi_HonmeiKnit_Black.prefab`
