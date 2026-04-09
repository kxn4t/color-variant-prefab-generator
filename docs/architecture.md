# アーキテクチャ

Color Variant Prefab Generatorのコードアーキテクチャ。データモデルやメソッドの詳細はソースコードのXMLコメントを参照。

プロダクトレベルの機能説明とUI仕様は [specification.md](specification.md) を参照。

---

## レイヤー概要

```
UI層 (CreatorWindow, BatchGeneratorWindow)
  ↓
Core層 (PrefabScanner, RendererMatcher, PrefabVariantGenerator, VariantAnalyzer)
  ↓
Localization層 (Localization.cs — #if CVG_HAS_NDMF で条件分岐)
```

※ `PrefabVariantGenerator`（Core）が`EditorUIUtility`（UI）の純粋ユーティリティ関数（`IsValidOutputPath`, `ResolveFileName`, `NormalizePath`）を使用している。これらはUI非依存の関数だが、コード配置上はUI層に属する。

---

## レンダラーマッチングアルゴリズム

`RendererMatcher.TryMatch()`が実装する4段階の優先度システム:

| 優先度 | 条件 | 説明 |
|---|---|---|
| **P1** | `rendererPath`完全一致 + `objectName`一致 + `slotIndex`一致 | 完全パス一致 — 同じ位置のRenderer |
| **P2** | `hierarchyDepth`一致 + `objectName`一致 + `slotIndex`一致 | 同じ深さ・同名 — 親パスが異なる |
| **P3** | `objectName`一致 + `slotIndex`一致（深さ不問） | 名前のみで照合、もっとも近い深さを優先 |
| **P4** | `objectName`大文字小文字無視一致 + `slotIndex`一致 | 大文字小文字を無視したフォールバック |

各段階は順番に試行される。P1でマッチが見つかればP2–P4はスキップ。

### タイブレーク (`SelectBestCandidate`)

同一優先度で候補が複数ある場合の絞り込み:

1. **ベースマテリアル名の一致** — ソースのベースマテリアル名と一致する候補を優先
2. **階層深度の近さ** — 深度差がもっとも小さい候補を優先
3. **パスの類似度** — パス文字列のレーベンシュタイン距離が最小の候補を優先

マッチ済みターゲットキーの`HashSet`により、重複マッチを防止する。

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

## 設計上の注意点

- **Prefab Variant生成時、プレビューインスタンスは使用しない**: 常に`PrefabUtility.InstantiatePrefab()`で新規インスタンスを生成し、マテリアルオーバーライドのみを適用して保存する。プレビュー中にHierarchy上で意図せず変更された他のプロパティ（Transform位置など）がVariantに混入することを防ぐため。
- **`CompareRenderers`は差分のみ返す**: マテリアルが同じスロットは結果に含まれない。未マッチのソーススロットは`targetSlot = null`で含まれる（ただしベースマテリアルが`null`のスロットは除外）。
- **`MatchRenderers()`と`AnalyzeVariant()`**: 公開APIとして存在するが、組み込みUIからは`CompareRenderers`と直接比較方式に移行済み。
