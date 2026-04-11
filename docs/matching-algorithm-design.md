# マッチングアルゴリズム設計

## 概要

Color Variant Generator - RendererMatcher、Editor-Plus - ObjectMatcher（MA Material Helper）/ MaterialCopierは共通のマッチング問題を扱う: ソースのオブジェクト/スロットに対応する正しいターゲットを、命名や構造が異なる可能性のあるアバター階層から見つける。

基本原則は **完全一致を優先し、類似名は最終手段**。まず厳しい順に完全一致（P1-P4）を試行し、すべて失敗した場合にのみスコアベースの類似名マッチング（P5）にフォールバックする。

## 設計方針

1. **偽陽性は偽陰性より重い。** 偽陽性は間違ったマテリアルを無言で適用する。偽陰性はUIに表示され手動で修正できる。アルゴリズムはデフォルトで保守的に動作する。
2. **辞書やホワイトリストを使わない。** 色名リストや意味的ブロックリストに依存しない。類似性の判定には構造的パターン（数字、バージョン、セパレーター）のみを使用する。
3. **完全一致ティアはシンプルに保つ。** P1-P4はパス・深度・名前の単純な条件を使い、動作が予測可能で理解しやすいようにする。
4. **類似名マッチングは最終手段。** P5は完全一致ティアがすべて失敗した後にのみ実行されるため、ある程度の許容が認められる。UIではP5の結果を低信頼度として表示する。
5. **バイナリ判定よりスコアリング。** プレフィックスベースの適格判定ゲートではなく、スコアリングとタイブレークで候補をランキングする。

## ティア構造

すべてのティアで`slotIndex`の一致（RendererMatcher）またはRendererコンポーネントの存在（ObjectMatcher）が必要。P1-P4では`rendererType`の一致も必要。

```text
P1: 完全パス + 完全名                                     [信頼度: 確定]
P2: 同一深度 + 完全名                                     [信頼度: 中]
P3: 完全名（深度不問）                                    [信頼度: 中]
P4: 大文字小文字無視                                      [信頼度: 中]
P5: 類似名（スコアリング）                                [信頼度: 低]
P5 cross-type: 類似名（スコアリング、rendererType緩和）   [信頼度: 低]
```

P5 cross-typeは通常のP5で候補が見つからない場合にのみ実行される。同じメッシュがソースとターゲットで異なるRenderer型を使っているケース（例: PC版のMeshRendererとQuest版のSkinnedMeshRenderer）をカバーする。

ティアは順番に試行される。P1でマッチが見つかればP2-P5はスキップ。

## タイブレーク

同一ティアのフィルターを通過する候補が複数ある場合、以下の順序でランキングして最良の1つを選ぶ。

### P1-P4

1. ベースマテリアル名の一致（RendererMatcherのみ — ソースと同名のマテリアルを持つ候補を優先）
2. `PathSegmentScore` 降順（ObjectMatcher/MaterialCopierでは`AncestorContextScore`を加算）
3. 階層深度の近さ　昇順
4. レーベンシュタイン距離　昇順

### P5

1. `TotalScore` = NameScore + `PathSegmentScore`（+ `AncestorContextScore`）降順
2. ベースマテリアル名の一致（RendererMatcherのみ）
3. 階層深度の近さ　昇順
4. レーベンシュタイン距離　昇順

NameScoreの値: NormalizeName一致 = 100、HasCommonBaseName一致 = 60。

## `NormalizeName`

末尾の**構造的**サフィックスを繰り返し除去し、文字列が安定するまで続ける。結果が空になる場合は除去しない。色名は意図的に除去しない — 重複・バージョン・コピーを示すパターンのみを対象とする。

### 除去するパターン

| パターン | 例 | 正規表現 |
|---|---|---|
| 数字サフィックス | `_01`, `-2`, ` 03` | `[_\-\s]\d+$` |
| 括弧付き重複番号 | `(1)`, ` (2)` | `\s*\(\d+\)$` |
| Blender重複 | `.001`, `.002` | `\.\d{3}$` |
| バージョン | `_v2`, `-ver3` | `[_\-\s]v(?:er)?\d+$` |
| コピーマーカー | `_copy`, `_variant` | `[_\-\s](?:copy\|variant)$` |

すべてのパターン除去後、末尾のセパレーター（`_`, `-`, `.`, 空白）をトリム。結果が空になる場合はスキップ。

各パスで5パターンを順番に試し、最初にマッチしたものを除去して先頭からリスタート。どのパターンもマッチしなくなるまで繰り返す。

### 除去しないもの

色名、方向サフィックス（`L`, `R`, `front`, `back`）、身体部位名、その他の単語は除去**しない**。これらの判定には辞書が必要になるため。

### 例

```text
NormalizeName("Body_01")         → "Body"
NormalizeName("Skirt (2)")       → "Skirt"
NormalizeName("Hair.001")        → "Hair"
NormalizeName("Mesh v2")         → "Mesh"
NormalizeName("Body_01_copy")    → "Body"         (_copy → _01 の2パス)
NormalizeName("Item_01_v2_copy") → "Item"          (_copy → _v2 → _01 の3パス)
NormalizeName("Mesh_(1)")        → "Mesh"          ((1)除去 → "Mesh_" → 末尾"_"トリム)
NormalizeName("Ribbon_blue")     → "Ribbon_blue"   (構造的サフィックスなし)
NormalizeName("Kutsu kuro")      → "Kutsu kuro"    (構造的サフィックスなし)
NormalizeName("_01")             → "_01"            (除去すると空になるため保持)
```

## `HasCommonBaseName`

トークンベースの比較。セパレーター（`_`, `-`, `.`, 空白）で分割し、先頭トークンが完全一致かつ十分な長さを持つ場合にtrueを返す。以下のいずれかの条件を満たす場合にマッチ:

1. 両方が2トークン以上で先頭トークンが一致
2. 片方が1トークン（サフィックスなし）で、もう片方の先頭トークンと一致（例: `Shoes` ↔ `Shoes_red`）

### 二段階の閾値

| コンテキスト | minTokenLength | 目的 |
|---|---|---|
| P5の適格性判定 | 3 | 短いプレフィックス（`L_`, `FX_`）による誤マッチを防止 |
| `PathSegmentScore`内のランキング | 1 | 短いプレフィックスでも弱いランキングヒントとして活用 |

### 例

| ペア | 先頭トークン | 長さ | minToken=3 | minToken=1 |
|---|---|---|---|---|
| `Ribbon_blue` / `Ribbon_red` | `Ribbon` | 6 | true | true |
| `Kutsu kuro` / `Kutsu shiro` | `Kutsu` | 5 | true | true |
| `Hair.001` / `Hair.002` | `Hair` | 4 | true | true |
| `Shoes` / `Shoes_red` | `Shoes` | 5 | true | true |
| `L_Hand` / `L_Shoe` | `L` | 1 | **false** | true |
| `FX_EyeHighlight_L` / `FX_EyeHighlight_R` | `FX` | 2 | **false** | true |
| `L` / `L_Hand` | `L` | 1 | **false** | true |

## `PathSegmentScore`

パスのセグメントを末端側からルート方向に比較し、オブジェクトに近いセグメントほど高い重みを与える。

```text
PathSegmentScore(sourcePath, targetPath):
  sourceSegments = '/' で分割
  targetSegments = '/' で分割
  score = 0, weight = 1.0
  alignCount = min(sourceSegments.length, targetSegments.length)

  for i in 0..alignCount-1:
    s = sourceSegments[末尾 - i]
    t = targetSegments[末尾 - i]

    if s == t:                                    score += 100 × weight
    else if NormalizeName(s) == NormalizeName(t): score += 75 × weight
    else if HasCommonBaseName(s, t, minToken=1):  score += 50 × weight

    weight *= 0.7

  score += -5 × abs(sourceSegments.length - targetSegments.length)
  return score
```

たとえば `Outfit/Jacket/Mesh` と `Clothing/Jacket/Mesh` の比較では、共有セグメント `Jacket/Mesh` が高スコアになり、ルート側の親オブジェクトが異なっていても正しくマッチできる。

## `AncestorContextScore`（ObjectMatcher / MaterialCopierのみ）

ソースのルートオブジェクト名がターゲットパスに含まれる場合にボーナスを与える。複数ルートのコピー&ペースト（例: `Outfit_A`と`Outfit_B`の両方をコピーし、両方をサブ階層として持つアバターにペースト）で正しく振り分けるための仕組み。

```text
AncestorContextScore(sourceRootName, targetPath):
  sourceRootName が null/空 → 0
  targetPath.split('/') の各セグメントについて:
    if セグメント == sourceRootName                              → 30
    if HasCommonBaseName(セグメント, sourceRootName, minToken=1) → 20
  return 0
```

RendererMatcherはソースルート名を追跡しないため、この機能を使用しない。

## P5 類似名ティア

P5はP1-P4がすべて失敗した後にのみ実行される。バイナリ判定ではなくスコアリングで候補を選ぶ。

### 適格性

候補は以下のいずれかを満たす場合にP5の対象になる:
- `NormalizeName(ソース名) == NormalizeName(ターゲット名)`（大文字小文字無視）
- `HasCommonBaseName(ソース名, ターゲット名, minTokenLength=3)`

### なぜP5を残すのか

- 完全一致ティアが失敗した後にのみ実行される
- `rendererType`と`slotIndex`で検索空間が制約される
- UIでP5の結果を低信頼度として表示できる
- 同スコアの場合は未マッチにフォールバックできる

P5はゼロリスクである必要はない。一般的な命名パターン（色違い、番号違い）を回収しつつ、偽陽性の影響範囲を抑えられれば十分。

## 代表的なケース

### マッチすべきペア

| ペア | 期待ティア | 理由 |
|---|---|---|
| `Body` / `Body` | P1-P4 | 完全名一致 |
| `Ribbon_blue` / `Ribbon_red` | P5 | 共通ベース名 `Ribbon` |
| `Kutsu kuro` / `Kutsu shiro` | P5 | 共通ベース名 `Kutsu`（空白セパレーター） |
| `Body_01` / `Body_02` | P5 | NormalizeName → 両方 `Body` |
| `Skirt (1)` / `Skirt (2)` | P5 | NormalizeName → 両方 `Skirt` |
| `Hair.001` / `Hair.002` | P5 | NormalizeName → 両方 `Hair` |
| `Mesh_v2` / `Mesh_v3` | P5 | NormalizeName → 両方 `Mesh` |
| `Tops_copy` / `Tops` | P5 | NormalizeName `Tops_copy` → `Tops` |

### 拒否すべきペア

| ペア | 拒否理由 |
|---|---|
| `L_Hand` / `L_Shoe` | 先頭トークン `L`（長さ1）が minToken=3 未満 |
| `FX_EyeHighlight_L` / `FX_EyeHighlight_R` | 先頭トークン `FX`（長さ2）が minToken=3 未満 |
| `GroupA` / `GroupC_blue` | `GroupA` は単一トークン（セパレーターなし）、NormalizeName一致もなし |
| `Body` / `Bone` | 単一トークン同士、異なる名前、構造的一致なし |

### 受容しているリスク

辞書を使わない設計のため、一部のP5マッチはあいまいなまま残る:

| ペア | マッチし得る理由 |
|---|---|
| `Hair_front` / `Hair_back` | 先頭トークン `Hair`（長さ4 ≥ 3）が共通 |
| `Arm_L` / `Arm_R` | 先頭トークン `Arm`（長さ3 ≥ 3）が共通 |

このリスクが許容される理由:
- P5（最終手段）でのみ発生し、正しいターゲットが存在しない場合にのみ起きる
- ターゲットに `Hair_front` と `Hair_back` の両方があれば、P1-P3で正確にマッチしP5には到達しない
- `rendererType`と`slotIndex`でさらに制約される
- UIでP5は低信頼度として表示される

## ツール間の差異

3つのツールは同じティア構造とユーティリティを共有するが、以下の点で異なる:

| 観点 | RendererMatcher | ObjectMatcher | MaterialCopier |
|---|---|---|---|
| マッチ単位 | マテリアルスロット | RendererありのTransform | 保存済みMaterialData |
| マッチ方向 | ソース → ターゲット | ソース → ターゲット | ターゲット → ソース（逆方向） |
| マテリアル名タイブレーク | あり | なし | なし |
| `AncestorContextScore` | なし | あり | あり |
| `RootNameBonus` | なし | なし | あり |
| 重複防止 | `matchedTargetKeys` | `matchedPaths`（オプション） | なし（意図的） |

MaterialCopierの逆方向マッチングは、ターゲットオブジェクトを順に処理し、保存済みソースリストから最適なマッチを検索する。同一ソースが複数ターゲットに適用できる（例: 1つの衣装マテリアルを複数アバターにペースト）ため、ソース側の重複防止は意図的に行わない。

MaterialCopierの`RootNameBonus`（完全一致=30、共通ベース名=20）は、複数のソースルートに同名・同パスのオブジェクトがある場合にルート名で正しく振り分けるための仕組み。

## 既知の制限と今後の課題

### Greedy matching

現在のマッチングはソースを順に処理し、それぞれ利用可能な最良のターゲットに割り当てる。ソースの処理順序が結果に影響し得る。将来的には**ティアごとの二部マッチング**による全体最適な割り当てに改善可能。

### CJK単文字トークン

`HasCommonBaseName`の`minTokenLength=`により、単一/二文字の漢字プレフィックス（例: `靴_黒` / `靴_白` — `靴`は1文字）は適格にならない。ローマ字表記（`Kutsu kuro` / `Kutsu shiro`）は正しく動作する。

### P5のスコアマージンルール

P5で2候補のスコアが近い場合（例: 161 vs 160）、高い方が無条件に選ばれる。最小スコア差ルール（僅差の場合は未マッチにする）は、あいまいなP5マッチが問題になった場合に追加を検討。
