# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 言語

ユーザーへの回答、コードコメント、コミットメッセージ、ドキュメントは**日本語**で記述すること。

## プロジェクト概要

Unity Learn「Create with VR」コースのテンプレートをベースにした Unity VR プロジェクト。Meta Quest 向け（Android APK ビルド、`Builds/` を参照）で、インタラクションには XR Interaction Toolkit を使用している。

- Unity エディター: **6000.4.3f1**（`ProjectSettings/ProjectVersion.txt`）
- レンダーパイプライン: URP 17.4
- XR: XR Interaction Toolkit 3.4、OpenXR、Oculus XR Plugin、XR Management。入力は新しい Input System
- ユーザー自身のシーンは `Assets/Scenes/Macky Room.unity` で、Build Settings で有効になっている唯一のシーン。他の Build Settings のエントリの多くは、このリポジトリに存在しないコースのシーンを指している。
- 使用デバイスはMeta Quest3を用いる。

## ビルドとテスト

Unity エディター用のプロジェクトで、コマンドラインのビルドスクリプト、リンター、テストスイートはない。ビルドと実行は Unity エディター（File > Build Profiles、Android/Quest ターゲット）で行う。`.csproj` と `VR Room Project.slnx` は Unity が再生成するため手動で編集しない。`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Build(s)/` は gitignore 対象。

Unity のバッチモードでヘッドレスにコンパイルチェックできる（エディターのインストールパスは環境に合わせて変更）:
```
"C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\UnityProjects\VR Room Project" -logFile -
```
プロジェクトがエディターで開かれている場合は失敗する。

## アーキテクチャ

### `Assets/_Course Library/` — コース提供の部品
スクリプトはグローバル名前空間にある小さな単機能の MonoBehaviour（asmdef なし、`Assembly-CSharp` にコンパイルされる）。他のコードから呼び出すのではなく、**Inspector 上で UnityEvent を介して接続する**前提で作られている:

- `Scripts/Conditions/` — イベントの発生源（`OnButtonPress`、`OnCollision`、`OnTrigger`、`OnVelocity`、`OnTilt`、`OnPull`、`OnTimedInterval` など）。それぞれ public な `UnityEvent` フィールド（例: `OnPress`、`OnRelease`）を公開している。
- `Scripts/Actions/` — イベントの受け手。`Play()`、`Toggle()`、`Spawn()` など、引数なし（または単純な引数）の public メソッドを持ち、UnityEvent のターゲットとして接続する。
- `Scripts/Controls/` — XRI のインタラクタブルをベースにした物理的な VR コントロール（`XRButton`、`XRLever`、`XRKnob`、`XRSlider`、`XRJoystick`）。値や状態の UnityEvent を発行する。
- `Scripts/Testing/` — デバッグ用ヘルパー（FPS 表示、メモ、フェード付きテレポートなど）。
- `_Prefabs/`、`Materials/`、`Audio/`、`Input Presets/`、`Source Files/Sample Scenes/`（Modern/Rustic/Traditional の部屋サンプル）は、これらのスクリプトと組み合わせて使うアセット。

振る舞いはシーンやプレハブにシリアライズされた接続で決まるため、**public メソッド、UnityEvent フィールド、シリアライズされたフィールドの名前を変更すると、Inspector の参照が警告なしに壊れる**。名前を変更する前に `.unity`/`.prefab` の YAML（`m_MethodName: <名前>`）を検索すること。

### `Assets/Challenges/` — コースのチャレンジシーン
`01_Architecture`、`02_3DPainting`、`03_Training` があり、それぞれ修正対象の `*_Prototype_Broken.unity` と参照用の `*_Prototype_Fixed.unity` がある。

### その他
- `Assets/Samples/XR Interaction Toolkit/3.1.3/` には XRI Starter Assets（XR Origin リグ、入力アクション）と、ヘッドセットなしでテストできる XR Device Simulator が入っている。インポートされたパッケージのサンプル（独自の asmdef あり）なので編集は避ける。
- `Assets/XR/`、`Assets/XRI/` には XR ローダーと XRI のプロジェクト設定アセットがある。

## リポジトリに関する注意

- `.gitattributes` は Unity YAML マージ（`merge=unityyamlmerge`）を使い、Unity YAML の改行を LF に強制している。Windows では CRLF→LF の正規化により多くのファイルが変更ありと表示されるが、実際の内容は変わっていない。
- すべてのアセットには GUID を保持する `.meta` ファイルがある。アセットの移動・名前変更・削除は `.meta` ファイルと一緒に行わないと参照が壊れる。
