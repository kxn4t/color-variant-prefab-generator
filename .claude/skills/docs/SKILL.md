---
name: docs
description: Update documentation (architecture.md, specification.md, CHANGELOG.md, README) to match code changes. Use after implementing features, fixes, or refactors.
---

# Documentation Update Skill

コード変更に対応するドキュメントを更新する。

## 手順

1. **変更内容の把握**: `$ARGUMENTS` が指定されていればその内容を確認する。指定がなければ `git diff --staged` および `git diff` で変更されたファイルを特定し、変更内容を要約する

2. **対象ドキュメントの特定**: 変更内容に応じて更新が必要なドキュメントを判断する
   - `docs/architecture.md` — クラス構成、データモデル、アルゴリズム、レイヤー依存関係の変更
   - `docs/specification.md` — UI仕様、ユーザー向け機能、バリデーションルール、ワークフローの変更
   - `CHANGELOG.md` — ユーザーに影響する変更（機能追加、バグ修正、破壊的変更）
   - `README.md` / `README.ja.md` — インストール方法、使い方、機能概要の変更

3. **更新計画の提示**: 更新するドキュメントとセクションの一覧をユーザーに提示し、承認を得てから編集を開始する

4. **ドキュメントの更新**: 承認後、対象セクションのみを更新する

## ルール

- 変更箇所に直接関係するセクション**のみ**を更新する。関係のないセクションは触らない
- 新しいセクションやスコープを勝手に追加しない
- 英語技術用語（Renderer, Prefab, Variant等）は無理にカタカナに変換せず英語のまま記載する
- CHANGELOGはバイリンガル（英語 + 日本語）で記載する
- 既存の文体・フォーマットに合わせる
