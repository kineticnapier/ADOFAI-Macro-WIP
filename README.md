# ADOFAI-Macro-WIP

A Dance of Fire and Ice（ADOFAI）向けのマクロ実験用リポジトリです。  
メインの自動入力ツールに加えて、譜面編集の補助ツールも同梱しています。

> ⚠️ このリポジトリはゲーム入力を自動化するツールを含みます。利用は自己責任で行ってください。

## 構成

このソリューションには以下の 5 プロジェクトがあります。

- `ADOFAI-Macro`  
  ADOFAI 譜面（`.adofai`）を解析し、入力イベントをスケジューリングしてキーボード入力を送出するメインツール。
- `AdofaiPauseConverter`  
  `Pause` イベントを `SetSpeed` ベースに変換する補助コンバーター。
- `JSONDuplicatorDelete`  
  JSON 内の重複キーを検出・整形し、バックアップ付きで保存するクリーナー。
- `VKCodeViewer.cs`  
  押したキーの Windows Virtual-Key コードを表示する簡易ツール。
- `Macro-Inserter`  
  UMM向けのmod用プロジェクト。ADOFAI内部で仮想入力を発生させる譜面検証用の実験機能です。競技、提出、ランキング用途では使用しないでください。

## 前提環境

- Windows（`user32.dll`, `winmm.dll` を使用）
- .NET SDK 10.0（`net10.0` ターゲット）

## ビルド

```bash
dotnet build ADOFAI-Macro.slnx
```

`Macro-Inserter` はADOFAI本体のManaged DLLを参照します。既定ではSteam版の標準インストール先を見ます。別の場所にある場合は `ADOFAIManagedDir` を指定してください。

```bash
dotnet build Macro-Inserter/Macro-Inserter.csproj -p:ADOFAIManagedDir="C:\path\to\A Dance of Fire and Ice_Data\Managed"
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

### 5) Macro-Inserter

Unity Mod Managerで読み込むADOFAI内部マクロです。外部SendInput/Pico入力ではなく、`scrController` の内部入力候補に対してReflection/Harmonyで入力を差し込みます。

UMM設定:

- `EnableInternalMacro`: 内部マクロを有効化します。デフォルトはOFFです。
- `DryRun`: 実入力せず、`targetTime`, `audioTime`, `diffMs`, `seqID` をログ出力します。
- `MacroOffsetMs`: 予定入力時刻に加えるオフセットです。
- `StartFromCurrentFloor`: 現在フロア以降から開始します。
- `UseAudioTime`: `AudioSource.timeSamples / clip.frequency` を基準にします。OFFの場合は現在の音声時刻に固定したUnity unscaled timeを使います。
- `FireMode`: `DirectHit` は `scrController.instance.Hit(false)` を呼び、`InputPatch` は `ValidInputWasTriggered` と `CountValidKeysPressed` を予定フレームだけ差し替えます。

安全条件として、エディタ再生中またはPlayerControl中のみ動作し、pause中は進行しません。UMM画面や入力欄の操作中はスケジューラを開始しません。

## 注意事項

- 高密度譜面では入力遅延の影響で理論上再現が困難になる場合があります。
- 利用キー数を増やしすぎると処理負荷が増えるため、必要最小限のキー構成を推奨します。
- 事前にコピー譜面で動作確認し、原本データはバックアップを取ってください。
- `Macro-Inserter` は譜面検証用です。競技、提出、ランキング用途では使用しないでください。
