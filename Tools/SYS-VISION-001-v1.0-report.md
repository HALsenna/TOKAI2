# SYS-VISION-001 v1.0 実装報告

対象は、ユーザー提供の「SYS-VISION-001 看守視界システム 実装指示書」と、先に提示された統合引き継ぎ資料の確定済み視界ルールです。作業ブランチは `codex/sys-vision-001-v1-0`。最新 `origin/plan-main` から作成し、依存するSYS-GUARD-001 v1.2実装コミットを引き継いでいます。

ユーザーの指示によりPR作成は保留です。視界実装のために監視デッキの確率・構成・シャッフル・消費・履歴管理は変更していません。

## 1. 事前確認

| 項目 | 確認結果 |
|---|---|
| エンジン・言語 | Unity `6000.6.6f1`、C#、既存UGUI・3D盤面。 |
| ランタイム構造 | `Assets/Scripts/Daidatsugoku` にゲーム進行・監視・盤面・囚人。 |
| ゲームフロー | `GameManager` がターン・カード対応・看守ターン・勝利確定を管理。 |
| 監視 | SYS-GUARD-001 v1.2の30枚山札・公開履歴・履歴UIを再利用。 |
| フィールド | `GridManager` の25マス段差盤面。行ごとにローカル列があり、`GlobalColumn()` で全体列へ変換。 |
| 柵 | `FenceBoundary` による共有境界。現在の配置を `GridManager` が保持。 |
| 囚人 | `Prisoner.Cell` と盤上・未配置・捕獲・脱獄状態を再利用。 |
| カード対応 | `SkillCardDefinition`、`TurnEvent`、`ResponseChain`、外部 `TurnFlowPolicy` を再利用。 |
| 検証基盤 | `Assets/Editor` の実シーンチェック、Unityバッチ実行、Unity同梱Roslyn・Mono。 |

## 2. 追加・変更ファイルと責務

各新規スクリプトにはUnity用 `.meta` を追加しています。

| ファイル | 区分・役割 |
|---|---|
| `Assets/Scripts/Daidatsugoku/VisionCell.cs` | 追加。0始まりの `Row` / `Column` 論理座標と値の等価性・ハッシュ。集合で重複を排除。 |
| `Assets/Scripts/Daidatsugoku/GuardPosition.cs` | 追加。基準列とTOP/BOTTOMの状態。`Flip()` は列を変えず方向だけを切り替える。 |
| `Assets/Scripts/Daidatsugoku/GuardVisionCalculator.cs` | 追加。Unity非依存の基本視界・遮蔽・最終視界計算。 |
| `Assets/Scripts/Daidatsugoku/GuardVisionFieldDefinition.cs` | 追加。行数・列数・看守初期列・方向を明示するフィールドデータと妥当性確認。 |
| `Assets/Scripts/Daidatsugoku/GuardVisionSystem.cs` | 追加。フィールドからの初期化、外部からの反転、既存盤面から最新情報を取得するアダプター。 |
| `Assets/Scripts/Daidatsugoku/GuardVisionDebugDisplay.cs` | 追加。明示更新した看守・前面柵・最終視界のScene Gizmos表示。通常プレイ時は無効。 |
| `Assets/Editor/GuardVisionSceneSetup.cs` | 追加。既存プロトタイプ向けの明示的なフィールドアセット・シーン参照設定。 |
| `Assets/Settings/PrototypeGuardVisionField.asset` | 追加。既存配置を保持する仮データ。正式フィールド仕様ではない。 |
| `Assets/Scenes/Daidatsugoku.unity` | 変更。視界用フィールド・コンポーネント参照と、TOP/BOTTOM両方の看守が見えるカメラ設定。 |
| `Assets/Scripts/Daidatsugoku/GridManager.cs` | 変更。現在の横向き前面柵取得、看守の位置表示・非表示。移動・柵設置破壊規則は維持。 |
| `Assets/Scripts/Daidatsugoku/GuardIntegration.cs` | 変更。視界スナップショット・囚人一括更新インターフェースを追加。 |
| `Assets/Scripts/Daidatsugoku/GuardSystem.cs` | 変更。スナップショットを1回取得して全囚人を照合、重複しない全対象の一括発見通知。抽選と履歴処理は維持。 |
| `Assets/Scripts/Daidatsugoku/GameManager.cs` | 変更。監視への許可された割り込みを視界計算前に解決し、最終結果確定後に発見を適用。 |
| `Assets/Scripts/Daidatsugoku/SkillCardDefinition.cs` | 変更。監視無効化系カードの明示的な対応資格を表す拡張口。個別カードは追加しない。 |
| `Assets/Scripts/Daidatsugoku/TurnContracts.cs` | 変更。`GuardMonitoring` イベントと抽選済み `MonitoringResult` を追加。 |
| `Assets/Editor/GuardVisionCalculatorChecks.cs` | 追加。純C#視界・位置状態の決定的チェック。 |
| `Assets/Editor/GuardVisionIntegrationChecks.cs` | 追加。実盤面の位置・柵更新、監視前の割り込み、視界の単一スナップショット、一括通知・無効化・デバッグ状態を検証。 |
| `Assets/Editor/DaidatsugokuPrototypeChecks.cs` | 変更。新しい看守フローとの回帰検証を接続。 |
| `Assets/Editor/GuardValidationBatch.cs` | 変更。`RunVision()` による実シーン検証、TOP/BOTTOMの画像保存と視界用ビルド入口を追加。 |
| `Assets/Scripts/Daidatsugoku/README.md` | 変更。設定・暫定接続・デバッグ操作・検証範囲を更新。 |
| `Tools/SYS-VISION-001-v1.0-report.md` | 追加。本報告。 |

Unityで生成したフィールドアセットと既存シーン参照を含めて成果物とし、仮データをコード内の暗黙の初期配置にはしていません。

## 3. 実装した処理と接続方法

### 3.1 看守状態と視界計算

`GuardPosition.Column` は基準列、`Side` はTOP/BOTTOMです。初期値は `GuardVisionFieldDefinition` から取得し、ランダム決定しません。`GuardVisionSystem.Flip()` は状態を反転して常時位置表示と `PositionChanged` を更新します。反転だけでは監視抽選・発見判定を実行しません。

`GuardVisionCalculator.CalculateBaseVisibleCells()` は、TOPなら `d = row + 1`、BOTTOMなら `d = rowCount - row` とし、左右への広がりを整数演算 `(d - 1) / 3 + 1` で計算します。盤面境界で切り捨て、重複しない `HashSet<VisionCell>` を返します。

`CalculateVisibleCells()` は完成した基本視界に含まれる柵だけを採用します。列ごとにTOPでは最小行、BOTTOMでは最大行の柵を基準とし、TOPは `row >= fenceRow`、BOTTOMは `row < fenceRow` を除外します。入力の柵集合は変更しません。毎回新しい集合を計算し、過去の視界や柵配置を再利用しません。

`GuardVisionSystem.CalculateVisibleCells()` はその時点のフィールド・看守位置・柵から計算し、既存段差盤面に実在するマスへ制限します。`IGuardVisionSnapshotService` によって、1回の発見判定中に囚人ごとの再計算を行いません。

### 3.2 監視と割り込み

標準フローは次の順序です。

1. 監視v1.2から1枚取得し、公開結果を保持する。
2. `GuardMonitoring` イベントに対して、許可された監視無効化系の割り込みを受け付ける。
3. 外部の対応計画と既存チェーンに従って割り込みを解決する。
4. 監視結果と無効化状態を確定する。
5. 「見とる」かつ監視有効の場合だけ最新の視界を計算する。
6. 全盤上囚人を同じスナップショットで照合し、全発見対象を一括通知する。
7. 囚人処理へ委譲した後、看守ターンを完了する。

`CanInvalidateGuardMonitoring` は対象カードを明示する印です。`CanPlay()` と `TurnFlowPolicy` による条件・対応計画も必要で、印だけで正式な使用タイミング・初回対応権・優先順位を定義しません。個別の監視無効化カードや反転カードは新規作成していません。

「見とらん」または取消済み監視では、視界計算と発見通知を省略します。監視の再試行で余分なカードを引かず、結果適用を重ねません。旧所有者別の捕獲対応コードは互換用に残していますが、標準フローからは呼びません。

### 3.3 囚人と演出

`PrisonersDiscovered` には変更不可の全対象リストを一度に渡します。重複した囚人は除き、未配置・既に除外済みなど盤上状態にない囚人は対象にしません。囚人更新は `IGuardPrisonerBatchService` または既存の `IGuardPrisonerService` へ委譲します。

既定アダプターは既存 `Prisoner.Capture()` を使用します。捕獲後は盤面から除外され、再配置・復活せず、脱獄人数にも加算されません。これは新しい囚人システム全体の完成を意味しません。

驚いた際の跳躍・震えは、通知イベントへ演出担当が接続できる構成にしています。高さ・時間・状態除外までのタイミングが未確定のため、今回具体的なアニメーションは実装していません。

## 4. 暫定接続と未確定事項

- 既存盤面は25マスの段差形状です。7列×5行の全体座標範囲へ対応付け、実在しない位置を除外しています。この盤面サイズ・形状を正式なフィールド仕様として新規確定していません。
- 仮フィールドの初期列3・TOPは、既存の看守表示位置を維持する値です。`PrototypeGuardVisionField.asset` に明示し、正式フィールドの決定後に差し替えます。
- 既存の横向き共有境界で `Line == row` のものを、その実在セルの前面柵として扱います。縦向き柵と実在セルを伴わない外側境界は、正式な対応規則が未提供のため視界入力に使用しません。
- 未設定・不正なフィールドや現在のプロトタイプと不整合な寸法は、配置・視界を推測せず仕様待機にします。
- 本番スキルカード・山札構成・各カードの正確な割り込み条件と優先順位は未提供です。既定対局はスキルカード未設定による仕様待機が残ります。
- 発見後の脱落判定の正確なタイミングや全員脱落処理の改訂は本実装対象ではありません。囚人・ゲームフロー仕様との照合が別途必要です。
- 6列×5行は視界の説明・単体テスト例としてのみ使用しています。

## 5. 表示と開発用確認

看守の現在位置は既存の盤面表示を再利用して常時示します。TOP/BOTTOMの切り替え時は位置表示を即時更新し、反転アニメーションは追加していません。シーンカメラは両側の看守を収めるため、Z位置3.2・正投影サイズ6へ調整しています。

通常プレイ中に視界を色分けしません。Play中に `GuardVisionDebugDisplay` のInspectorで `Show Vision` を有効化し、コンテキストメニュー `Refresh Vision Debug Snapshot` を実行してSceneビューのGizmosを確認します。看守・前面柵・最終視界を描きます。

デバッグは明示更新したスナップショットを描画します。反転・柵変更後は再更新してください。描画や更新操作は監視抽選・囚人発見を起動せず、通常プレイヤーターンでの毎フレーム視界計算も追加しません。色はInspectorで調整可能な開発用表示値です。

保存した `Logs/SYS-VISION-001-top.png` と `Logs/SYS-VISION-001-bottom.png` を表示し、両側の看守ラベルと、看守が1人だけ表示されること、通常画面に視界の色分けがないことを確認しました。デバッグのデータ・明示更新・キャッシュ保持は自動検証済みですが、SceneビューでのGizmos描画の手操作確認は未実施です。

## 6. 検証範囲と結果

正本SYS-VISION-001 v1.0第13章の全文は提供されていません。本検証は、提示済みの式・具体例と、今回の実装指示書9章に列挙された12の重点条件を対象にします。未提供の第13章全ケースを網羅したとは扱いません。

| 指示書9章の重点条件 | 検証箇所・結果 |
|---|---|
| 1. TOP/BOTTOMの反転 | 純C#：基本視界の上下鏡像と繰り返し反転を検証、成功。 |
| 2. 3マスごとの拡張 | 純C#：距離1～15の幅を固定の期待値と照合、成功。 |
| 3. 盤面外の切り捨て | 純C#：左右端・1列盤面を検証、成功。 |
| 4. 基本視界外の柵 | 純C#：`(1,1)` の柵が深い `(3,1)`・`(4,1)` を隠さない例、成功。 |
| 5. TOPは柵のマスも遮蔽 | 純C#：`(3,1)` と `(4,1)` の遮蔽を検証、成功。 |
| 6. BOTTOMは柵自身を遮蔽しない | 純C#：柵の行を残し上側だけを遮蔽、成功。 |
| 7. 同列の複数柵 | 純C#：最近傍の選択と破壊後の次の有効柵を検証、成功。 |
| 8. 設置・破壊後の最新状態 | 純C#と実盤面：柵の設置・破壊を次の計算へ反映。監視割り込み中の変更も最終視界へ反映、成功。 |
| 9. 反転を複数回使用 | 純C#と実盤面：公開 `Flip()` を繰り返して列不変・方向の持続・同一看守表示の更新を検証、成功。本番の反転カードは未提供のため、公開APIと明示的なテストフィクスチャーを対象とした。 |
| 10. 見とるの場合のみ発見 | 実Unity：見とるの最終確定後に視界を1回だけ計算。見とらんでは視界計算・発見を実行しないことを検証、成功。 |
| 11. 複数囚人の同時発見 | 実Unity：複数所有者の全対象を変更不可のリストで一括通知し、全対象が盤上にいる段階で囚人サービスへ渡すことを検証、成功。 |
| 12. 監視無効化時に発見しない | 実Unity：明示的な無効化カードのフィクスチャーを実使用し、視界計算・発見・除外なしを検証。資格のないカードは初回・子イベントへの割り込みともに拒否、抽選・履歴は1回のまま、成功。 |

追加で、集合の重複排除、柵入力の列挙順不変、呼び出し元入力を変更しないこと、返却済み集合の変更が次回計算に影響しないこと、不正寸法・不正位置・未定義方向の拒否も検証しました。

- **純C#単体：68 assertions PASS。** Unity `6000.6.6f1` 同梱Roslyn、4.8参照アセンブリでコンパイルし、同梱Monoで実行。
- **Unity実シーン：1824 assertions PASS。** 純計算68件、今回の視界統合、既存ターン・監視v1.2・山札・履歴UI・盤面回帰を含みます。ログは `Logs/SYS-VISION-001-validation.txt` と `Logs/SYS-VISION-001-unity.log` です。
- 初期フィールドの拒否・初期値取得、反転と表示、最新の柵、割り込み解決前の視界計算なし、解決後の単一スナップショット、全対象の一括通知、監視無効化、デバッグの明示更新とキャッシュ保持を実シーンで確認しました。
- TOP/BOTTOMの保存画像による表示確認は成功。Scene Gizmosの実描画の手操作、本番の個別カードと製品版の通し対局、オンライン対戦は未検証です。
- Gameビュー設定の復元処理で検出したインデックス不備を修正し、最終実シーン検証を再実行しました。1824項目の成功と、終了時の `Invalid index` がないことを確認しています。
- 過去の監視v1.2の1707検証は、視界を追加する前の結果です。今回の再検証結果と区別します。

### 再実行方法

Unity `6000.6.6f1` の実シーン検証は次の入口を使います。非同期に検証し、結果・画像を保存して自動終了するため、`RunVision` には `-quit` を付けません。

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe' -batchmode -projectPath C:\DevGame\TOKAI2 -executeMethod GuardValidationBatch.RunVision -logFile Logs/SYS-VISION-001-unity.log
```

試作フィールドの参照を新しく設定する場合は、Playを停止して `Tools > Daidatsugoku > Configure Prototype Guard Vision` を実行します。既に設定済みの正式なフィールドを、この試作設定で置換する運用にはしません。

### Windows64ビルド・Player起動結果

- Windows64 Developmentビルド：**成功、エラー0・警告496件、13.15秒**。ビルド入口は `GuardValidationBatch.BuildVisionPlayer`、出力先は `Builds/SYS-VISION-001/TOKAI2.exe`、要約は `Logs/SYS-VISION-001-build.txt` です。ビルド処理には `-quit` を使用します。Unityパッケージ・シェーダー等の警告は未解消です。
- Player起動：非表示・グラフィック有効で6秒実行し、早期終了とゲームスクリプト例外がないことを確認しました。確認後に今回起動した検証用プロセスだけを終了しています。ログは `Logs/SYS-VISION-001-player.log` です。従来同様のD3D12 info queue照会失敗ログは残っています。
- Unityがビルド時に自動更新した描画設定3ファイルは、今回の変更と無関係なため除外しています。

## 7. 完了判定と残件

純計算・看守状態・最新フィールド接続・監視前割り込みの境界・発見通知・開発用表示を実装し、実装済み範囲の自動チェック、上下位置の描画確認、ビルドと起動確認が成功しました。提供された指示書に基づく実装範囲の作業は完了しています。

正本第13章全文、本番フィールドと柵データ対応、個別カード効果・タイミング、囚人発見演出のパラメータ、脱落処理の改訂は引き続き確認事項です。これらを独自の正式ルールとして補っていません。
