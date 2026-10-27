# SYS-FIELD-001 v1.0 実装報告

対象はUnity `6000.6.6f1` / C# のTOKAI2既存プロジェクトです。ブランチ `codex/sys-field-001-v1-0` は `origin/plan-main` を基点とし、接続先のSYS-GUARD-001 v1.2とSYS-VISION-001 v1.0のコミットを含みます。PRのbaseは `plan-main` です。

## 実装範囲

固定段差盤面をデータ駆動のフィールドへ移行しました。左下原点のXY、連続行範囲による非長方形、有効セル・各行のスタートと出口・固定柵禁止位置の算出、初期柵・初期看守、構造検証、未確定ステージの開始拒否と選択UIを実装しています。監視の確率・山札・履歴と純粋な視界式は変更せず、入出力を接続しました。

ステージ01は元資料の行合計24マスと総数25マスが不一致でした。**ユーザー承認によりY=2の終了Xを4から5へ変更**し、25マスと列ごとのマス数 `2・4・5・5・5・3・1` に一致させています。AIによる無断の形状補正ではありません。

ステージ02〜05の初期柵は未確定です。空配列を正式な「柵なし」に変換せず、一覧に状態を表示して対局開始を拒否します。最大寸法、個別カード・囚人能力・喧嘩のルールも独自に追加していません。

## 追加・変更ファイル

| ファイル | 役割 |
|---|---|
| `Assets/Scripts/Daidatsugoku/FieldCell.cs` | `FieldCell(X,Y)` と `FieldRow(Y,StartX,EndX)` の値型。 |
| `FieldStage.cs` | 変更不可の形状、`Cells`／`StartCells`／`ExitCells`／`FenceForbiddenCells`／`CellCount`、`ClassifyMove()`／`ClassifyPush()`。 |
| `FieldStageValidator.cs` | `Validate()`、`ValidateCatalog()`、`TryValidateForPlay()`。元データを修正せず構造・開始可否を検証。 |
| `FieldStageDefinition.cs` | Unity編集用アセットと `ToStage()` によるコピー。初期柵の確定状態と確認待ち理由 `sourceIssue` を保持。 |
| `StageSelectionPanel.cs` | ステージ一覧・名称・寸法・マス数・開始可否、選択と開始。追加件数に応じたスクロール。 |
| `PrisonerCollisionResolver.cs` | 囚人システムへ衝突を委譲する `IPrisonerCollisionResolver.TryResolve()`。 |
| `GridManager.cs` | 実在セルだけの盤面生成、動的寸法、占有参照、上端柵、初期柵、看守表示・カメラ、視界座標変換。 |
| `GameManager.cs` | リソース読込、カタログ検証、選択・開始、配置制約、衝突委譲。未確定再開始で旧盤面を進行させない。 |
| `NormalActionRules.cs` / `Prisoner.cs` | 空きスタートへの通常配置、非実在セル・同居の防止。既存のタイプ未分化な移動方向は維持。 |
| `GuardVisionSystem.cs` | 選択フィールドから寸法・看守初期値を取得。反転状態を維持し、非実在セルを視界から除外。 |
| `GuardSystem.cs` / `GuardVisionDebugDisplay.cs` | 対象囚人とデバッグのXY変換への接続。監視抽選は変更なし。 |
| `GameHud.cs` | ステージ選択パネルと結果画面からの戻り口。履歴を上位表示。 |
| `Assets/Resources/FieldStages/Stage01.asset`〜`Stage05.asset` | 5形状と既知の初期値。02〜05は `initialFencesDefined=false`。 |
| `Assets/Scenes/Daidatsugoku.unity` | 現行フィールド参照へ対応。 |
| `Assets/Editor/FieldStageAssetBuilder.cs` | 初回アセット生成。既存アセットを上書きしない。 |
| `FieldStageChecks.cs` / `FieldIntegrationChecks.cs` | データ・境界と実シーン描画・座標・占有・選択・拒否のテスト。 |
| `DaidatsugokuPrototypeChecks.cs` / `GuardSystemChecks.cs` / `GuardVisionIntegrationChecks.cs` | 既存回帰テストを動的寸法・XY・単一占有へ対応。 |
| `GuardValidationBatch.cs` / `GuardVisionSceneSetup.cs` | フィールド検証・画像・ビルドの入口、Editor設定処理。 |
| `Assets/Scripts/Daidatsugoku/README.md` | 起動・設定・接続と依存関係を更新。 |

名前だけのランタイムファイルは `Assets/Scripts/Daidatsugoku/`、Editorファイルは `Assets/Editor/` 以下です。新規ソース・Unityアセットには `.meta` を付属します。旧 `GuardVisionFieldDefinition` と試作アセットは削除せず残しますが、現在の実行データ源として使用しません。

## 他システムとの接続

### 座標と視界

盤面・囚人・柵は左下原点XYです。純視界計算の上始まり座標へは `GridManager.ToVisionCell()` が `row=height-1-Y, column=X` として一度だけ変換し、`TryGetVisionCell()` で逆変換します。柵・発見対象・デバッグも同じ変換を使います。純計算式やTOP/BOTTOM遮蔽条件を維持し、最後に非実在セルを除外します。

初期看守はTOPのみです。GとG±1の各列に実在する最大Yのセルを求め、3か所の上端を禁止位置とします。各列の上端は同じYである必要はありません。反転しても禁止位置を動かさず、BOTTOM側の禁止位置も作りません。ステージ変更後は看守を初期化し直すまで計算を拒否します。

### 配置・移動・押し出し

`CanPlacePrisoner()` は各行の空きスタートを返し、柵の有無では拒否しません。`OccupantAt()` は現在の囚人リストから問い合わせ、占有状態を重複保持しません。1マスに2人を描く旧オフセット処理を廃止しました。

`FieldStage.ClassifyMove(from,to)` は囚人側が方向・距離を検証した後に使います。移動元の行の右端を越える右移動を脱獄とし、斜め右の上下範囲外より優先します。右端を越えない非実在着地は拒否します。`ClassifyPush()` は同じ行の左右端を使い、左は未配置へ戻る結果、右は脱獄結果を返します。分類API自体は囚人状態・能力・勝利を変更しません。

既存通常操作は右・上・下の隣接移動と行右端からの脱獄を維持しています。占有先への移動は `IPrisonerCollisionResolver` に委譲し、未接続時は同居や独自抽選を行わず仕様待機にします。衝突解決が停止した状態を通常行動完了処理で上書きしないようにしています。

### データ駆動と検証

`Resources.LoadAll<FieldStageDefinition>("FieldStages")` で起動時に列挙します。新規ステージは同フォルダーへアセットを追加するだけで、ID分岐・描画コードの追加なしで一覧に反映します。

最小5行・3列、必須ID／名称／行、全Yの一意な連続範囲と寸法内座標、TOP初期看守と正面3列を検証します。初期柵の欠落・重複・非実在・禁止位置、カタログの重複IDを拒否します。検証は形状や柵を削除・移動しません。不正カタログ、未確定ステージ、確認待ちアセットから旧盤面を再開できないよう、初期化と旧開始APIにも制約を適用します。

未確定柵は `initialFencesDefined=false` です。構造の参照はできますが開始不可で、空配列でも確定扱いにしません。正式な柵なしは `true` と空配列で表します。最大サイズをルールとして固定せず、`Int32` 座標・コレクション件数や描画・メモリを技術上の制約として区別します。大きすぎるフィールドの負荷限界は未検証です。

## ステージデータ

| ID | 幅×高さ | マス数 | 初期看守 | 初期柵・状態 |
|---|---|---:|---|---|
| 01 | 7×5 | 25 | 列3・TOP | (1,3)、(1,2)、(2,0)、(3,2)、(4,1)。フィールド準備済み |
| 02 | 8×5 | 34 | 列3・TOP | 未確定・開始拒否 |
| 03 | 7×6 | 31 | 列2・TOP | 未確定・開始拒否 |
| 04 | 7×7 | 35 | 列2・TOP | 未確定・開始拒否 |
| 05 | 9×9 | 55 | 列4・TOP | 未確定・開始拒否 |

## テスト結果

最終の実シーン検証は **2,087 assertions PASS**。フィールド単体109件、実際のResources内の5ステージアセット、明示的な追加フィールドの統合検証、既存ターン・監視・視界・履歴UIの回帰を含みます。ステージ01は承認された25マスの正式アセットで検証し、フォールバックを使わず開始することを確認しました。

| 内容 | 検証範囲 |
|---|---|
| 非長方形・スタート・出口 | 行範囲、全実在セルと欠落セル、行左端・右端を純C#と実シーンで確認。 |
| ステージ01 | Resourcesアセットから25マス、列別 `2・4・5・5・5・3・1`、Y=2の終了X=5、指定初期柵5本と選択経由の開始を確認。 |
| ステージ02〜05 | 34・31・35・55マスと各3禁止位置を照合。初期柵未確定による開始拒否。 |
| 斜め脱獄・上下外形・押し出し | 分類APIの固定期待値。タイプごとの実行操作は未実装。 |
| 柵・禁止位置 | 実シーンで上端ワールド位置、破壊、最新柵状態、反転後の固定禁止位置。 |
| 占有 | 柵付きスタート可、占有スタート不可、直接配置・移動でも同居不可。 |
| 動的寸法・追加データ | 8×6・32マス統合フィクスチャー、純データ40×60。 |
| XY／視界 | 実在セル全件の往復変換、TOP/BOTTOM遮蔽、非実在セル除外。 |
| 不正データ | 必須値、範囲外、行重複、初期柵の重複・禁止・非実在、初期BOTTOM、重複IDなどの拒否。 |
| 選択と進行 | 未確定選択で監視・囚人・ラウンドを変えず、再開始／旧開始APIでも選択を迂回しない。 |
| 衝突接続失敗 | 未接続または接続先がfalseを返した場合、単一占有と仕様待機を維持して後続進行を止める。 |
| 既存回帰 | ターン、監視30枚・履歴、視界・監視無効化・対象一括通知、HUD。 |

最終結果は `Logs/SYS-FIELD-001-validation.txt` です。既にUnity Editorが開いていたため、最終実行はUnity MCP経由で `GuardValidationBatch.RunField()` を呼びました。Editorは閉じず、検証後にPlayモードを終了しています。`Logs/SYS-FIELD-001-unity.log` は並行Editorによって終了した別起動のログであり、最終検証結果とは扱いません。

`Logs/SYS-FIELD-001-selection.png` と `Logs/SYS-FIELD-001-regression.png` を表示し、開始可能なステージ01、未確定の02〜05、開始ボタンの可読性、XY盤面、行ごとのスタート・出口、上端の初期柵、単一看守表示を確認しました。盤面画像のカードと囚人配置にはEditor専用の操作フィクスチャーを使用し、本番カードを追加していません。

Windows64 Developmentビルドは **成功・エラー0・警告506件、11.60秒**。`Logs/SYS-FIELD-001-build.txt` に要約を保存しています。Unityパッケージ・描画関連などのビルド警告は未解消です。Playerを非表示で6秒起動し、早期終了とゲームスクリプト例外がないことを確認して、検証用プロセスのみ終了しました。`Logs/SYS-FIELD-001-player.log` に既存同様のD3D12 info queue照会失敗は残っています。ビルド時の自動生成設定4ファイルの差分は実装から除外しています。

### 再実行方法

実シーンの非同期チェックは完了時に自動終了するため、`RunField` に `-quit` を付けません。

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe' -batchmode -projectPath C:\DevGame\TOKAI2 -executeMethod GuardValidationBatch.RunField -logFile Logs/SYS-FIELD-001-unity.log
```

Windows64 Developmentビルドは `-batchmode -quit -executeMethod GuardValidationBatch.BuildFieldPlayer` を使用します。出力先は `Builds/SYS-FIELD-001/TOKAI2.exe`、要約は `Logs/SYS-FIELD-001-build.txt` です。

## 未実装・保留と完了範囲

- ステージ02〜05は形状定義済みですが、正式初期柵が未確定なので完成ステージとは扱いません。
- SYS-PRISONER-001の実装前確認・承認が未完了です。6種類・6人、タイプ別移動、喧嘩抽選・連鎖、未配置復帰、固有能力、ポイント勝利と即時中断は未実装です。今回のフィールドAPIと衝突接続口へ別途接続します。
- 既存の汎用囚人3人／プレイヤー・脱獄人数ベースの勝利・捕獲状態を維持しています。囚人仕様に一致済みとはしません。
- 本番カード・山札構成・正確な割り込み条件と優先順位は未提供で、既定シーンの通し対局はカード設定待ちです。
- 柵の追加設置・破壊条件などSYS-FENCE-001の未確定ルールは追加していません。フィールドが確定させた上端位置・初期柵・禁止位置を反映しています。
- 通常画面の詳細デザイン、バランス、巨大フィールドの負荷限界、オンライン、SceneビューGizmosの手操作は対象外または未検証です。

共通フィールドの確定範囲と5形状を実装し、ステージ01の形状補正は承認を得ています。未確定柵と未接続の囚人・カード仕様を分離し、製品版対局全体が完成したとの判断は行いません。
