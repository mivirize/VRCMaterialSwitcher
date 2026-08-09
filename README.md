# VRC Material Switcher

VRChat アバターのマテリアル（テクスチャ・カラー）切替メニューを **自動で生成** する Unity エディタツールです。  
Modular Avatar 対応。衣装の色違いやパーツの切替を、Expression Menu のトグルとしてワンクリックでセットアップします。

## ✨ 主な機能

- **自動マテリアルスキャン**: 指定フォルダ内のマテリアルを解析し、カラーバリエーションを自動グルーピング
- **自動レンダラーマッピング**: アバター上のメッシュと検出されたマテリアルグループを自動で紐付け
- **Modular Avatar セットアップ**: Expression Menu・パラメータ・MaterialSetter を一括自動生成
- **複数メッシュ連動**: 浴衣の上下など、複数メッシュに同じマテリアルが適用されるケースに対応
- **Streaming Mip Maps 一括修正**: VRChat アップロード時のテクスチャバリデーションエラーを自動修正
- **FX レイヤー自動クリーンアップ**: 過去のセットアップ残骸を自動検出・削除

## 📦 インストール

1. [Releases](../../releases) から最新の `VRCMaterialSwitcher.unitypackage` をダウンロード
2. Unity プロジェクトにドラッグ＆ドロップでインポート

### 前提条件

- Unity 2022.3.x
- VRChat SDK - Avatars 3.x
- Modular Avatar

## 🚀 使い方

1. `Tools > VRC Material Switcher` でウィンドウを開く
2. **アバター**: シーン上のアバターを指定
3. **スキャンフォルダ**: 衣装マテリアルが入っているフォルダを指定
4. **「スキャン実行」** をクリック → マテリアルグループが自動検出される
5. 不要なバリエーションのチェックを外す / ★でデフォルトを設定
6. **「セットアップ実行」** をクリック → Modular Avatar コンポーネントが自動生成
7. アバターをアップロード

## 📁 ファイル構成

| ファイル | 説明 |
|---|---|
| `MaterialSwitcherData.cs` | データモデル定義（バリエーション・グループ・ターゲット） |
| `MaterialVariationDetector.cs` | マテリアル自動スキャン＆レンダラー自動マッピング |
| `MaterialSwitcherSetup.cs` | Modular Avatar セットアップ実行 |
| `MaterialSwitcherWindow.cs` | エディタウィンドウ（GUI） |
| `ParamResidueCleaner.cs` | FX コントローラーの残骸レイヤー自動クリーンアップ |
| `StreamingMipMapFixer.cs` | テクスチャ Streaming Mip Maps 一括修正 |
| `PackageExporter.cs` | パッケージエクスポートユーティリティ |

## 🛠️ トラブルシューティング

### アップロード時に "mipmapped textures without Streaming Mip Maps" エラー
`Tools > VRC Material Switcher > Fix Streaming Mip Maps (Project-wide)` を実行してください。

### チェックボックスが効かない
最新版では変更が即時保存されるよう修正済みです。パッケージを再インポートしてください。

## 📄 License

MIT License
