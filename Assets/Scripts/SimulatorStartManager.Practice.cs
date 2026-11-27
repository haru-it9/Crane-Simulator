using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class SimulatorStartManager
{
    private const int MinimumPracticeTrials = 8;

    [Header("Task Switch 練習")]
    [SerializeField, Tooltip("従来の設定。通常はTaskSwitchExperimentManagerのEnable Practice Modeを使用します。")]
    private bool enableTaskSwitchPractice;
    [SerializeField, Tooltip("負の値の場合は実験のSourceクレーンを使います。")]
    private int practiceCraneIndex = -1;
    [SerializeField] private Vector2 practicePickupWorld = new Vector2(-4f, 4f);
    [SerializeField] private Vector2 practiceAlternatePickupWorld = new Vector2(4f, 2f);
    private Vector2 activePracticePickup;
    private static readonly Vector2 PracticeTravelDistance = new Vector2(6f, 4f);
    [SerializeField] private float practiceStartLocalY = -1.56f;
    [SerializeField] private Vector2 practiceDisplayOrigin = new Vector2(1f, -1f);
    [SerializeField, Min(0f)] private float practiceStableChangePercent = 5f;
    [SerializeField] private Text practiceStatusText;

    public static bool IsPracticeActive { get; private set; }
    [SerializeField] private TaskSwitchExperimentManager practiceModeSettings;

    private bool UsePracticeStartScreen()
    {
        if (practiceModeSettings == null)
            practiceModeSettings = FindObjectOfType<TaskSwitchExperimentManager>(true);
        if (IsPracticeActive) return true;
        if (practiceModeSettings != null && practiceModeSettings.IsPracticeConditionSelected)
        {
            ApplyPracticeDisplay();
            return false;
        }
        return enableTaskSwitchPractice ||
            (practiceModeSettings != null && practiceModeSettings.IsPracticeModeEnabled);
    }

    private void ApplyPracticeDisplay()
    {
        var layout = FindObjectOfType<DisplayLayoutManager>(true);
        if (layout != null) layout.SelectTaskSwitchDisplayMode();
        SelectTaskSwitchExperimentMode();
    }

    private readonly List<double> practiceTimes = new List<double>();
    private CraneOperationManager practiceOperation;
    private CraneInstance practiceCrane;
    private CraneWorkCycleController practiceCycle;
    private BoardGenerator practiceBoards;
    private Text practiceStartButtonText;
    private readonly List<int> practiceStartDirections = new List<int>(4);
    private bool practiceReady, practiceRunning, practiceCompleted;
    public bool IsPracticeWaitingForStart => IsPracticeActive && !practiceRunning && !practiceCompleted;
    private int previousPracticeDirection = -1;
    private double practiceStartTime, practiceFinishTime;

    private bool InitializePracticeCrane()
    {
        ApplyPracticeDisplay();
        if (!ApplyCraneCountForSelectedMode()) return false;

        int craneIndex = practiceCraneIndex >= 0 ? practiceCraneIndex
            : practiceModeSettings != null ? practiceModeSettings.PracticeSourceCraneIndex : 0;
        var operation = FindObjectOfType<CraneOperationManager>();
        var registry = FindObjectOfType<CraneRegistry>();
        var crane = registry != null ? registry.GetCraneByRuntimeIndex(craneIndex) : null;
        var cycle = crane != null ? crane.GetComponentInChildren<CraneWorkCycleController>(true) : null;
        var boards = crane != null ? crane.BoardGenerator : null;
        if (boards == null && crane != null) boards = crane.GetComponentInChildren<BoardGenerator>(true);
        if (boards == null)
        {
            var scenarios = FindObjectOfType<CraneInterventionScenarioManager>();
            if (scenarios != null) boards = scenarios.GetBoardGeneratorForCrane(craneIndex);
        }
        if (operation == null || crane == null || crane.CraneUnit == null ||
            crane.InformationTarget == null || cycle == null || boards == null) return false;

        StopAllLogging();
        operation.BeginTaskSwitchExperimentMode();
        if (!operation.SelectCraneForTaskSwitch(craneIndex))
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
            operation.CurrentInformationDisplay.CoordinateDisplayOrigin = new Vector2(
                Mathf.Round(practiceDisplayOrigin.x), Mathf.Round(practiceDisplayOrigin.y));
        return true;
    }

    private void PreparePracticeTrial()
    {
        IsPracticeActive = true;
        practiceReady = false;
        SetOperationUIInteractable(false);
        if (startScreen != null) startScreen.SetActive(false);

        if (practiceOperation == null && !InitializePracticeCrane())
        {
            SetPracticeStatus("練習用クレーン、作業サイクル、板置場の設定を確認してください。", true);
            return;
        }

        practiceOperation.SetTaskSwitchOperationInputLocked(true);
        practiceOperation.SetPracticeCoordinatesVisible(false);
        IsOperationEnabled = true;
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
        if (previousPracticeDirection < 0 && !ValidatePracticeStartPoses()) return;

        // Randomize the approach without repeating the exact previous start.
        var eligibleDirections = practiceStartDirections.FindAll(candidate =>
            candidate != previousPracticeDirection);
        int direction = eligibleDirections[Random.Range(0, eligibleDirections.Count)];
        if (!TrySetPracticeStartPose(direction))
        {
            SetPracticeStatus("練習の開始座標が操作範囲外です。板山の座標と開始高さを確認してください。", true);
            return;
        }

        practiceCrane.CraneUnit.ResetSpeedLevel();
        activePracticePickup = GetPracticePickup(direction);
        previousPracticeDirection = direction;
        practiceReady = true;
        string previousResult = practiceTimes.Count > 0
            ? $"\n前回 {practiceTimes[practiceTimes.Count - 1]:F2} 秒" : "";
        SetPracticeStatus($"Start · 第{practiceTimes.Count + 1}試行{previousResult}");
    }

    private bool ValidatePracticeStartPoses()
    {
        practiceStartDirections.Clear();
        for (int direction = 0; direction < 8; direction++)
        {
            Vector2 pickup = GetPracticePickup(direction);
            if (pickup.x != Mathf.Round(pickup.x) || pickup.y != Mathf.Round(pickup.y) ||
                !practiceBoards.TryGetPickupTargetWeightKg(pickup.x, pickup.y, 1, out _, out _, out _))
                continue;
            if (TrySetPracticeStartPose(direction)) practiceStartDirections.Add(direction);
        }

        if (practiceStartDirections.Exists(candidate => (candidate & 1) == 0) &&
            practiceStartDirections.Exists(candidate => (candidate & 1) != 0)) return true;
        SetPracticeStatus("横行6 m・走行4 mで左右から開始できません。練習用の板山座標と開始高さを確認してください。", true);
        return false;
    }

    private Vector2 GetPracticePickup(int direction) =>
        (direction & 4) == 0 ? practicePickupWorld : practiceAlternatePickupWorld;

    private bool TrySetPracticeStartPose(int direction)
    {
        Vector2 pickup = GetPracticePickup(direction);
        float x = pickup.x + ((direction & 1) == 0 ? -PracticeTravelDistance.x : PracticeTravelDistance.x);
        float z = pickup.y + ((direction & 2) == 0 ? -PracticeTravelDistance.y : PracticeTravelDistance.y);
        return practiceCrane.CraneUnit.TryAlignInterventionPickup(
            practiceCrane.InformationTarget, x, z, practiceStartLocalY);
    }

    public void StartPracticeTrial(TaskSwitchExperimentManager experiment = null)
    {
        if (practiceCompleted || practiceRunning || ExperimentPauseManager.IsPaused) return;
        if (experiment != null) practiceModeSettings = experiment;
        ResolvePracticeStartButtonText();
        if (practiceModeSettings != null && practiceModeSettings.IsPracticeConditionSelected)
            ApplyPracticeDisplay();
        if (!practiceReady) PreparePracticeTrial();
        if (!practiceReady) return;

        practiceCycle.SetExperimentConditions(new[] { new TaskSwitchWorkCondition
        {
            role = "Source", index = 1,
            pickupX = activePracticePickup.x, pickupZ = activePracticePickup.y,
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
        practiceOperation.SetPracticeCoordinatesVisible(true);
        practiceOperation.SetTaskSwitchOperationInputLocked(false);
        UpdatePracticeTimerText();
    }

    private void RecordPracticeFinish(CraneWorkCycleController cycle, int completedCycles)
    {
        if (!practiceRunning || cycle != practiceCycle || completedCycles < 1) return;
        practiceFinishTime = ExperimentPauseManager.ActiveRealtime;
        practiceOperation.SetTaskSwitchOperationInputLocked(true);
        practiceOperation.SetPracticeCoordinatesVisible(false);
    }

    private void Update()
    {
        if (!practiceRunning || practiceCycle == null) return;
        if (practiceCycle.IsRunning)
        {
            UpdatePracticeTimerText();
            return;
        }

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
            SetPracticeStatus($"練習完了 · {count}試行\n最終 {duration:F2} 秒");
    }

    private static bool IsPracticeTimeStable(double previous, double current, double limit)
    {
        return previous > 0 && System.Math.Abs(current - previous) / previous <= limit;
    }

    private void ResolvePracticeStartButtonText()
    {
        if (practiceStartButtonText != null || practiceModeSettings == null) return;

        foreach (Button button in FindObjectsOfType<Button>(true))
        {
            var onClick = button.onClick;
            for (int i = 0; i < onClick.GetPersistentEventCount(); i++)
            {
                if (onClick.GetPersistentTarget(i) != practiceModeSettings ||
                    onClick.GetPersistentMethodName(i) != nameof(TaskSwitchExperimentManager.StartExperiment)) continue;
                Text label = button.GetComponentInChildren<Text>(true);
                if (label == null) continue;
                practiceStartButtonText = label;
                label.resizeTextMinSize = 8;
                label.resizeTextMaxSize = label.fontSize;
                label.resizeTextForBestFit = true;
                return;
            }
        }
    }

    private void UpdatePracticeTimerText()
    {
        double elapsed = ExperimentPauseManager.ActiveRealtime - practiceStartTime;
        WritePracticeStatusText($"第{practiceTimes.Count + 1}試行\n{elapsed:F2} 秒");
    }

    private void WritePracticeStatusText(string message)
    {
        if (practiceStatusText != null) practiceStatusText.text = message;
        if (practiceStartButtonText != null) practiceStartButtonText.text = message;
    }

    private void SetPracticeStatus(string message, bool error = false)
    {
        WritePracticeStatusText(error ? "準備エラー\nConsole確認" : message);
        if (practiceStatusText != null && practiceStatusText != practiceStartButtonText)
            practiceStatusText.text = message;
        if (error) Debug.LogError(message, this);
        else Debug.Log(message, this);
    }
}
