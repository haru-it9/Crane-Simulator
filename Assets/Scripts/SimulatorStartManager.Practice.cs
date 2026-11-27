using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class SimulatorStartManager
{
    private const int MinimumPracticeTrials = 8;

    [Header("Task Switch 練習")]
    [SerializeField] private bool enableTaskSwitchPractice;
    [SerializeField] private int practiceCraneIndex;
    [SerializeField] private Vector2 practicePickupWorld = new Vector2(-4f, 4f);
    [SerializeField] private Vector2 practiceTravelDistance = new Vector2(2f, 2f);
    [SerializeField] private float practiceStartLocalY = -1.56f;
    [SerializeField] private Vector2 practiceDisplayOrigin = new Vector2(0.5f, -0.5f);
    [SerializeField, Min(0f)] private float practiceStableChangePercent = 5f;
    [SerializeField] private Text practiceStatusText;

    public static bool IsPracticeActive { get; private set; }
    private bool PracticeSelected => enableTaskSwitchPractice && simulatorMode == SimulatorMode.TaskSwitchExperiment;
    private readonly List<double> practiceTimes = new List<double>();
    private CraneOperationManager practiceOperation;
    private CraneInstance practiceCrane;
    private CraneWorkCycleController practiceCycle;
    private BoardGenerator practiceBoards;
    private bool practicePreparing, practiceReady, practiceRunning, practiceCompleted;
    private int previousPracticeDirection = -1;
    private double practiceStartTime, practiceFinishTime;

    private IEnumerator PrepareFirstPracticeTrial()
    {
        IsPracticeActive = practicePreparing = true;
        yield return null;
        practicePreparing = false;
        PreparePracticeTrial();
    }

    private bool InitializePracticeCrane()
    {
        if (!ApplyCraneCountForSelectedMode()) return false;

        var operation = FindObjectOfType<CraneOperationManager>();
        var registry = FindObjectOfType<CraneRegistry>();
        var crane = registry != null ? registry.GetCraneByRuntimeIndex(practiceCraneIndex) : null;
        var cycle = crane != null ? crane.GetComponentInChildren<CraneWorkCycleController>(true) : null;
        var boards = crane != null ? crane.BoardGenerator : null;
        if (boards == null && crane != null) boards = crane.GetComponentInChildren<BoardGenerator>(true);
        if (operation == null || crane == null || crane.CraneUnit == null ||
            crane.InformationTarget == null || cycle == null || boards == null) return false;

        StopAllLogging();
        operation.BeginTaskSwitchExperimentMode();
        if (!operation.SelectCraneForTaskSwitch(practiceCraneIndex))
        {
            operation.EndTaskSwitchExperimentMode();
            return false;
        }

        practiceOperation = operation;
        practiceCrane = crane;
        practiceCycle = cycle;
        practiceBoards = boards;
        practiceCycle.AllCyclesCompleted += RecordPracticeFinish;
        if (operation.CurrentInformationDisplay != null)
            operation.CurrentInformationDisplay.CoordinateDisplayOrigin = practiceDisplayOrigin;
        return true;
    }

    private void PreparePracticeTrial()
    {
        IsPracticeActive = true;
        practiceReady = false;
        IsOperationEnabled = false;
        SetOperationUIInteractable(false);
        if (startScreen != null) startScreen.SetActive(true);

        if (practiceOperation == null && !InitializePracticeCrane())
        {
            SetPracticeStatus("練習用クレーン、作業サイクル、板置場の設定を確認してください。", true);
            return;
        }

        practiceOperation.SetTaskSwitchOperationInputLocked(true);
        practiceCycle.StopCycleAndTracker();
        practiceCrane.CraneUnit.ClearInterventionBoardAttachment();

        // Reset to the same board and stage geometry even in random generation mode.
        Random.State randomState = Random.state;
        try
        {
            Random.InitState(731);
            practiceBoards.ResetBoards();
        }
        finally
        {
            Random.state = randomState;
        }

        if (practiceCompleted) return;
        if (!(practiceTravelDistance.x > 0f && practiceTravelDistance.y > 0f) ||
            !practiceBoards.TryGetPickupTargetWeightKg(practicePickupWorld.x, practicePickupWorld.y,
                1, out _, out _, out _))
        {
            SetPracticeStatus("練習の移動距離と、厚板のあるつり上げ目標座標を確認してください。", true);
            return;
        }

        int direction = Random.Range(0, previousPracticeDirection < 0 ? 4 : 3);
        if (previousPracticeDirection >= 0 && direction >= previousPracticeDirection) direction++;
        float x = practicePickupWorld.x + ((direction & 1) == 0 ? -1f : 1f) * practiceTravelDistance.x;
        float z = practicePickupWorld.y + ((direction & 2) == 0 ? -1f : 1f) * practiceTravelDistance.y;
        if (!practiceCrane.CraneUnit.TryAlignInterventionPickup(practiceCrane.InformationTarget, x, z, practiceStartLocalY))
        {
            SetPracticeStatus($"練習の開始座標が操作範囲外です (X={x:F2}, Z={z:F2})。", true);
            return;
        }

        practiceCrane.CraneUnit.ResetSpeedLevel();
        previousPracticeDirection = direction;
        practiceReady = true;
        SetPracticeStatus($"練習 {practiceTimes.Count + 1} 試行目の準備完了。Startで開始します。");
    }

    private void StartPracticeTrial()
    {
        if (practiceCompleted || practiceRunning || practicePreparing || ExperimentPauseManager.IsPaused) return;
        if (!practiceReady)
        {
            PreparePracticeTrial();
            return;
        }

        practiceCycle.SetExperimentConditions(new[] { new TaskSwitchWorkCondition
        {
            role = "Source", index = 1,
            pickupX = practicePickupWorld.x, pickupZ = practicePickupWorld.y,
            pickupCount = 1, placementCount = 1
        } });
        practiceCycle.SetAutomaticPhaseAdvance(true);
        if (!practiceCycle.StartTaskSegment(CraneStatusManager.WorkPhase.Move1, CraneStatusManager.WorkPhase.LiftUp))
        {
            SetPracticeStatus("練習を開始できませんでした。Consoleを確認してください。", true);
            return;
        }

        practiceReady = false;
        practiceRunning = true;
        practiceStartTime = ExperimentPauseManager.ActiveRealtime;
        practiceFinishTime = -1;
        if (startScreen != null) startScreen.SetActive(false);
        SetOperationUIInteractable(true);
        IsOperationEnabled = true;
        practiceOperation.SetTaskSwitchOperationInputLocked(false);
    }

    private void RecordPracticeFinish(CraneWorkCycleController cycle, int completedCycles)
    {
        if (!practiceRunning || cycle != practiceCycle || completedCycles < 1) return;
        practiceFinishTime = ExperimentPauseManager.ActiveRealtime;
        practiceOperation.SetTaskSwitchOperationInputLocked(true);
    }

    private void Update()
    {
        if (!practiceRunning || practiceCycle == null || practiceCycle.IsRunning) return;

        practiceRunning = false;
        if (practiceFinishTime < practiceStartTime)
        {
            PreparePracticeTrial();
            SetPracticeStatus("作業サイクルが中断されました。この試行は回数に含めません。", true);
            return;
        }

        double duration = practiceFinishTime - practiceStartTime;
        practiceTimes.Add(duration);
        int count = practiceTimes.Count;
        Debug.Log($"練習 {count} 試行目: {duration:F2} 秒", this);
        double limit = practiceStableChangePercent / 100.0;
        practiceCompleted = count >= MinimumPracticeTrials &&
            IsPracticeTimeStable(practiceTimes[count - 3], practiceTimes[count - 2], limit) &&
            IsPracticeTimeStable(practiceTimes[count - 2], practiceTimes[count - 1], limit);

        PreparePracticeTrial();
        if (practiceCompleted)
            SetPracticeStatus($"練習完了（{count} 試行）。直近3回のタイムが安定しました。");
    }

    private static bool IsPracticeTimeStable(double previous, double current, double limit)
    {
        return previous > 0 && System.Math.Abs(current - previous) / previous <= limit;
    }

    private void SetPracticeStatus(string message, bool error = false)
    {
        if (practiceStatusText != null) practiceStatusText.text = message;
        if (error) Debug.LogError(message, this);
        else Debug.Log(message, this);
    }
}
