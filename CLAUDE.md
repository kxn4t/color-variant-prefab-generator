# CLAUDE.md

このファイルはClaude Code (claude.ai/code) がこのリポジトリで作業する際のガイダンスを提供します。

## 言語ルール

- **コーディング**: 英語で行う（コード、コメント、変数名、XMLドキュメント等すべて英語）
- **作業・会話**: ユーザーとのやり取りや作業説明は日本語で行う
- **コミットメッセージ**: 英語でConventional Commits形式に従う（例: `feat:`, `fix:`, `refactor:`）

## プロジェクト概要

**Color Variant Prefab Generator** — VRChat衣装のカラーバリエーション用Prefab Variantをマテリアル差し替えで一括生成するUnity Editorパッケージ（`net.kanameliser.color-variant-generator`）。Editor専用、ランタイムコードなし。

- **Unity**: 2022.3以上
- **言語**: C#
- **UIフレームワーク**: UIToolkit（VisualElement + USS）
- **名前空間**: `Kanameliser.ColorVariantGenerator`
- **アセンブリ**: `Kanameliser.ColorVariantGenerator.Editor`（Editorプラットフォーム限定）
- **オプション依存**: NDMF 1.11.0+（日本語ローカライズ用、`CVG_HAS_NDMF`コンパイルシンボルで検出）

## ビルド・開発

外部ビルドシステムなし — Unity Editorで直接開発する。

- **メニューパス**: `Tools/Color Variant Prefab Generator/Creator` および `Batch Generator`
- **自動テストなし** — Editor UIを通じた手動検証
- **ログ接頭辞**: `[Color Variant Generator]`

## アーキテクチャ

3層構成、依存方向は一方向:

```
UI層 (CreatorWindow, BatchGeneratorWindow)
  ↓
Core層 (PrefabScanner, RendererMatcher, PrefabVariantGenerator, VariantAnalyzer)
  ↓
Localization層 (Localization.cs — #if CVG_HAS_NDMF で条件分岐)
```

### Core層 (`Editor/Core/`)

すべてstatic class、UI非依存:

- **ColorVariantData.cs** — 共通データモデル（`MaterialSlotIdentifier`, `MaterialOverride`, `ScannedMaterialSlot`, `RendererMatchResult`, `GenerationResult`等）
- **PrefabScanner** — Prefab/Hierarchyインスタンスから全Rendererとマテリアルスロットをスキャン
- **RendererMatcher** — 4段階優先度マッチングアルゴリズム（P1: 完全パス一致、P2: 同深度+同名、P3: 同名のみ、P4: 大文字小文字無視）。タイブレーク: マテリアル名一致 → 深度近接 → レーベンシュタイン距離
- **PrefabVariantGenerator** — マテリアルのみのオーバーライドでPrefab Variantを生成。常に新規インスタンスから生成（プレビューからは生成しない）
- **VariantAnalyzer** — 既存Prefab Variantを解析し、ベース名をプレフィックスとして除去してバリアント名を導出

### UI層 (`Editor/UI/`)

両ウィンドウとも**partial class**でUIセクションごとに分割:

- **CreatorWindow** — 5ファイル: メインライフサイクル、BasePrefab、MaterialSlots、Preview、Output
- **BatchGeneratorWindow** — 4ファイル: メインライフサイクル、VariantList、Matching、Output
- **MaterialBrowserPanel** — グリッド/リスト切替のマテリアルブラウザー（非同期サムネイル読み込み）
- **EditorUIUtility** — 共有定数・ユーティリティ（命名テンプレート、重複名解決、パスユーティリティ等）

### Localization層 (`Editor/Localization/`)

- `Localization.cs`が全NDMF呼び出しをラップ — UIコードは`#if CVG_HAS_NDMF`を直接使わない
- 翻訳ファイル: GNU gettext `.po`形式（`en-us.po`, `ja-jp.po`）
- NDMFなし: `LocalizationAsset`リフレクションによる英語のみフォールバック

## コーディング規約

- public/internal APIにはXMLドキュメントコメント
- エラーハンドリング: try-catch + `Debug.LogError` / `Debug.LogWarning`
- Undo対応: ユーザー操作は`Undo.RecordObject` / `Undo.SetCurrentGroupName`で対応
- アセットパスはフォワードスラッシュに正規化
- USSスタイルシートは対応するウィンドウファイルと同ディレクトリに配置
- ドキュメント・CHANGELOGはバイリンガル（英語 + 日本語）
- 日本語テキストでは英語技術用語（Renderer, Variant, Prefab等）はそのまま英語で記載し、無理にカタカナに変換しない
- asmdefの`versionDefines`ではNuGetスタイルの範囲記法を使用する（例: `[1.11.0,2.0.0)`）。比較演算子（`>=1.11.0`）は不可

## 作業ルール

- ファイルのレビューや編集時は、最初に見つけた1ファイルで終わらず関連ファイルをすべて確認する。とくにUSS、`.po`、ローカライズ関連は全ファイルをチェックする
- UI変更時は、既存のレイアウトパターンやスタイル規約を先に読み取ってから実装する。新しいUI要素は既存パターンに合わせる
- ドキュメント更新時は変更箇所に直接関係するセクションのみ更新する。スコープを勝手に広げない
- UIコンテナーに要素を追加する際は、`ElementAt()` / `Children`等のインデックスベースの参照が壊れないか確認する

## 主要ドキュメント

- [docs/architecture.md](docs/architecture.md) — コードアーキテクチャ詳細
- [docs/specification.md](docs/specification.md) — プロダクトレベルの機能仕様・UIワークフロー
