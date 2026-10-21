# スマホ向けWeb版

- Webのテンプレートは `PROJECT:Mobile`。ブラウザの表示領域と画面回転に追従し、端末のノッチ領域を避けます。
- 縦画面は上に盤面・下に操作パネル、横画面は左に操作パネル・右に盤面を表示します。
- カメラの表示範囲を画面比率に合わせ、盤面の左右が欠けないようにします。
- UIと盤面は同梱の Noto Sans JP を共有します。フォントのライセンスは `Assets/Resources/Fonts/OFL.txt`。
- Input Systemの既存ポインター操作でタップできます。柵のコライダーを広げ、細い柵も選択しやすくしています。
- 高密度ディスプレイの描画倍率を最大2倍に抑えます。圧縮ファイルの展開フォールバックを有効にし、専用の圧縮ヘッダー設定がない静的ホストでも配信しやすくします。
- App UI 2.2.4のコードエディタ向けTextMateネイティブライブラリは、このゲームでは使わないためWebビルドから除外します。Unity 6.6では古いEmscripten ABIによる未解決シンボルが発生するためです。EditorのApp UI機能は使用できます。今後ゲーム内でApp UIのシンタックスハイライトを使う場合は、対応バイナリへの更新とこの除外処理の見直しが必要です。
- WebのManaged Stripping LevelはHighにし、未使用のTextMate呼び出しも除去します。このゲームの動的ロード用フォントとマテリアルはResourcesに配置し、CreatePrimitiveで生成する描画・当たり判定の型はlink.xmlで保持します。今後リフレクションで型を読み込む場合は、必要な型の保持設定も追加してください。

## ビルド

UnityのWeb Build Supportを導入してください。Unityを閉じた状態で次の引数を指定すると、ゲームシーンを明示したWebビルドを作れます。

```
Unity.exe -batchmode -nographics -quit -projectPath <project> -executeMethod MobileWebBuild.Build -logFile <log>
```

出力先は `Builds/MobileWeb`。HTTP(S)サーバーでフォルダ全体を配信します。
通常のBuild Profilesからビルドする場合は、別PRの公開シーン設定をマージするか、シーン一覧に `Assets/Scenes/Daidatsugoku.unity` を登録してください。

## 実機で確認する項目

iPhoneのSafariとAndroidのChromeで、起動・日本語・縦横回転・駒配置・駒選択・移動・柵設置と破壊・交代確認・リスタートを確認します。
端末のメモリやGPUによる差があるため、EditorやWebビルド成功だけでは実機動作の確認にはなりません。
