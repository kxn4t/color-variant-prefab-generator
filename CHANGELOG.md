# Changelog / 更新履歴

🇬🇧 **English** | 🇯🇵 **日本語**

All notable changes to this project will be documented in this file.  
このプロジェクトの注目すべき変更はすべてこのファイルに記録されます。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).  
フォーマットは [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased]

Initial release. / 初回リリース。

### Added / 追加

- **CV Creator** — Create color variants one at a time with real-time Scene preview and Undo/Redo support  
  **CV Creator** — リアルタイムシーンプレビューとUndo/Redo対応でカラーバリアントを1つずつ作成
- **Batch Generator** — Bulk-generate Prefab Variants from existing color prefabs with automatic material difference detection  
  **Batch Generator** — 既存カラーPrefabからマテリアル差分自動検出付きでPrefab Variantを一括生成
- **Material Browser** — Grid/list view panel for browsing and drag-and-drop assigning materials  
  **Material Browser** — マテリアルの閲覧とドラッグ&ドロップ割り当てが可能なグリッド/リスト表示パネル
- **Smart Renderer Matching** — 4-tier matching algorithm to map material slots between source and base prefabs  
  **Smart Renderer Matching** — ソースとベースPrefab間のマテリアルスロットを対応付ける4段階マッチングアルゴリズム
- **Naming Template** — Customizable output file naming with `{BaseName}` and `{VariantName}` placeholders; trailing `_Base` is auto-stripped  
  **Naming Template** — `{BaseName}`・`{VariantName}` プレースホルダーによるカスタマイズ可能な出力命名; 末尾の `_Base` は自動除去
- **Localization** — English and Japanese UI (Japanese requires NDMF 1.11.0+)  
  **ローカライズ** — 英語・日本語UI対応（日本語はNDMF 1.11.0以上が必要）