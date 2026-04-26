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
    ▼
GenerateStandardVariantInternal()
    ├─ Step 1: DuplicateInstancePreservingPrefabConnection()
    │            → Unsupported.DuplicateGameObjectsUsingPasteboard()
    │            → Ctrl+D と同じ経路でPrefab接続を維持したまま複製
    │              （Object.InstantiateだとPrefab接続が切れ、保存結果が
    │               Variantではなく通常のPrefabになるため不可。nested Prefab
    │               配下の構造override（追加・削除component / 追加・削除child）も
    │               この複製で再帰的に保持される）
    ├─ Step 2: includePropertyChanges == false のみ
    │            → PrefabModificationHelper.RevertNonStructuralOverrides(duplicate)
    │              ├─ RevertAddedComponent      既存GO上の追加componentを除去
    │              ├─ RemovedComponent.Revert   既存GOから削除されたcomponentを再追加
    │              ├─ RevertObjectOverride      既存GO上のTransform/Componentの
    │                                            プロパティoverrideを一掃
    │              └─ no-op GameObject mod の trim
    │                                            既存GO上の「変更→ベース値に戻し」で残る
    │                                            no-op PropertyModificationを除去
    │              ※追加subtreeはRevert対象外。GameObject自体のoverride
    │               （m_Name / m_IsActive / m_TagString / m_Layer /
    │                m_StaticEditorFlags 等）はベース値と異なる場合のみ保持される
    ├─ Step 3: PrefabUtility.SaveAsPrefabAsset(duplicate, path)
    │            → Unityが認識するoverride（Step 2でRevertされなかった分）を保存
    ├─ Step 4: PrefabUtility.LoadPrefabContents(path)
    ├─ Step 5: マテリアルオーバーライド適用（contents上）
    ├─ Step 6: PrefabUtility.SaveAsPrefabAsset(contents, path)
    ├─ Step 7: PrefabUtility.UnloadPrefabContents(contents)
    └─ Step 8: Object.DestroyImmediate(duplicate)
    │
    ▼
Prefab Variantファイル (.prefab)
```

Standardモードは常にHierarchyインスタンスを複製してから保存する。`PrefabUtility.SaveAsPrefabAsset` は引数のGameObjectのPrefab接続を新アセット側へ張り替える副作用があり、シーン上のユーザー操作対象を直接渡すと元のbase Prefabへの接続が失われるため。複製には`Unsupported.DuplicateGameObjectsUsingPasteboard()`を使う — `Object.Instantiate`はPrefabインスタンス上でPrefab接続を失うため、保存結果がVariantではなく通常のPrefabになってしまう。この関数はCtrl+Dと同じEditor内部経路でPrefab接続を保ったまま複製し、nested Prefab内部の構造overrideまで再帰的に維持する。

`includePropertyChanges = false` のときの「Transform/Componentプロパティ変更とcomponent追加・削除を除外する」挙動は、複製後にcomponent単位で `PrefabUtility.RevertObjectOverride` を呼ぶことで実現する。GameObject単位のRevertは使わない（`m_Name` / `m_IsActive` / `m_TagString` / `m_Layer` / `m_StaticEditorFlags` 等を一括で消してしまうため）。`m_Materials` overrideもこのRevertでいったん消えるが、後続のStep 5でユーザー指定のマテリアルoverrideがcontents経由で書き直されるので最終`.prefab`には反映される。

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

- **Prefab Variant生成時、プレビューインスタンスは使用しない**: StrictモードとCV Creator/Batch Generatorのマテリアルのみ生成では `PrefabUtility.InstantiatePrefab()` で新規インスタンスを生成してマテリアルオーバーライドを適用して保存する。プレビュー中にHierarchy上で意図せず変更された他のプロパティ（Transform位置など）がVariantに混入することを防ぐため。Standardモードでは構造変更とプロパティoverrideをHierarchyインスタンスから取り込む必要があるため、`Unsupported.DuplicateGameObjectsUsingPasteboard()` で複製した上で必要に応じてoverrideをRevertする方式を採用する（プレビューインスタンス自体を `SaveAsPrefabAsset` に渡さない点は同じ）。
- **Standard / Strictモード**: `CreatorMode`列挙型で管理。`EditorPrefs`に永続化。Strictモードは従来の挙動（マテリアルオーバーライドのみ）を維持するための退避ルート。Standardモードは常にHierarchyインスタンスを複製した上で `StandardModeOptions.includePropertyChanges` に応じてRevertの有無が変わる:
  - **OFF（デフォルト）**: 複製後に `PrefabModificationHelper.RevertNonStructuralOverrides` で「既存GameObject上のTransform/Componentプロパティoverride」「既存GameObject上の追加・削除component」をRevert。「GameObjectの追加・削除」「既存GameObject自体のプロパティ変更（`m_Name` / `m_IsActive` / `m_Layer` / `m_TagString` / `m_StaticEditorFlags` 等）」「追加subtree内部の構造override（nested Prefab配下の追加・削除component / 追加・削除childを含む）」は保持される
  - **ON**: 複製後にRevertを行わず、Unityが認識するoverride全部（Transform・コンポーネントのプロパティ・コンポーネント追加削除など）をそのまま保存する
  - 複製には `Unsupported.DuplicateGameObjectsUsingPasteboard()` を使う（Ctrl+Dと同じ内部経路）。`Object.Instantiate` はPrefabインスタンスに対してPrefab接続を失うため、保存結果がVariantではなく通常のPrefabになってしまい不可。Hierarchyインスタンスを直接 `SaveAsPrefabAsset` に渡すと元のbase Prefabへの接続が新Variantへ張り替えられてしまうため、複製ステップは必須。マテリアルoverrideは `PrefabUtility.LoadPrefabContents` で再オープンして上から適用する
- **GameObject単位のRevertは使わない**: `RevertNonStructuralOverrides` のStep 3（既存オブジェクト上のプロパティoverrideのRevert）はcomponent単位で `PrefabUtility.RevertObjectOverride` を呼ぶ。GameObject単位で呼ぶと `m_Name` / `m_IsActive` / `m_TagString` / `m_Layer` / `m_StaticEditorFlags` といった保持したいoverrideまで一括で消えてしまうため。component単位のRevertは `m_Materials` も含めてそのcomponentに乗る全プロパティを一掃するが、後続のマテリアルoverride適用フェーズでcontents上で書き直されるので最終的な `.prefab` には影響しない。
- **追加subtreeはRevert対象外**: `BuildAddedInstanceIds` で `PrefabUtility.GetAddedGameObjects` 配下のinstance IDを集め、`IsAddedObject` で「追加subtreeに属するか」を判定して、追加subtree配下のcomponentは `RevertObjectOverride` / `RevertAddedComponent` の対象から除外する。これによりnested Prefabの内部構造を含む追加subtreeはoverrideがそのまま保存される。
- **`ValueDiffersFromBase` は `mod.value` 文字列とベースを比較**: `PropertyModification.target` はベースアセット側を指すので、`SerializedObject(mod.target)` を読んでもユーザーのoverride値は得られない。ユーザーの値は `mod.value` 文字列にしか存在しないため、ベースアセットの現在値と文字列を型ごとに比較して「リバート済みのno-op override」を `AnalyzeStructuralChanges` のサマリから除外する。
- **`CompareRenderers`は差分のみ返す**: マテリアルが同じスロットは結果に含まれない。未マッチのソーススロットは`targetSlot = null`で含まれる（ただしベースマテリアルが`null`のスロットは除外）。
- **`MatchRenderers()`と`AnalyzeVariant()`**: 公開APIとして存在するが、組み込みUIからは`CompareRenderers`と直接比較方式に移行済み。
