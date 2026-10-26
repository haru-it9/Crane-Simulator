# 作業切替・復帰時の認知支援（ひな型）

`RemoteManagementScene(union)` の `TaskSwitchExperimentManager` に
`TaskSwitchCognitiveSupport` を追加済みです。初期値は **両方・8秒** です。

## 条件の選択

Inspector の **Support Content** から選択します。

| 設定 | 内容 |
| --- | --- |
| Phase Only | フェーズのみ |
| Input Direction Only | 入力方向のみ |
| Phase And Input Direction | フェーズ＋入力方向 |

支援なしの比較をする場合は、このComponent自体を無効にします。
`Move1/Move2` は「移動」、`LiftUp` は「吊り上げ」、`Place/PlaceToTrack` は「配置」です。

## 表示開始・終了

- Target画面への実際の切替（`TargetDisplaySwitched`）と、Source画面への復帰
  （`SourceDisplayRestored`）で、そのフレームから表示します。要請・カウントダウン開始では表示しません。
- **Display Seconds** は初期値8秒。5秒・10秒などに変更できます。
- 確認待ちも表示時間に含みます。確認押下・操作開始ではタイマーを延長しません。
- 一時停止中も表示を保持し、停止時間は表示秒数に含めません。
- 次の切替・復帰では表示内容と残り時間を更新し、実験終了・再開始・無効化では非表示にします。
- 今回は固定時間方式です。「入力が安定したら消す」判定はまだ実装していません。

## 位置・外観

Hierarchy の
`TaskSwitchUIRoot/TouchPanel/TextandButton/CognitiveSupport` 配下に置いています。

- `SupportPhaseText`: タイトルと操作パネルの間の初期配置。RectTransform・文字サイズ・色を調整できます。
- `SupportLeftX`: 情報パネル左側。横行を縦の矢印で表示します。
- `SupportRightY`: 情報パネル右側。上昇・下降を縦の矢印で表示します。
- `SupportRightZ`: 情報パネル右側。走行を横の矢印で表示します。

右Yと右Zは同じ位置に重ねています。移動と上下動を同時には推奨せず、その時点で必要な側だけ表示します。
左Xと右Zは、両軸が未到達なら同時に表示します。すべて Raycast Target をOFFにしてあり、既存Button操作を遮りません。
矢印はフォントの矢印文字ではなく `TaskSwitchSupportArrow` のメッシュで描きます。

## 方向・作業ステップ

矢印は現在の詳細ステップ、共通目標X/Z、InformationTargetの位置、着床、MainLifMagのlocal Yから更新します。
現在のレバー入力をそのまま示す表示ではありません。完了判定を変更することもありません。

- 移動: ステップのX/Z許容値を使い、未到達の軸だけ表示します。
- 配置前に位置がずれている場合: まず位置合わせを示し、下降を同時には示しません。
- 下降: 着床で下降矢印を消します。
- 上昇: ステップで定義されたlocal Y到達値まで表示します。
- 重量吸着・解放、速度停止待ちなど: 移動矢印は表示しません。

確認押下前は、その実験で選択されたCSV条件を読み取り専用で参照します。
目標表示・作業Tracker・操作ロックは変更しません。Sourceがフェーズ境界で待機している場合は
再開予定のフェーズとそのCSV目標を使い、押下後は実際のTrackerへ追従します。
CSVなしで未来のランダム目標がまだ不明の場合は、古い目標を推測して移動矢印を出しません。

**Positive X Points Up / Positive Y Points Up / Positive Z Points Right** で
正の移動指令に対応する矢印を反転できます。初期値は現在のInputManagerと
CraneOperationManagerの符号変換（X=-Joystick3Vertical、Y=Joystick2Vertical、Z=-Joystick2Horizontal）を前提としています。
通常の非反転ジョイスティックY軸では手前側が正のため、初期値の上昇は下向き矢印です。
実機の軸・ドライバー設定が異なる場合は、この3項目で合わせてください。

## ログ

既存の `events.csv` に `CognitiveSupportShown` / `CognitiveSupportUpdated` /
`CognitiveSupportHidden` を出力します。条件・固定表示秒数・レバー対応・フェーズ・ステップ・
必要なXYZ指令・非表示理由を記録します。XYZは表示向きではなくCraneUnitの指令符号です。
他の実験イベント、入力ログ、操作判定を置き換えません。

## 確認

`bash Tests/CognitiveSupport/run.sh` はMonoのmcs/monoで、方向判定、3条件、
画面切替前非表示、切替・復帰、押下による時間延長なし、停止・無効化、シーン参照を検証します。
Unity上では、Play時の実際の配置、マルチ画面、実機のレバー方向を最終確認してください。
