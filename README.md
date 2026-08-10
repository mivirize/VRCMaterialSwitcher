# VRC Material Switcher

VRChat アバターの「衣装の色替えメニュー」を自動で作る Unity エディタ拡張です。

衣装フォルダを指定してスキャンすると、色違いマテリアルを自動でグループ分けし、
Modular Avatar の Expression Menu（ゲーム内メニュー）として一括セットアップします。
アニメーションや FX レイヤーの知識は不要。アバター本体は改変しません（非破壊）。

```
衣装マテリアルのフォルダ          ゲーム内 Expression Menu
├─ Onepiece_Black.mat            衣装カラー
├─ Onepiece_White.mat     →       ├─ Onepiece ▸ Black / White / Red
├─ Onepiece_Red.mat               └─ Ribbon   ▸ Blue / Pink
├─ Ribbon_Blue.mat
└─ Ribbon_Pink.mat
```

## 必要環境

| 要件 | バージョン |
|---|---|
| Unity | 2022.3.x |
| VRChat SDK - Avatars | 3.x |
| [Modular Avatar](https://modular-avatar.nadena.dev/) | 1.9 以降 |

## インストール

1. [Releases](../../releases) から最新の `VRCMaterialSwitcher.unitypackage` をダウンロード
2. Unity プロジェクトへドラッグ＆ドロップでインポート
3. メニューに `Tools > VRC Material Switcher` が追加されます

> v1.1 以前から更新する場合は、先に `Assets/Editor/VRCMaterialSwitcher` フォルダを削除してからインポートしてください。

## 使い方（3 分クイックスタート）

1. アバターと衣装をシーンに配置した状態で `Tools > VRC Material Switcher` を開く
2. **アバター**欄にシーン上のアバターを設定（「シーンからアバターを自動検出」でも可）
3. **スキャンフォルダ**に衣装のマテリアルが入ったフォルダを設定し、**「🔍 スキャン実行」**
4. 検出されたグループを確認する
   - 不要な色はチェックを外す / ★ でデフォルトの色を選ぶ（初期値はアバターが今着ている色）
   - グループ分けが意図と違う場合は手動で直せます（[マニュアル](MANUAL.md#5-マテリアルグループの編集)）
5. **「✓ セットアップ実行」**
6. アバターをアップロード → ゲーム内の Expression Menu に「衣装カラー」が追加されています

うまく検出されない場合・手動でセットアップしたい場合は **[MANUAL.md](MANUAL.md)** を参照してください。

## 主な機能

- **自動スキャン** — フォルダ名・ファイル名・UV トークンから色バリエーションを自動グルーピング
- **自動マッピング** — アバターのどのメッシュ・スロットに適用するかを自動検出（複数メッシュ連動対応）
- **非破壊セットアップ** — Modular Avatar の Menu Item / Material Setter / Parameters を生成するだけ。FX レイヤーやアバター本体には触れない
- **手動編集** — グループの作成・分割・統合、バリエーションの追加・削除、適用先レンダラーの手動指定がすべて UI 上で可能
- **VRAM 見積り** — 切替対象テクスチャの容量を試算し、上限超過時はワンクリックで縮小
- **ユーティリティ** — Streaming Mip Maps 一括修正 / 旧バージョン残骸のクリーンアップ（いずれも実行前に対象を確認表示）

## どうやって色違いを見分けているか

マテリアルの置き場所と名前から「パーツ（切替対象）」と「色（バリエーション）」を推定します。

| 優先 | 手がかり | 例 |
|---|---|---|
| 1 | UV トークン（`UV1`, `uv_2` など） | `UV1 Check/`, `Kimono_UV2.mat` → 同じ UV 番号 = 同じパーツ |
| 2 | パーツ名フォルダ | `Obi/black.mat`, `Obi/red.mat` → フォルダ = パーツ |
| 3 | ファイル名の色トークン | `shirt_black.mat`, `shirt_white.mat` → 共通部 = パーツ、差分 = 色 |

色名の判定は内蔵辞書（英語色名・柄名・ローマ字色名）で行います。
辞書にない作者独自の色名は `ProjectSettings/VRCMaterialSwitcherKeywords.json` で追加できます
（詳細: [MANUAL.md](MANUAL.md#4-スキャンの仕組みと命名規則)）。

自動検出はあくまで「候補の提示」です。結果は必ず UI で確認し、必要なら手動で修正してください。

## ドキュメント

- **[MANUAL.md](MANUAL.md)** — 全機能の詳細マニュアル（手動セットアップ・命名規則・トラブルシューティング）
- **[CHANGELOG.md](CHANGELOG.md)** — 変更履歴

## License

MIT License
