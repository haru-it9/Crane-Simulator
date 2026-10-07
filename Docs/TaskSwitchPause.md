# Pause / Startによる状態を保持した一時停止

Pauseで進行と入力を止め、Startで止めた状態の続きから再開します。切替先の作業中でも選択クレーン・切替番号・作業フェーズ・詳細ステップ・目標・位置・保持板・重量・電流を初期化しません。Sourceを切替のために個別停止している場合、その個別停止状態も維持します。全体Pauseの解除でSourceまで誤って再開することはありません。

## ボタンと初回開始

- `ExperimentPauseManager` に登録したMulti/Single/Task SwitchのPauseボタンは同じ全体停止状態を切り替えます。ResumeはTaskSwitchExperimentManager.StartExperimentを毎回呼びません。
- Simulator開始済みでTask Switchモード、実験状態がIdleのときだけ、最初のStartで実験を初期化します。それ以降のStartは再開です。CompletedもStartだけでは再実験しません。新しい実験は明示的にStartExperimentを実行してください。
- シーンの既存PauseボタンにはStartExperimentの永続OnClickが1件あります。PauseManagerは登録したボタン上のそのコールを実行時にOFFへ変更し、自身のTogglePauseを1回だけ登録します。既存シーンの手動修正は不要です。別途作成するPauseボタンもPauseManagerへ登録してください。実験開始専用ボタンはPauseボタンとして登録しません。
- Task Switch初回開始のSimulatorStartManager/TaskSwitchExperimentManager参照はInspectorで指定可能です。未設定ならシーンから取得します。Simulatorの初期Start前は再開を受け付けません（Wait For Simulator StartがONの場合）。

## 保持する進行

| 対象 | Pause / Startの動作 |
| --- | --- |
| クレーンと保持板 | Time.timeScale=0で物理を停止。移動・速度変更・選択・電流入力・板解除の入力を抑止し、状態を保持 |
| 作業監視 | 詳細ステップ判定、安定時間、重量逸脱判定を停止。Unscaled Time設定でも進まない |
| サイクル | 現在フェーズ・保留中の次フェーズ・フェーズ間待ち時間を維持。PauseCycle/ResumeCycleによる別の個別停止状態を書き換えない |
| 切替 | ランダム待ち、カウントダウン、確認待ち、確認ボタン解放待ちを維持。Pause中の要求・確認は受け付けない |
| ストック・模式図 | 入荷時間・持出し判定・模式図の更新を停止。板生成後の検出猶予時間もPauseを除いた時計で保持 |
| 音サブタスク | AudioListener.pauseで再生位置・予定音・DSP時計を停止。同じ提示の応答待ちを再開し、Pause時間をRTに含めない |
| CSV | 壁時計で記録を継続。停止区間・停止中の状態・入力の生値を識別可能にする |

予定時刻を持つ切替タイマーと表示用フィードバックは `ExperimentPauseManager.ActiveRealtime` を使います。この時計は全体Pause中だけ止まり、Startで同じ値から進みます。CSVのreal_elapsed_sとイベントの実時間は引き続き壁時計です。Pause前にTime.timeScaleが1以外だった場合も元の値へ戻します。AudioListener.pauseの以前の値も保持します。ExperimentStatus(Text)の表示内容は追加・変更しません。

## CSV（schema_version = 5）

Task SwitchセッションはPause中もcrane_state/input/gazeを壁時計で採取します。`global_paused=1` が停止中、`pause_interval_index` は停止区間番号で、稼働中は空欄です。events.csvにはGlobalPauseStarted/GlobalPauseEndedを即時記録し、共通時計で停止境界を照合できます。

`pause_intervals.csv` は区間ごとに1行を保存します。

- 共通のsession_id/participant_id/block_id/experiment_run_indexとpause_interval_index。
- start_s/end_s/duration_s：記録開始からの壁時計の開始・終了・継続時間。
- start_observed/end_observed：実際のPause/Start境界を記録できたか。停止中にCSVを始めた場合のstart_observedは0、記録を止めた時点でも停止中ならend_observedは0。
- outcome：通常再開はResumed、停止中のCSV終了はLoggingStoppedWhilePaused。後者のend_sは再開時刻ではなく記録終了時刻。
- 開始/終了時の切替番号・選択クレーン・実験状態・フェーズ・ステップ。

記録を停止中に始めた場合は、実際のPause開始より前の時間を推測せず、記録開始を区間の始点とします。GlobalPauseAtLoggingStartをeventsに残します。中途半端な区間を完全なPauseとして扱わないよう、境界観測フラグを確認してください。

既存switch_summaryの各所要時間は壁時計のためPauseを含みます。稼働時間だけを求める場合は、分析対象区間とpause_intervalsの重複時間を差し引きます。聴覚サブタスクにはpause_count/paused_duration_sとwall_reaction_time_sを追加し、reaction_time_sをPauseを除いた時間として維持します。

## 検証

`Tests/TaskSwitchLogging/run.sh` は実際のPauseManager・聴覚サブタスク・CSVを代替Unity APIで実行します。旧OnClickからの再初期化抑止、初回Startのみの初期化、切替元/切替先/確認待ち/カウントダウン状態の保持、Pause中の時計凍結、時間倍率と音の復元、同じ提示の再開、Pause区間の時刻・継続時間、途中記録開始・停止を検証します。Unityの物理と実デバイスの動作は代替APIでは検証できないため、Unity Editorでの実験シーン確認は別途必要です。

視覚サブタスクも青/赤の状態、次回提示までの待ち、同じ提示への回答待ちを保持します。時計はActiveRealtimeを使い、Pause時間を反応時間から除外します。詳細は [TaskSwitchVisualSubtask.md](TaskSwitchVisualSubtask.md) を参照してください。
