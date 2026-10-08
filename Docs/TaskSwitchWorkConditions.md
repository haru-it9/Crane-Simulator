# 作業範囲・座標・枚数のCSV指定

TaskSwitchExperimentManagerの **Switch Schedule Csv** に切替要請スケジュール、**Work Conditions Csv** に作業条件を割り当てます。RemoteManagementScene(union)には作業条件テンプレートを割り当て済みです。Source Total Cycle Countは5です。CSVはヘッダー付き、数値は小数点「.」、枚数と番号は整数とします。

## 切替要請スケジュール

```csv
switchIndex,sourceCycle,sourcePhase,minimumDelaySeconds,maximumDelaySeconds,targetTaskPattern
1,1,Move2,1,9,Move1ToLiftUp
2,2,Move1,1,9,Move2ToPlace
```

| targetTaskPattern | 実施する切替先作業 | 初期状態 / 終了 |
| --- | --- | --- |
| Move1ToLiftUp（または1） | Move1 → LiftUp | 吸着なしで開始。LiftUpの全詳細ステップ完了で復帰 |
| Move2ToPlace（または2） | Move2 → Place | pickupCount枚を実物の板から吸着済みにして開始。Placeの配置後上昇まで完了すると復帰 |

旧5列CSVはManagerのTarget Task Patternを使用します。切替元の指定フェーズ開始からminimum～maximum秒のランダム待ちで要請します。待ちがフェーズをまたぐ動作は従来と同じです。switchIndexは1から連番、sourceCycleは1～5です。CSVの3パターンには既存の要請タイミングを保ち、2種類の作業を交互に指定しています。研究条件に合わせてこの列を変更してください。

## 座標と枚数

[TaskSwitchWorkConditionsTemplate.csv](../Assets/TaskSwitchWorkConditionsTemplate.csv) を複製または編集します。

```csv
role,index,pickupX,pickupZ,placementX,placementZ,pickupCount,placementCount
Source,1,-4,2,4,0,1,1
Target,1,16,2,24,0,1,1
```

| 列 | 意味 |
| --- | --- |
| role | Source（切替元） / Target（切替先） |
| index | Sourceはサイクル番号、TargetはスケジュールのswitchIndex |
| pickupX / pickupZ | Move1・LiftUpの目標ワールド座標[m]。Move2開始時に事前吸着する板の置場にも使用 |
| placementX / placementZ | Move2・Placeの目標ワールド座標[m] |
| pickupCount | 吊り上げる枚数。Move2開始パターンでは最初から吸着する枚数 |
| placementCount | 配置する枚数。Move1→LiftUpの切替先では配置フェーズを実施しないため未使用 |

座標は地点番号や操作UIの表示座標ではなく、CraneWorkTargetManager／BoardGeneratorと同じワールドXZです。例えばCrane1のX=-4、Crane2のX=16は、それぞれのクレーンの左側置場です。pickup座標はBoardGeneratorのSpawn Positionsに対応させ、指定枚数以上の板が必要です。テンプレートはSourceの5サイクルとTargetの8要請分を用意し、枚数はすべて1です。

Move2→Placeの開始時はCSVで指定された置場の実際の厚板を、pickupCount枚まとめてリフマグへ吸着させます。位置はMainLifMagの原点ではなくMagnetSensorの実際の下面から求め、最上板の上面を合わせます。複数枚は各板の実寸の厚さで下に並べます。通常吸着と同じくワールド寸法を保って親子付けし、リフマグの非一様な縮尺によって板を縮小しません。別の仮の厚板は生成しません。磁石・板の形状またはRigidbodyが欠けている場合、吸着登録できない場合はConsoleにエラーを出して開始を中止します。

Move1→LiftUpおよびMove2→Placeの間は同じ座標を維持します。切替元は各サイクルの開始で条件を更新し、中断からの復帰で目標を再抽選しません。Sourceの完了サイクル数は切替を挟んでも累積し、5回目のPlace完了で終了します。

枚数から実際のBoardInfo.Weightの合計を計算し、重量と枚数の両方で達成を判定します。配置はリフマグが最後に吸着した板から外す順序に合わせます。placementCountはpickupCount以下です。TargetのMove2→Placeは一部配置にも対応し、余った板を保持したままその作業を終了できます。Sourceは次のMove1が吸着なしを前提とするため、最終サイクル以外のplacementCountをpickupCountと同じにしてください。無効なCSVは開始前にConsoleへ理由を表示し、開始を中止します。

同じ置場を繰り返し使う場合は合計吊り上げ枚数を確保してください。Sourceの置場は切替のたびに補充しません。Targetは要請ごとに既存のシナリオ初期化を使うため、Reset Boards On DoneをONにして次の作業の板を復元してください。今回のシーンの同設定は従来値を維持しています。実験開始位置・板サイズ・人・トレーラの設定は既存のInterventionScenarioManager／BoardGeneratorを使用します。

## 作業者主導の切替

Managerの **Switch Method = Operator Initiated** を選びます。

1. CSVのタイミングで要請が発生し、確認ボタンが「赤ボタンで作業切替」になります。
2. 要請後も切替元の表示・操作・作業進行を継続します。
3. UI確認ボタンまたはJoyStick2RedButtonを押すと切替元をその場で中断し、切替先へ表示と作業を切り替えます。このモードは切替先開始のための追加押下を要求しません。
4. 押しっぱなしの確認入力が操作へ流れないよう、ボタンを離した後に切替先の操作を有効にします。
5. 切替先完了後は切替元へ戻り、従来どおり確認ボタンで中断点から再開します。

Pause中は切替確認を受理せず、Start後も同じ要請待ちを維持します。要請待ちの間に切替元の5サイクルがすべて完了した場合は実験を終了します。既存の表示先切替後確認・Countdown・Phase Boundaryは、切替先画面を表示した後の確認操作を維持します。既存enum値0～2は変えず、OperatorInitiatedを3として追加しています。

## 切替・復帰時の電流制御

操作対象に切り替わった時点の吸着状態で、電流スライダーの再開条件を固定します。切替先への移行と切替元への復帰の両方に適用します。

| 吸着状態 | 待機中の電流 | 通常制御へ戻る入力 |
| --- | --- | --- |
| 厚板あり | 仮想保持電流70A（厚板と重量を保持） | 70A以上 |
| 厚板なし | 仮想電流10A（前のクレーンの高い入力による吸着を抑止） | 10A以下 |

条件を満たした入力から通常の重量判定・吸着処理を再開します。Move2→Placeで事前吸着した板は、70A以上の入力を確認した後、現在電流が保持重量の必要電流（重量[t] × Current Ampere Per Ton）を下回ると、最下層から順に解除できます。この板に対する電流解除はMove2中の高さ・Placeの接地判定を待ちません。解除時は親子付けを外し、Rigidbodyを動的・重力有効へ戻します。通常の吸着板・従来の介入板と解除ボタンのフェーズ制限は維持します。Pause中・確認入力の解除待ちには条件を判定しません。10A側の閾値は各LifMagSystemのInspectorにあるTask Switch Empty Release Current Ampere（初期値10）で設定できます。従来の累積入力モードは変更しません。ExperimentStatus(Text)は変更しません。

## CSV記録（schema 9）

出力は既存の12ファイルです。session.csvにwork_conditions_csvとtarget_task_pattern、events.csvにtarget_task_patternとtarget_work_condition_jsonを追加します。作業条件CSV全体と各要請の適用条件を残します。TaskSwitchBoardsPreloadedには事前吸着枚数・磁石下面のワールドY・接地なし電流解除の適用を記録し、BoardDetachedInsufficientCurrentで各板の電流解除を記録します。WorkConditionPreparedには座標・枚数・吊り上げ目標重量、TargetTaskSegmentStartedには開始/終了フェーズを記録します。

OperatorSwitchAvailableとOperatorSwitchConfirmationPressedで要請後の判断待ちを区別できます。switch_summaryのrequest_sからsource_suspended_sまでが、作業者主導で切り替えるまでの待ち時間です。従来の計時・再集計との互換のためTargetWorkCycleCompletedというイベント名は残し、detailにCompletedSegments・Pattern・EndPhaseを記録します。切替先のcycle_summaryの1行は今回の2フェーズ作業の完了を意味し、切替元の完全サイクルとは区別して分析してください。target_runs_full_cycle列は0になります。

## 検証範囲

Tests/TaskSwitchFlow/run.shは実際のManager・CycleController・LoadPlanManagerを代替Unity APIで動かし、2パターン、CSV解析、5サイクル、復帰座標の保持、事前吸着・部分配置重量、作業者主導の継続・1押下切替・押下解除待ち、Pause、既存3手法、CSV要請の自動発生を検証します。LifMagSystem本体とCraneUnitの実際の吸着メソッドを使い、複数板の下面整列・厚みの積み重ね・非一様な親の縮尺と板のピボットずれへの対応・Rigidbody固定、必要電流を下回った際の下層からの解除とイベント、通常板の解除制限、70A/10Aの境界値、閾値前の吸着抑止、Pause・確認ロックを検証します。BoardGeneratorの実際の選択メソッドを切り出して、上側からの枚数・重量、板不足、移動済みの板を確認します。Tests/TaskSwitchLogging/run.shで12ファイル・再集計・Pause・視線・サブタスクの回帰を確認します。Unity Editorの実描画・衝突・物理挙動と実機入力は別途確認してください。
