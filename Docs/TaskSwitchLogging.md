# TaskSwitchExperiment CSV（schema_version = 2）

Task SwitchモードでStartを押すと、保存先の中に `<入力ファイル名>_<UTC時刻>_<UUID>/` を作成します。同じ名前で開始しても以前の実験を上書きしません。1セッションの標準出力は次の7ファイルです。通常の自動管理モードは既存のLoggerを使用します。Debug開始はCSVを出力しません。

| ファイル | まとめ方と主な内容 |
| --- | --- |
| `session.csv` | 記録開始・実験開始・記録終了時の設定。被験者・ブロックID、Unity/アプリ版、任意のソース版、切替方式、クレーン条件、サイクル数、カウントダウン、スケジュールCSV全文、入力設定、AOI設定 |
| `events.csv` | 状態遷移、要求、確認、作業停止、操作ロック解除、初回有効入力、ステップ/フェーズ/サイクル、保持電流、板の吸着/解除、重量逸脱、巻き戻し。イベント対象の位置・重量・電流も記録 |
| `crane_state.csv` | 同じサンプル時刻でクレーンごとに1行。world XYZ、目標world XZ、符号付き/絶対誤差、表示座標、重量、目標重量、保持板数、電流、保持能力、安全保持・解除閾値、フェーズ/ステップ/経過時間/安定時間/ヒステリシス、ステップ条件JSON |
| `input.csv` | 生のジョイスティック軸、実際に使用している電流スライダー軸名・値・要求電流、処理後の要求移動指令、最後に受理した移動指令とフレーム、確認/解除ボタン、実際の制御電流 |
| `gaze.csv` | 接続・フォーカス・有効性、viewport、raw screen、game screen、画面内に丸めた座標、画面寸法、任意のAOI、AOIの画面上の四隅と有効状態 |
| `switch_summary.csv` | 切替1回につき1行。各境界時刻、待ち時間、再開時間、中断時のフェーズ/ステップ/サイクル/経過時間/位置ラッチ/保持枚数、解除時の入力保持、エラー数、完了/未完了 |
| `cycle_summary.csv` | クレーン・サイクル実行ごとに1行。開始/終了、経過時間、監視・操作可能・全体一時停止の時間、完了ステップ数、重量逸脱・電流不足解除・巻き戻しの回数、完了/未完了 |

## 識別子・時計・単位

- 全ファイルに `session_id, participant_id, block_id, experiment_run_index` を付けます。IDはセッション開始時に固定します。`TaskSwitchExperimentCsvLogger` のInspectorで Participant Id / Block Id を開始前に入力してください。空欄は未設定を意味し、ファイル名から推測しません。Source Revisionも任意設定で、未設定なら空欄です。
- 記録開始からの共通時計を使用します。`real_elapsed_s` は倍精度の実時間、`simulation_elapsed_s` はTime.timeの経過時間です。UTCは照合用です。全体一時停止中も実時間で採取し、`global_paused` を記録します。
- `experiment_run_index` は同じ記録中の再実験を区別します。`switch_index` は実験内の切替番号です。クレーン番号は0始まりです。
- `cycle_instance_id` はセッション内で一意です。切替先の作業が毎回Cycle 1から始まっても衝突しません。`cycle_number` はその実行内のサイクル番号です。
- 時系列3ファイルは `sample_index` を共有し、`crane_state.csv` はクレーン番号と組み合わせて結合します。既定は0.05秒間隔、実際の採取はフレームレートに制限されます。欠けた時刻のサンプルは補間・複製しません。
- 位置はm、重量はkg、電流はA、時間は秒です。数値はInvariantCulture、小数6桁で出力します。未取得・未発生の数値は空欄、boolは1/0です。空欄を0に置換しないでください。
- `world_*` と `target_world_*` はUnity座標です。表示換算は `display_x = world_x + 20`、`display_z = 250 - world_z` です。生の保持重量と、選択中のUIが表示する `display_weight_ton` を別々に記録します。非選択クレーンのUI値は空欄です。
- `x_highlighted/z_highlighted` は実際のUI判定、`x_within_display_tolerance/z_within_display_tolerance` は最新位置からの範囲判定です。垂直作業では表示色の保持があるため、両者は一致しない場合があります。`achievement_frame` で表示更新フレームを確認できます。
- `pickup_weight_error_kg` は最新保持重量−吸着目標重量です。配置後の残重量目標は別列です。ステップの許容値は `step_configuration_json` に保存します。

## 操作開始と切替時間

`ConfirmationPressed` / `SourceReturnConfirmationPressed` は確認操作、`TargetOperationStarted` / `SourceOperationResumed` は監視ロジックの開始です。確認ボタンを離して実入力を許可した境界は `TargetInputUnlocked` / `SourceInputUnlocked` です。これらを操作開始と同一視しません。

`FirstEffectiveTargetInput` / `FirstEffectiveSourceInput` は、解除後に実際の制御経路で受理した最初の入力です。対象はロックやデッドゾーンを通過した移動指令、電流制御の変化（0.01A以上または安全保持解除）、通常操作での板の吸着/解除です。移動限界・保護機構で実際に動かなかった場合も「受理した指令」として記録します。物理的な移動量は `crane_state.csv` の座標変化から確認してください。

`MovementCommandChanged` は移動指令の変化（停止指令も含む）をイベントとして残します。電流入力の変化もイベント化します。時系列の採取間隔より短い操作を確認する際は、これらを併用してください。

| 集計列 | 算出する境界 |
| --- | --- |
| `request_to_suspend_s` | 要求 → 切替元の作業停止（Countdown/PhaseBoundaryでの待機を含む） |
| `request_to_target_input_s` | 要求 → 切替先の初回有効入力 |
| `suspend_to_source_input_s` | 切替元の作業停止 → 切替元の初回有効入力 |
| `source_display_to_confirm_s` | 切替元の画面復帰 → 復帰確認 |
| `source_confirm_to_unlock_s` | 復帰確認 → ボタンを離して入力解除 |
| `source_unlock_to_input_s` | 入力解除 → 切替元の初回有効入力 |
| `target_confirm_to_unlock_s` | 切替先の確認 → 入力解除 |
| `target_unlock_to_input_s` | 入力解除 → 切替先の初回有効入力 |

両端が発生しなければ時間は空欄です。解除時にジョイスティックや電流スライダーを保持している場合は `*_input_held_at_unlock` を1にします。この場合の初回入力時間には保持済みの入力が含まれます。新たに手を動かした反応時間としてそのまま解釈しないでください。

切替先の単一フェーズ完了と1サイクル完了（通常・フェーズ経由・フォールバック）を同じ完了境界として扱い、重複通知では最初の時刻を保持します。切替集計は切替元の初回有効入力時に確定します。入力がなければ次の要求・再実験・記録終了時に確定し、論理的な確認完了と実入力の有無を別に記録します。`CompletedWithoutSourceInput` は「確認完了、復帰後の入力なし」です。

## エラーとサイクル集計

`BoardDetachedInsufficientCurrent` は電流不足による板の解除、`BoardsDetached` は全板解除です。後者をすべて落下と判定しないでください。`PickupWeightInvalidated` は保持重量の条件逸脱、`PickupWeightRollback` は作業をLoadAcquisitionへ戻した通知です。`invalidation_*` 列は修復前の重量・誤差・ステップ経過・除去枚数を保存するため、イベント購読順序による再設定の影響を受けません。

サイクルの経過時間は開始から終了までの実時間で、中断を含みます。`monitoring_s` は監視中かつサイクル稼働中、`control_available_s` はそのうち選択中・入力許可・操作有効の時間です。「実際に動かしていた時間」とは異なります。

監視/操作可能/一時停止の時間は、書き出した状態サンプルを右端値として区間積分します。最初の区間はサイクル開始時刻で打ち切ります。サイクル終了までの最後の未採取区間は含めず、`duration_sampled_until_s` に最終採取時刻を残します。境界で最大1採取間隔程度（フレーム停止時はそれ以上）の誤差があり、これらの時間は近似値です。

`CycleStarted` / `CycleSummaryClosed` と一意なサイクルIDにより、生ログから境界と派生値を再構成できます。初期フェーズがMove1以外の実行は `first_phase` で識別し、通常のフルサイクルと分けて解析してください。

## 視線・質問紙

既存のTobii取得APIを使い、raw/viewport/画面座標・有効性・接続・フォーカスを保持します。これはUnity側のポーリング時刻であり、視線装置の独立したハードウェア時刻や新規サンプル保証ではありません。

AOIはInspectorのGaze Areasにラベル・RectTransform・Canvas Cameraを設定します。上から優先し、非表示の領域は判定しません。Overlay CanvasのCameraは空欄で構いません。未設定・領域外・視線無効は `Unknown` です。既存シーンにはAOIを自動割当していません。複数画面ではTobiiのViewportと対象Canvasが同じ画面座標系になることを実機で確認してください。`aoi_layout_json` は各採取時の領域形状を保存します。

NASA-TLX等の主観評価を集める場合は、別の質問紙データに `session_id, participant_id, block_id, experiment_run_index` を付けて結合してください。未取得の主観値をシミュレータから生成しません。

## 再集計と検証

```bash
python3 Tools/rebuild_task_switch_summaries.py /path/to/session
```

`events.csv` と `crane_state.csv` から派生集計を再計算し、元の集計を上書きせず `switch_summary_rebuilt.csv` / `cycle_summary_rebuilt.csv` に保存します。元の集計ファイルのヘッダーをスキーマとして参照し、内容は参照しません。小数6桁への丸めで微小な差が生じる場合があります。強制終了で最終終了イベントがないサイクルはこのツールでは確定行になりません。通常のStop/終了で記録を閉じてください。

```bash
Tests/TaskSwitchLogging/run.sh
```

Mono（mcs/mono）とPython 3が必要です。Unity代替APIを使用して実際のLogger・CSV書き込み・集計処理を実行し、7ファイルの列数、共通時計/ID、入力ゲート、重複完了、途中終了、再開始、視線の有効/無効、再集計との全列一致を検証します。代替APIはTests配下のみです。Unityシーン・実機ジョイスティック・Tobiiの動作確認は別途必要です。ExperimentStatusのTextや安全保持解除の70A設定はこのログ変更では変更しません。
