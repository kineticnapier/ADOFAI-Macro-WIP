# ADOFAI-Macro-WIP

A Dance of Fire and Ice（ADOFAI）向けのマクロ実験用リポジトリです。  
メインの自動入力ツールに加えて、譜面編集の補助ツールも同梱しています。

> ⚠️ このリポジトリはゲーム入力を自動化するツールを含みます。利用は自己責任で行ってください。

## 構成

このソリューションには以下の 4 プロジェクトがあります。

- `ADOFAI-Macro`  
  ADOFAI 譜面（`.adofai`）を解析し、入力イベントをスケジューリングしてキーボード入力を送出するメインツール。
- `AdofaiPauseConverter`  
  `Pause` イベントを `SetSpeed` ベースに変換する補助コンバーター。
- `JSONDuplicatorDelete`  
  JSON 内の重複キーを検出・整形し、バックアップ付きで保存するクリーナー。
- `VKCodeViewer.cs`  
  押したキーの Windows Virtual-Key コードを表示する簡易ツール。

## 前提環境

- Windows（`user32.dll`, `winmm.dll` を使用）
- .NET SDK 10.0（`net10.0` ターゲット）


## C++ ネイティブ高速化（新規）

計算量の大きい一部処理を C++ ネイティブライブラリ (`native/`) に移し、`P/Invoke` で呼び出せるようにしました。
現在ネイティブ化済みの処理:

- ノーツ時間テーブル生成（`generate_delay_table`）
- 譜面進行に応じたキー数解決（`resolve_key_counts`）

### ネイティブライブラリのビルド

```bash
cmake -S native -B native/build
cmake --build native/build --config Release
```

出力された `adofai_native` (`.dll` / `.so`) を `ADOFAI-Macro` 実行ファイルと同じ場所へ配置すると、
自動的に C++ 実装を利用します（見つからない場合は C# 実装にフォールバック）。

## ビルド

```bash
dotnet build ADOFAI-Macro.slnx
```

## 使い方

### 1) ADOFAI-Macro（メイン）

譜面パスを引数で渡すか、起動後にコンソール入力します。

```bash
dotnet run --project ADOFAI-Macro -- "C:\path\to\chart.adofai"
```

実行時のポイント:

- 最初のタイルは手動で叩いて開始（デフォルト開始キーは `Space`）。
- 再生中は `←` / `→` でオフセット調整。
- 起動時に「使用キー数レンジ」の入力が求められます（ノーツ範囲ごとのキー数設定）。

### 2) AdofaiPauseConverter

`Pause` を `SetSpeed` に変換した譜面を出力します。

```bash
dotnet run --project AdofaiPauseConverter -- "input.adofai" "output.adofai"
```

引数なしの場合は、標準入力で `input` / `output` パスを受け取ります。

### 3) JSONDuplicatorDelete

対象 JSON を解析して重複キーを検出し、元ファイルの `.bak` を作成したうえで整形保存します。

```bash
dotnet run --project JSONDuplicatorDelete -- "chart.adofai"
```

### 4) VKCodeViewer

キー入力時の VK コードを表示します（`ESC` で終了）。

```bash
dotnet run --project VKCodeViewer.cs
```

## 注意事項

- 高密度譜面では入力遅延の影響で理論上再現が困難になる場合があります。
- 利用キー数を増やしすぎると処理負荷が増えるため、必要最小限のキー構成を推奨します。
- 事前にコピー譜面で動作確認し、原本データはバックアップを取ってください。
