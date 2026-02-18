# Changelog / 更新履歴

🇬🇧 **English** | 🇯🇵 **日本語**

All notable changes to this project will be documented in this file.
このプロジェクトの注目すべき変更はすべてこのファイルに記録されます。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
フォーマットは [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased]

### Added

- **CV Creator: Bulk Material Replace** — "Bulk" mode toggle that groups slots by material, letting you replace all slots sharing the same material at once via drag-and-drop. Supports per-group and per-slot clear with single-step Undo
- **Collapsible Renderer Headers** — Renderer groups in normal mode can now be collapsed and expanded to reduce clutter
- **Alt+Click Fold All** — Alt+clicking a collapsible arrow expands or collapses all groups at once (works in both normal and bulk modes)

### Changed

- **CV Creator: Improved Material Highlight** — Increased material highlight duration from 2 seconds to 5 seconds. Clicking a slot row now also highlights the row itself with a blue left border, making it easy to see which slot you selected. Consecutive clicks properly clear the previous highlight before showing the new one

### 追加

- **CV Creator: 一括マテリアル置き換え** — マテリアルごとにスロットをグルーピングする「一括」モードを追加。同じマテリアルを使う全スロットをドラッグ＆ドロップでまとめて置き換え可能。グループ・個別のクリアにも対応し、1回のUndoで元に戻せる
- **Rendererヘッダーの折りたたみ** — 通常モードのRendererグループを折りたたみ・展開して見た目をすっきりに
- **Alt+クリックで全折りたたみ** — 折りたたみ矢印をAlt+クリックすると、全グループを一括展開・折りたたみ（通常モード・一括モード両対応）

### 変更

- **CV Creator: マテリアルハイライトの改善** — マテリアルのハイライト表示時間を2秒から5秒に延長。スロット行をクリックした際にその行自体も青い左ボーダーでハイライト表示されるようになり、どのスロットを選択したか一目でわかるように。連続クリック時は前のハイライトが即座にクリアされる

## [0.1.0] - 2026-02-17

Initial release.

### Added

- **CV Creator** — Create color variants one at a time with real-time Scene preview and full Undo/Redo support
- **Batch Generator** — Generate Prefab Variants in bulk from existing color prefabs. Material differences are detected automatically
- **Material Browser** — Browse materials in a grid or list view, and assign them to slots via drag-and-drop
- **Smart Renderer Matching** — Automatically maps material slots between source and base prefabs using a 4-tier matching algorithm
- **Naming Template** — Customize output file names with `{BaseName}` and `{VariantName}` placeholders. Trailing `_Base` is auto-stripped
- **Localization** — English and Japanese UI (Japanese requires NDMF 1.11.0+)

---

初回リリース。

### 追加

- **CV Creator** — リアルタイムのシーンプレビューとUndo/Redoに対応し、カラーバリアントを1つずつ作成
- **Batch Generator** — 既存のカラーPrefabからPrefab Variantを一括生成。マテリアルの差分は自動検出
- **マテリアルブラウザー** — グリッド・リスト表示でマテリアルを閲覧し、ドラッグ＆ドロップでスロットに割り当て
- **スマートマッチング** — 4段階のマッチングアルゴリズムでソースとベースPrefab間のマテリアルスロットを自動対応付け
- **命名テンプレート** — `{BaseName}`・`{VariantName}` で出力ファイル名をカスタマイズ。末尾の `_Base` は自動除去
- **ローカライズ** — 英語・日本語UIに対応（日本語はNDMF 1.11.0以上が必要）
