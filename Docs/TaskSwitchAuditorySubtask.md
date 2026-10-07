# 高音・低音への足ペダル応答

`TaskSwitchExperimentManager` の Inspector に **Auditory Secondary Task / 聴覚サブタスク** を追加しています。**Enable Auditory Subtask** で有効・無効を選びます。既定はOFFです。シーンへの別コンポーネント追加や音声ファイルの割当は不要です。実験開始時に2種類の正弦波と専用の2D AudioSourceを生成します。既存のExperimentStatus(Text)は変更しません。

| 設定 | 既定値 / 動作 |
| --- | --- |
| Pedal Axis Name | `TaskSwitchPedal`（専用Input Manager軸） |
| 高音 / 低音 | 1000 Hz / 500 Hz |
| 反応の対応 | 高音＝右＋、低音＝左− |
| High Tone Uses Positive Pedal | ON。OFFで対応を反転 |
| Invert Pedal Axis | OFF。機器の軸方向が逆の場合にON |
| Press / Release Threshold | 0.5 / 0.2。中立に戻ってから次の押下を受理 |
| Tone Duration / Volume | 0.2秒 / 0.2（AudioSourceの相対音量） |
| Minimum / Maximum Interval | 3～5秒、音の予定開始時刻から次の開始時刻までを一様乱数で選択 |
| Response Timeout | 2秒 |
| Minimum Valid Reaction | 0.1秒。これ未満はTooEarlyとして分離 |
| Random Seed | 0は有効化ごとに生成。非0は再現用固定シード |

音種は各提示で独立に確率1/2で選び、交互提示・総数の厳密な均等化はしません。設定はサブタスク有効化時にコピーし、途中の数値変更はOFF→ONまたは次の実験で適用します。有効/無効の変更は実行中にも反映されます。初回提示と全体一時停止からの再開は3～5秒後です。フレーム遅延で本来の次回提示時刻を過ぎた場合は現在のDSP時刻から最低0.1秒後に予約し、過去の音をまとめて再生しません。

## ペダルの接続

新設の `TaskSwitchPedal` は機器未指定のため、初期設定では検証用の **`]` キー＝＋、`[` キー＝−** としています。既存のクレーン用ジョイスティックをペダルとして推測して割り当てることはしません。

実機では **Edit → Project Settings → Input Manager → Axes → TaskSwitchPedal** で以下を設定してください。

1. **Type = Joystick Axis**、**Joy Num** と **Axis** を使用するペダルのデバイス・軸番号に合わせる。
2. **Gravity = 0 / Sensitivity = 1 / Snap = OFF** とし、Deadは機器のノイズに応じて設定する（初期値0.001）。キーボード割当は不要なら空欄にする。
3. 未押下＝0、右ペダル＝＋、左ペダル＝−になることを確認する。軸方向が逆ならInput ManagerのInvertかサブタスクのInvert Pedal Axisのいずれか一方で反転する。
4. 必要ならPress Threshold/Release Thresholdを実測範囲に合わせる。ReleaseはPressより小さくする。

この実装は**1本の符号付き軸・中立0**を前提とします。左右が別々の軸、未押下が−1などの機器には、機器側または別の入力変換でこの形式へ合わせる必要があります。両ペダル同時押下は単一軸の値だけから識別できません。機器の型番・実際の軸番号は今回固定していません。

## 主タスク・一時停止との関係

- クレーン切替、確認待ち、カウントダウン、主操作の入力ロック中も音への応答を受理します。確認ボタンや電流・ジョイスティック操作を反応入力として使用しません。ペダル操作はクレーンを動かしません。
- `ExperimentPauseManager` の全体一時停止では予約/再生音を即時停止し、その提示を中断または提示前取消として確定します。途中の待ち時間を停止解除後まで持ち越さず、新しく提示を予約します。
- Simulatorの操作無効化も停止条件です。主タスクの完了/退出、Managerの無効化/破棄、再実験、InspectorでのOFFでも未確定提示を確定して音を止めます。
- CSVのStopでは未確定提示を記録してからファイルを閉じ、その後のサブタスクを止めます。CSV開始または新しい実験で再開します。Debug開始では音のサブタスクを実行できますが、CSVは出力されません。
- 起動直後/一時停止解除直後は中立を一度観測するまで押下を受理しません。押しっぱなし、＋から中立を経ずに−へ変えた入力は新しい押下にしません。
- 提示後の最初の有効な押下で判定を確定します。間違えてから正しい側を踏んでも正解に書き換えません。100 ms未満の反応も最初の反応として確定し、TooEarlyとして分けます。

## CSV（schema_version = 3）

既存7ファイルに以下の2ファイルを追加します。サブタスクOFFでもヘッダーを作成し、提示/応答の行は書きません。

| ファイル | 内容・分析時の用途 |
| --- | --- |
| `auditory_subtask.csv` | 全イベント。SubtaskStarted/Stopped/Paused/Resumed/Error、StimulusScheduled、StimulusOnsetObserved、PedalPressed、TrialFinished。音提示前・提示後の余分な押下も確認する |
| `auditory_trials.csv` | TrialFinishedのみ、提示予約1回につき1行。反応時間、正誤、Miss率などの集計に使用。予約取消も含むためpresented列で実提示と区別する |

`session.csv` に有効設定と設定JSON、`input.csv` にサブタスクの有効/動作状態・軸名・生ペダル値・次の押下を受理可能かを追加します。停止中・無効時のinputの生ペダル値は空欄です。ペダルイベントはManagerのUpdateごとに検出し、通常の0.05秒サンプル周期とは独立して記録します。

共通キーは `session_id, participant_id, block_id, experiment_run_index`。提示はこれに `trial_index` を加えて識別します。trial_indexは実験内で増加し、有効化を切り替えても同じ番号に戻りません。実験再開始時にリセットします。`activation_index` はサブタスク有効化の回数です。提示と無関係なイベントのtrial_indexは空欄です。

各行には以下を保存します。

- 現在の共通実時間/シミュレーション時間、UTC、フレーム、切替番号、選択クレーン、実験状態、主入力ロック・全体一時停止・操作有効状態・アプリフォーカス。
- 音種、周波数、予定DSP開始時刻、共通時計へ換算した推定開始時刻、開始を観測したフレーム時刻、その観測遅延。
- 応答の符号、未反転の生ペダル値、正誤、結果、反応時間、応答フレーム時刻、提示時の入力保持。
- 提示開始を観測した時点の切替番号/クレーン/実験状態/フェーズ/ステップ、およびイベント時点のフェーズ/ステップ。提示後に切り替わった場合も両方を残す。
- 実際に使用した軸・対応・閾値・音量・タイミング・乱数シードと設定JSON、中断などの理由。

| outcome | 意味 / correct列 |
| --- | --- |
| Correct | 最初の反応が指定側、時間範囲内 / 1 |
| Incorrect | 最初の反応が反対側、時間範囲内 / 0 |
| TooEarly | 最小有効反応時間未満 / 空欄 |
| Miss | 応答期限までに押下なし / 空欄、反応時間も空欄 |
| Interrupted | 提示後に停止・中断 / 空欄、反応時間も空欄 |
| CancelledBeforeOnset | 予約後、開始前に停止 / 空欄、presented=0 |
| FalseAlarm | 反応対象となる音がないタイミングの押下。イベント履歴だけに保存 / 空欄、trial_indexも空欄 |
| LateResponse | 更新フレームで期限超過と押下を同時に観測。提示はMissとして確定し、この入力は同じtrial_indexの追加イベントで記録 / 空欄 |

最小反応時間以上、応答期限以下が有効範囲です。Missは締切を超えた最初のフレームで確定します。Miss確定後の後続フレームの入力はFalseAlarmです。中断・取消はMissや不正解に混ぜず、実提示後に確定したCorrect/Incorrect/TooEarly/Missを分析対象としてください。Correctのみの反応時間分布を用いる場合も、除外数・誤反応率・Miss率を併記してください。

## 時計・検証の範囲

音は `AudioSource.PlayScheduled` と `AudioSettings.dspTime` で予約します。reaction_time_sは**入力を観測したフレームのDSP時刻−予定開始DSP時刻**です。onset_estimated_real_sは予約時のDSP/実時間の対応から換算した推定値、onset_observed_real_sとonsetの作業状態は予定開始を過ぎた最初の更新フレームでの観測です。サウンド出力やペダル接点を外部計測した時刻ではなく、実機の音響遅延・入力遅延は校正していません。提示開始と同一フレームの作業切替は、そのフレームの更新順序にも依存します。

反応検出は描画フレーム単位で、フレーム間の短い押下を完全には捕捉できません。長いフレーム遅延はonset_observation_lag_sを確認して扱ってください。ON/OFFで操作ロックやExperimentStatusを変更しません。設定の不整合・入力読取/音生成の例外はSubtaskErrorを記録し、サブタスクを停止します。修正後は実験またはCSVを再開始してください。

`Tests/TaskSwitchLogging/run.sh` は実際の判定・音生成/予約コード・CSV実装を代替Unity APIで実行します。正誤、早過ぎる入力、無反応、遅延入力、入力保持、中立復帰、軸反転、設定検証、音のフェード/長さ、早い反応で音が切れないこと、クレーン入力ロック中の応答、一時停止・再開、CSV停止、OFFを検証します。CSVの列数、結果行と元イベントの一致、欠測空欄、共通時計、提示時と応答時の切替状態もPythonで確認します。Unity Editor・実際のスピーカー・足ペダルでの実行は別途確認が必要です。
