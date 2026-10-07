# TaskSwitchExperiment CSV（schema_version = 5）

Task SwitchモードでStartを押すと、保存先の中に `<入力ファイル名>_<UTC時刻>_<UUID>/` を作成します。同じ名前で開始しても以前の実験を上書きしません。1セッションの標準出力は次の12ファイルです。通常の自動管理モードは既存のLoggerを使用します。Debug開始はCSVを出力しません。

既定の保存先は `C:\Users\harui\GitHub\Crane-Simulator\Assets\ExperimentData\` です。スクリプトの初期値とRemoteManagementScene(union)の7つのLogger設定を同じ保存先にしています。Task SwitchのCSVは、その下に作成するセッションフォルダ内に保存します。

| ファイル | まとめ方と主な内容 |
| --- | --- |
| `session.csv` | 記録開始・実験開始・記録終了時の設定。被験者・ブロックID、Unity/アプリ版、任意のソース版、切替方式、クレーン条件、サイクル数、カウントダウン、スケジュールCSV全文、入力設定、AOI設定 |
| `events.csv` | 状態遷移、要求、確認、作業停止、操作ロック解除、初回有効入力、ステップ/フェーズ/サイクル、保持電流、板の吸着/解除、重量逸脱、巻き戻し。イベント対象の位置・重量・電流も記録 |
| `crane_state.csv` | 同じサンプル時刻でクレーンごとに1行。world XYZ、目標world XZ、符号付き/絶対誤差、表示座標、重量、目標重量、保持板数、電流、保持能力、安全保持・解除閾値、フェーズ/ステップ/経過時間/安定時間/ヒステリシス、ステップ条件JSON |
| `input.csv` | 生のジョイスティック軸、実際に使用している電流スライダー軸名・値・要求電流、処理後の要求移動指令、最後に受理した移動指令とフレーム、確認/解除ボタン、実際の制御電流、サブタスク有効/動作状態・ペダル軸名・生値・中立復帰状態 |
| `gaze.csv` | 接続・フォーカス・有効性、viewport、raw screen、game screen、画面内に丸めた座標、画面寸法、任意のAOI、AOIの画面上の四隅と有効状態 |
| `switch_summary.csv` | 切替1回につき1行。各境界時刻、待ち時間、再開時間、中断時のフェーズ/ステップ/サイクル/経過時間/位置ラッチ/保持枚数、解除時の入力保持、エラー数、完了/未完了 |
| `auditory_subtask.csv` | 高音/低音サブタスクの提示予約・提示時刻のフレーム観測・ペダル入力・終了・一時停止。共通時計で切替ログに結合 |
| `auditory_trials.csv` | 音提示1回につき1行。正誤・反応時間・無反応・中断・提示前取消、提示時/応答時の切替状態。無効時はヘッダーのみ |
| `visual_subtask.csv` | 左右の青→赤提示予約・実際の色変更フレーム・ペダル入力・Pause/再開・終了。音用ファイルと分けて記録 |
| `visual_trials.csv` | 色提示予約1回につき1行。左右、正誤、反応時間、Miss、中断/取消、提示時/回答時の作業状態。無効時はヘッダーのみ |
| `pause_intervals.csv` | Pause区間ごとに1行。共通時計の開始/終了・継続時間、境界観測の有無、開始/終了時の選択クレーン・状態・フェーズ・ステップ |
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

Tobii Experienceでは取得できているのにUnityでは取得できない場合は、次の順で確認してください。

1. Package Managerの`com.tobii.gaming.sdk`が正常に読み込まれているか確認します。本プロジェクトは `C:/Users/harui/Downloads/TobiiUnitySDK_5.0.0.3` のローカルSDKを参照します。Consoleの赤いエラー、Missing Script、Missing Prefab、DLL読み込みエラーを先に解消します。
2. シーンの`Tobii`配下にある`Tobii Initializer`が有効で、Missing Prefab/Scriptになっていないか確認します。`TobiiGamingStarter.cs`は現在このシーンには配置されていません。診断目的で手動Startを追加する前に、SDKのInitializerの初期化結果を確認してください。
3. Play後、Tobiiで設定したモニターにGameビューを置いてクリックし、フォーカスを与えます。Consoleを見るためにGameビューからフォーカスを外すと、`Application.isFocused`がfalseになるので、CSVの`app_focused`も併せて確認します。切り分けとして同じモニターでWindowsビルドも試します。
4. 既存の`TobiiDebug`のConsole出力を確認します。1秒ごとに`IsConnected`、`IsValid`、Screen、Viewport、AppFocused、ExperimentPaused、GameScreenに加え、Host、HostInitialized、HWND、ApiInitialized、TrackerEnabled、NativeDllを表示します。実験のPause中も出力します。Unity Editor自体のPauseボタンで実行を停止した場合は出力しません。

SDK 5.0.0.3のソースでは`TobiiHost.Initialize()`は接続を検証せずtrueを返します。Startの結果trueを接続成功と判断しないでください。`IsConnected`はネイティブAPIの`IsTrackerConnected()`です。Hostの`Tick()`はフレーム番号で更新し、Editorのウィンドウハンドル再取得もunscaledDeltaTimeを使うため、実験のtimeScale=0によってこれらの更新が止まる構造ではありません。Settingsの2項目はG2OMのレイヤー・候補保持時間であり、視線装置の接続設定ではありません。

Hostは非ゼロのHWNDに対して`TrackWindow()`を呼びますが、そのbool戻り値を確認せずHostInitializedをtrueにします。このためHostInitialized=trueだけでは登録成功を保証しません。TobiiDebugCheckの診断自体は読み取りのみです。下記の自動選択コンポーネントは選択したウィンドウを登録し、そのbool戻り値を確認します。

### Tobii対象画面のGameビュー自動選択

Windowsでは視線LoggerまたはTobiiDebugCheckがあるシーンのロード後に`TobiiTrackedGameView`が自動生成されます。シーンへの追加やInspectorの割当は不要です。SDKのファイル・DLL・ローカルPackage参照は変更しません。

PC/Gaze機能・モニター名・正の画面寸法を持つ機器の設定を読み、同じプロセスの可視Gameビュー（Windows Playerでは可視の実行ウィンドウ）の中から、モニター名とOS画面範囲がともに一致するものを選択します。選択中の機器情報が使えない場合は非同期列挙で見つかった、接続済みで条件に合う単一の機器を使います。複数の機器が候補の場合は自動で1台を選びません。UnityのDisplay番号とWindowsのDISPLAY番号を同じ番号として扱いません。

対象モニター上に候補が複数ある場合は、フォーカスのある親ウィンドウ内の候補を優先し、同条件なら現在の選択を保持します。残りはHWNDの順で固定します。SDKの描画領域変換を引き続き使い、Editorのツールバー・縦横比の余白を除いたGameビューのViewport/Screen座標を記録します。Gameビューを移動・新規作成する処理はありません。まず計測したいビューをTobii設定画面に表示してください。異なる描画解像度・拡大率の複数Gameビューを併用する場合は、選ばれたビュー内の中央・四隅と記録座標が一致するか実機で検証してください。

SDK 5.0.0.3には検索条件を差し替える公開APIがないため、SDKの境界Providerが保持する非公開HWNDを設定し、Editorの約2秒ごとのモニター条件なしの再検索を止めます。コンポーネントの停止時には元のHWNDと更新タイマーを戻します。リフレクションで必要なSDKフィールドを取得できない場合は無効扱いにして警告します。`Assets/link.xml`はPlayerのコード削除から使用する非公開メンバーを保持します。SDK更新後にフィールド構造が変わった場合は互換性の再確認が必要です。

探索は実時間で0.5秒ごと、選択したウィンドウの可視性・所属プロセス・モニター対応は毎フレーム確認します。Pause中も動作します。対象モニターにGameビューがない、モニター設定が空・0、登録拒否などの場合はConsoleに状態の変化時だけ警告を出します。`GameViewSelection=Matched:...`で対象モニター・HWNDを確認できます。対象ビューの消失・別モニターへの移動時は選択を解除し、別画面へフォールバックしません。

Task Switchの`gaze.csv`と通常の視線CSVはこの選択経路を共有します。対象なし・選択変更の当該フレーム・未接続・0.5秒以上前の視線は`is_valid=0`で座標を空欄にします。機器接続を表す`is_connected`は独立しているため、機器に接続していても対象ビューがなければ`is_connected=1,is_valid=0`です。選択変更時には前のビューの視線キューとSDKのLast/履歴を破棄し、再取得まで前の座標を記録しません。`gaze.csv`の既存列は維持します。画面外の有効な視線を画面内へ修正して有効化する処理はありません。

自動選択は1台のTobiiの計測対象を選ぶための処理です。複数の物理モニター全体の同時視線計測には拡張しません。WindowsのSDK API定義を使ったコンパイルと、選択・CSV空欄化の代替API検証は実施しますが、Win32ウィンドウ列挙・Unity Editor/Playerの描画位置・Tobii実機・IL2CPPでの動作は実機確認が必要です。

GameビューをTobiiのモニターへ移しても接続しない場合は、追加の`WindowMonitor`/`WindowMonitorRect`（SDKが保持するHWNDのモニター名・OS座標の矩形）、`SelectedTracker`（現在選択された機器）、`TrackerEnumeration`/`TrackerCount`/`TrackerList`（SDKから検出可能な機器）を確認します。機器情報にはModel、Type、Attached、Capabilities、Monitor、DisplayRectを出し、URLやシリアル番号は出しません。Monitor/DisplayRectとWindowMonitor/WindowMonitorRectを照合してください。名前や矩形が空・ゼロの場合は、設定画面との対応が確認できていません。

列挙は非同期です。`Pending`は取得待ちで、機器0台とは異なります。`Complete`かつ`TrackerCount=0`は列挙完了時点でSDKが機器を検出できていない状態です。`SelectedTracker=None`だけでは機器0台とは判断しません。完了後5秒で再列挙し、取得待ち中は前回完了時の件数・一覧を保持します。最初の取得待ちは`TrackerCount=Unknown`です。Pendingの秒数が長く増え続ける場合もそのまま診断結果として残します。検出されてもAttached=trueだけで視線接続成功とは判断せず、IsConnected/IsValidも確認してください。この処理は接続先・追跡ウィンドウ・視線座標系を変更しません。

Play開始後30秒程度のConsoleログを保存して比較します。同じモニターのWindows x86_64ビルド、SDK付属のGaze Point Dataサンプルでも比較すると、Editorのウィンドウ検出、プロジェクト固有の処理、SDKとTobii実行環境の連携を順に切り分けられます。

| 追加診断 | 切り分け |
| --- | --- |
| HostがTobiiHostStub | Windowsビルド対象、SDKのEULA受諾状態、Host破棄後の状態を確認 |
| 実HostでHWND=0x0 | SDKがGameビューのウィンドウを見つけられていない。Gameビューを単独ウィンドウにして再試行し、Windows x86_64ビルドでも比較 |
| HWND非ゼロ、ApiInitialized=false | ネイティブAPI側の初期化を確認。HostInitialized=trueでも判断できない |
| ApiInitialized=true、IsConnected=falseが継続 | ウィンドウとTobiiに設定したモニターの対応、SDKとTobii実行環境の連携を確認。ネイティブ初期化は装置接続の保証ではない |
| 接続true、TrackerEnabled=false | Tobii Experience側のトラッカー有効状態を確認 |

このSDKのEditor用ウィンドウ検索は、Win32のクラス名`UnityContainerWndClass`と子ウィンドウ名`UnityEditor.GameView`の一致に依存します。Editorだけで失敗しWindowsビルドで成功する場合はこの検索経路が候補です。実際のHWNDを確認するまでは原因として断定しません。`Unknown`はSDK内部フィールドが見つからない場合で、0と同じ意味ではありません。IL2CPPなどでは非公開メンバーが保持されない場合があるため、Hostの診断はまずEditorで確認してください。

| 診断結果 | 次に確認する箇所 |
| --- | --- |
| `IsConnected=false`が継続 | SDKの読み込み・Initializerの有効性・初期化エラー。起動直後1回だけのfalseで判断しない |
| 接続true、`IsValid=false`が継続 | Game/ビルドのフォーカス、Tobiiで設定したモニター上で視線を向けているか。SDK付属サンプルでも同じ症状か |
| 接続true、有効true、CSV座標あり | 取得は成功。Viewport/画面寸法と対象Canvasの画面を照合し、表示・AOIの座標変換を確認 |
| 診断は有効、CSVがない | Debug開始ではなくTask SwitchのStartから記録開始したか、上記保存先の新しいセッションフォルダか、Loggerが有効か |

`gaze.csv`では、上記の状態を`is_connected,app_focused,is_valid`（1/0）で記録します。無効な視線は座標を空欄で残します。Task SwitchのLoggerは実験のPause中も採取しますが、通常モードの`TobiiGazeCsvLogger`はPause中の採取を停止します。今回の診断ログ修正はSDK接続の修復ではありません。実機で原因の切り分けが必要です。

Tobii Gamingの画面ベースの視線は、トラッカーを取り付けて設定した1画面が対象です。複数の実験用モニター全体が同時に取得できるとは扱わないでください。

参照: [Unity Application.isFocused](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-isFocused.html)、[Tobiiの複数画面対応](https://help.tobii.com/hc/en-us/articles/209529429-Can-I-use-multiple-screens)。

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

Mono（mcs/mono）とPython 3が必要です。Unity代替APIを使用して実際のLogger・CSV書き込み・集計処理を実行し、12ファイルの列数、共通時計/ID、入力ゲート、重複完了、途中終了、再開始、視線の有効/無効、再集計との全列一致を検証します。代替APIはTests配下のみです。Unityシーン・実機ジョイスティック・Tobiiの動作確認は別途必要です。ExperimentStatusのTextや安全保持解除の70A設定はこのログ変更では変更しません。

## サブタスク

ManagerのSecondary Task ModeでNone/Auditory/Visualを選択します。音は [TaskSwitchAuditorySubtask.md](TaskSwitchAuditorySubtask.md)、色は [TaskSwitchVisualSubtask.md](TaskSwitchVisualSubtask.md) を参照してください。schema 5では視覚用2ファイルと、session/inputのモード・視覚設定/色状態を追加します。音用の2ファイルおよび列は維持します。inputのpedal_axis/raw_pedal/pedal_armedは現在動作しているサブタスクに共通です。サブタスクの開始/停止/エラー時にもsessionの設定行を追加します。使わない種類のファイルはヘッダーのみです。既存の切替・サイクル再集計ツールはそのまま使用できます。

## Pause / Start

Pauseは主タスク・切替・監視・電流制御・音/色提示を保持して止め、Startは同じ状態から再開します。停止中もTask Switchの時系列CSVは壁時計で記録し、global_paused=1とpause_interval_indexを付けます。events.csvにはGlobalPauseStarted/GlobalPauseEnded（停止中に記録開始した場合はGlobalPauseAtLoggingStart）、pause_intervals.csvには各区間の正確な開始/終了・継続時間を保存します。詳しくは [TaskSwitchPause.md](TaskSwitchPause.md) を参照してください。既存のswitch_summary/cycle_summaryの経過時間はPauseを含む壁時計のままです。Pauseを除いた作業時間が必要な場合はpause_intervalsとの重複時間を差し引いてください。
