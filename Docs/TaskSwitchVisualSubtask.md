# 左右の色変化へのペダル応答

TaskSwitchExperimentManagerのInspectorで **Secondary Task Mode** を選びます。

| 選択 | 動作 |
| --- | --- |
| None | サブタスクなし。左右の表示を非表示にする |
| Auditory | 従来の高音/低音。左右の表示は非表示 |
| Visual | 作業情報パネルの左右に青い表示を出し、片側が赤になったら同じ側のペダルで回答 |

右の赤＝右ペダル（＋）、左の赤＝左ペダル（−）です。左右は独立に確率1/2で選び、常に交互にはしません。最初の押下で判定を確定し、両方を青に戻します。回答なしで期限を超えた場合も青に戻します。両側が同時に赤になる提示はありません。誤った側を踏んだ後に正しい側を踏んでも、最初の判定を書き換えません。

既存のシーンは音が有効だったため、その設定をAuditoryとして維持しています。Visualへ変更するだけで色のパターンを利用できます。モード変更は実行中にも反映し、前の未確定提示をInterruptedまたはCancelledBeforeOnsetとして記録します。音と色は同時に実行しません。新規Managerの既定はNoneです。旧Enable Auditory Subtaskは移行用に非表示で残しています。

## UI配置と設定

RemoteManagementScene(union)の `TaskSwitchUIRoot/TouchPanel/TextandButton` に **VisualSubtaskLeft / VisualSubtaskRight** の2つのImageを配置し、ManagerのVisual Left/Right Indicatorに割り当てています。作業情報の左右外側に配置し、既存情報・確認ボタンを覆わない位置としています。ImageのRaycastは無効で、クリック操作を遮りません。位置・大きさは各RectTransformから変更できます。通常時は青、回答待ちは片側だけ赤です。実験停止/退出/完了、記録停止、Manager無効化時は両方を非表示にします。別シーンで利用する場合は作業情報UIに2つのImageを置き、左右の参照を割り当ててください。

| Visual Settings | 既定値 / 動作 |
| --- | --- |
| Pedal Axis Name | `TaskSwitchPedal`。右＋ / 左−、中立0の1本の軸 |
| Invert Pedal Axis | OFF。機器の軸方向が逆の場合にON |
| Press / Release Threshold | 0.5 / 0.2。中立を観測してから次の押下を受理 |
| Minimum / Maximum Interval | 3～5秒。初回は開始からの待ち、その後は直前の実際の色変更からの間隔を一様乱数で選択 |
| Response Timeout | 色が赤になってから2秒 |
| Minimum Valid Reaction | 0.1秒。これ未満はTooEarly |
| Random Seed | 0は有効化ごとに生成、非0は左右/間隔を再現する固定シード |

設定は有効化時にコピーします。実行中に数値を変更した場合は、Noneを経由してVisualへ戻すか、実験を再開始して適用してください。長いフレーム停止で次の予定時刻を過ぎた場合は現在時刻から最低0.1秒後に予約し、過去の提示をまとめて出しません。UIの未割当・非表示、軸設定やタイミングの不整合はSubtaskErrorで記録して視覚タスクを停止します。主作業の状態は変更しません。修正後は実験またはCSVを再開始してください。

ペダルの実機設定は [TaskSwitchAuditorySubtask.md](TaskSwitchAuditorySubtask.md) の「ペダルの接続」と共通です。未設定時の検証用入力は `]`＝右＋、`[`＝左−です。機器の軸番号は自動推定しません。

## Pauseと作業切替

クレーン切替中、確認待ち、カウントダウン、主操作の入力ロック中もペダル回答を受理します。確認ボタンは回答に使用しません。切替先の作業中にも左右の表示を継続します。

全体Pauseでは、現在の色・未提示の残り待ち・提示済みの残り回答時間を保持します。停止中の入力は判定しません。Startで同じ提示から再開し、再開後に中立を観測してから次の押下を受理します。赤のままPauseしても新しい提示に置き換えません。開始前にPauseした場合も、残り待ちを再開します。停止時間は反応時間に含めません。

## CSV（schema_version = 5）

保存先とセッション識別は [TaskSwitchLogging.md](TaskSwitchLogging.md) と共通です。既定の保存先は `C:\Users\harui\GitHub\Crane-Simulator\Assets\ExperimentData\` のセッションフォルダです。

| ファイル | 内容 |
| --- | --- |
| visual_subtask.csv | 提示予約、色変更、ペダル押下、Pause/再開、開始/停止/エラー、結果の全イベント |
| visual_trials.csv | TrialFinishedのみ、提示予約1回につき1行。反応時間や正答/誤答/Missの集計用 |
| session.csv | Secondary Task Mode、視覚有効フラグ、設定JSON。モードの開始/停止/エラー時にも設定行を記録 |
| input.csv | 選択モード、視覚有効/動作状態、左右の赤フラグ、共通ペダル軸名/生値/中立復帰状態。Pause中の生値は空欄 |

計12ファイルを作成します。未選択の種類のイベント/試行ファイルはヘッダーのみです。音の記録は従来どおりauditory_subtask.csv / auditory_trials.csvへ保存します。既存の切替/サイクル集計の列と再集計ツールは維持します。

視覚イベントは `secondary_task_mode=Visual`、`stimulus_side=Left/Right`、対応する `expected_sign=-1/+1` を保存します。`response_sign`、`outcome`、`correct`、`reaction_time_s`、入力保持、軸・閾値・乱数シード・設定JSON、提示時/回答時の選択クレーン・切替番号・状態・フェーズ・ステップを記録します。キーはsession_id、experiment_run_index、種類（Visual）、trial_indexです。モードを切り替えても同じ実験内でVisualのtrial_indexを再利用しません。

`reaction_time_s` は **Imageを赤に変更したフレームから押下を観測したフレームまでのActiveRealtime差** です。フレーム遅延で予定時刻を過ぎてから赤になった場合も、実際の色変更から測ります。`scheduled_onset_estimated_real_s` と `onset_observed_real_s` は記録開始からの共通壁時計、`onset_frame_delay_s` は実際の色変更と予定の差です。`active_clock_s`、`scheduled_onset_active_clock_s`、`actual_onset_active_clock_s` はUnity起動からの実時間から全体Pause累計を引いた時計で、CSV開始からの時間ではありません。音用DSP時計とは比較しないでください。

`pause_count`、`paused_duration_s`、`wall_reaction_time_s` でPauseをまたいだ試行を識別できます。色変更後のPauseは壁時計の反応時間に含み、ActiveRealtimeの反応時間から除きます。色変更前のPauseは待ち時間を後ろへずらすため、壁時計の反応時間にも含みません。left_red/right_redはイベント処理時の色です。StimulusOnsetObservedは片側が1、TrialFinishedは両側0で青へ復帰した状態を記録します。

Correct/Incorrect/TooEarly/Miss/Interrupted/CancelledBeforeOnset/FalseAlarm/LateResponseの意味と欠測空欄は音と共通です。未提示取消・中断はMissに混ぜず、presentedとoutcomeで分析対象を選んでください。

## 検証

`Tests/TaskSwitchLogging/run.sh` で実際の判定・スケジュール・UI色設定・CSV実装を代替Unity APIで実行します。左右とペダル対応、正誤、早すぎる応答、無反応、中立復帰、入力保持、描画フレーム遅延、音声デバイス/DSPに依存しない動作、主操作ロック、Pause前後の同じ提示、記録停止、3択の切替、旧設定の移行を検証します。CSVの列数、元イベントと結果行の一致、作業切替文脈、Pause時間、欠測、シーンの左右参照と配置も確認します。Unity Editorでの描画・実際のディスプレイと足ペダルの動作は未確認です。時刻はUnityのフレーム観測であり、物理的な発光/ペダル接点の外部測定値ではありません。
