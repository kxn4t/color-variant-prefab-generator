# アーキテクチャ

Color Variant Prefab Generatorのコードアーキテクチャ。データモデルやメソッドの詳細はソースコードのXMLコメントを参照。

プロダクトレベルの機能説明とUI仕様は [specification.md](specification.md) を参照。

---

## レイヤー概要

```
UI層 (CreatorWindow, BatchGeneratorWindow)
  ↓
Core層 (PrefabScanner, RendererMatcher, PrefabVariantGenerator, PrefabModificationHelper, VariantAnalyzer)
  ↓
Localization層 (Localization.cs — #if CVG_HAS_NDMF で条件分岐)
```

`PrefabModificationHelper`はStandardモードで構造変更の解析と、Hierarchyインスタンスから新規Variantインスタンスへの修正転写を担当する。

※ `PrefabVariantGenerator`（Core）が`EditorUIUtility`（UI）の純粋ユーティリティ関数（`IsValidOutputPath`, `ResolveFileName`, `NormalizePath`）を使用している。これらはUI非依存の関数だが、コード配置上はUI層に属する。

---

## レンダラーマッチングアルゴリズム

`RendererMatcher.TryMatch()`が実装する5段階の優先度システム。P1-P4では`slotIndex` + `rendererType`がハードフィルターとして適用される。P5は同じ型を優先しつつ、見つからない場合は型違いにもフォールバックする（cross-type fallback）:

| 優先度 | 条件 | 説明 |
|---|---|---|
| **P1** | `rendererPath`完全一致 + `objectName`一致 | 完全パス一致 — 同じ位置のRenderer |
| **P2** | `hierarchyDepth`一致 + `objectName`一致 | 同じ深さ・同名 — 親パスが異なる場合もPathSegmentScoreでランキング |
| **P3** | `objectName`一致（深さ不問） | 名前のみで照合、もっとも近い深さを優先 |
| **P4** | `objectName`大文字小文字無視一致 | 大文字小文字を無視したフォールバック |
| **P5** | 類似名 | `NormalizeName`一致または共通ベース名のトークン一致。名前スコア + 階層パス類似度の複合スコアでランキング |

各段階は順番に試行される。P1でマッチが見つかればP2–P5はスキップ。

`NormalizeName`は末尾の構造的サフィックス（数字`_01`、括弧`(1)`、Blender`.001`、バージョン`_v2`、コピー`_copy`）を繰り返し除去する。色名や方向は除去しない。

`HasCommonBaseName`はトークン分割（セパレーター: `_`, `-`, `.`, 空白）の先頭トークン一致のみで判定する。P5の適格性判定では先頭トークン3文字以上を要求し、PathSegmentScore内のランキングでは1文字以上で使用する。

### タイブレーク

**P1–P4** (`SelectBestCandidate`): 同一優先度で候補が複数ある場合の絞り込み:

1. **ベースマテリアル名の一致** — ソースのベースマテリアル名と一致する候補を優先
2. **階層パスの類似度**（`PathSegmentScore`）— 末端側から親オブジェクトを比較、完全一致（100）> NormalizeName一致（75）> 共通ベース名（50）
3. **階層深度の近さ** — 深度差がもっとも小さい候補を優先
4. **レーベンシュタイン距離** — パス全体の編集距離が近い候補を優先

**P5** (`SelectBestFuzzyCandidate`): 名前スコア（NormalizeName一致=100, 共通ベース名=60）+ 階層パス類似度の合計で降順ソート。タイブレークはP1–P4と同じ順序。

マッチ済みターゲットキーの`HashSet`により、重複マッチを防止する。

設計方針、代表的なテストケース、ツール間差分、既知の制限については [matching-algorithm-design.md](matching-algorithm-design.md) を参照。

---

## データフロー

### CV Creator（Strictモード）

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

### CV Creator（Standardモード）

```
Hierarchyインスタンス
    │
    ├─→ PrefabScanner.ScanRenderers()   → 追加オブジェクト配下のRendererもスロットUIに含める
    │
    └─→ PrefabModificationHelper.AnalyzeStructuralChanges()   → StructuralChangeSummary
    │
    ▼
ユーザーがマテリアルを差し替え ＋ Hierarchyで構造変更（追加/削除/リネーム/アクティブ切替）
    │
    ▼
「Generate」ボタン
    │
    ▼
PrefabVariantGenerator.GenerateStandardVariant(StandardGenerationRequest)
    │
    ├─ includePropertyChanges = false  →  GenerateStandardVariantFiltered()
    │     ├─ Step 1: ApplyRemovedGameObjects()   削除の転写
    │     ├─ Step 2: ApplyModifications()        GameObject自体のプロパティ変更を転写
    │     │                                       （rename / active / tag / layer /
    │     │                                        staticFlags 等。Transform・Component
    │     │                                        プロパティ変更は除外）
    │     ├─ Step 3: CopyAddedGameObjects()      追加の転写
    │     └─ Step 4: マテリアルオーバーライド適用
    │
    └─ includePropertyChanges = true   →  GenerateStandardVariantNative()
          ├─ Step 1: Object.Instantiate(hierarchyInstance) で複製
          │            → 元のシーンインスタンスのPrefab接続を保護
          ├─ Step 2: PrefabUtility.SaveAsPrefabAsset(duplicate, path)
          │            → Unityが認識するoverride全部を保存
          ├─ Step 3: PrefabUtility.LoadPrefabContents(path)
          ├─ Step 4: マテリアルオーバーライド適用
          ├─ Step 5: PrefabUtility.SaveAsPrefabAsset(contents, path)
          ├─ Step 6: PrefabUtility.UnloadPrefabContents(contents)
          └─ Step 7: Object.DestroyImmediate(duplicate)
    │
    ▼
Prefab Variantファイル (.prefab)
```

Filteredパスのstep順序には意味がある: `ApplyModifications`は`SetPropertyModifications`で修正リストを置換するため、先に`CopyAddedGameObjects`を行うと追加オブジェクトが破棄される。そのため削除 → プロパティ → 追加 → マテリアルの順で処理する。Nativeパスは必ずHierarchyインスタンスを `Object.Instantiate` で複製してから保存する: `PrefabUtility.SaveAsPrefabAsset` は引数のGameObjectのPrefab接続を新アセット側へ張り替える副作用があり、シーン上のユーザー操作対象を直接渡すと元のbase Prefabへの接続が失われるため。

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

## 設計上の注意点

- **Prefab Variant生成時、プレビューインスタンスは使用しない**: 常に`PrefabUtility.InstantiatePrefab()`で新規インスタンスを生成し、構造変更（Standardモードのみ）とマテリアルオーバーライドを適用して保存する。プレビュー中にHierarchy上で意図せず変更された他のプロパティ（Transform位置など）がVariantに混入することを防ぐため。Standardモードでも構造変更は`PrefabModificationHelper`経由で選択的に転写し、プレビューインスタンス自体は保存しない。
- **Standard / Strictモード**: `CreatorMode`列挙型で管理。`EditorPrefs`に永続化。Strictモードは従来の挙動（マテリアルオーバーライドのみ）を維持するための退避ルート。Standardモードは`StandardModeOptions.includePropertyChanges`で2経路に分岐する:
  - **OFF（デフォルト）**: ベースPrefabを新規インスタンス化し、`PrefabModificationHelper`経由で「GameObjectの追加・削除」と「既存GameObject自体のプロパティ変更（`m_Name` / `m_IsActive` / `m_Layer` / `m_TagString` / `m_StaticEditorFlags` 等）」を選択的に転写する影響範囲の限定されたパス。Transform・Componentのプロパティ変更とコンポーネントの追加・削除は意図的に除外する
  - **ON**: Hierarchyインスタンスを `Object.Instantiate` で複製してから `PrefabUtility.SaveAsPrefabAsset` で保存し、Unityが認識するoverride全部（Transform・コンポーネントのプロパティ・コンポーネント追加削除など）を取り込む。マテリアルoverrideは `PrefabUtility.LoadPrefabContents` で再オープンして上から適用する。複製ステップは必須で、Hierarchyインスタンスを直接渡すと元のbase Prefabへの接続が新Variantへ張り替えられてしまうのを防ぐ
- **`ApplyModifications` はベースPrefab経由でマッピング**: `PropertyModification.target` はシーン上のインスタンスではなく *ベースPrefabアセット側のオブジェクト* を指す。そのため `BuildBaseAssetToTargetMapping` で `GetCorrespondingObjectFromSource` を辿って「ベースアセット → 新規targetインスタンス」の対応表を作り、mod.targetをtargetインスタンス側のオブジェクトにremapする。`CopyAddedGameObjects` の `FindCorrespondingTransform` も同じ対応方式を使い、リネーム済み祖先の下に追加されたGameObjectでも親を正しく見つけられる。
- **override値は`SerializedObject`で直接書く**: `SetPropertyModifications` はoverrideリストを更新するだけでGameObjectの実値（`.name` / `.activeSelf` / `.tag`）は変更しない。後続の `SaveAsPrefabAsset` はベースとの差分からoverrideを再計算するため、実値がベースのままだと空のVariantになる。このため `ApplyModifications` は `SerializedObject.ApplyModifiedPropertiesWithoutUndo` で値自体を書き込み、`m_IsActive` については `GameObject.SetActive` も併用してランタイム状態を同期する。
- **`ValueDiffersFromBase` は `mod.value` 文字列とベースを比較**: `PropertyModification.target` はベースアセット側を指すので、`SerializedObject(mod.target)` を読んでもユーザーのoverride値は得られない。ユーザーの値は `mod.value` 文字列にしか存在しないため、ベースアセットの現在値と文字列を型ごとに比較して「リバート済みのno-op override」を除外する。
- **`CompareRenderers`は差分のみ返す**: マテリアルが同じスロットは結果に含まれない。未マッチのソーススロットは`targetSlot = null`で含まれる（ただしベースマテリアルが`null`のスロットは除外）。
- **`MatchRenderers()`と`AnalyzeVariant()`**: 公開APIとして存在するが、組み込みUIからは`CompareRenderers`と直接比較方式に移行済み。
