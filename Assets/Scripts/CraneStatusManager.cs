using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CraneStatusManager : MonoBehaviour
{
    public enum WorkPhase
    {
        Move1,
        LiftUp,
        Move2,
        Place,
        PlaceToTrack
    }

    [System.Serializable]
    public class PhaseSetting
    {
        public WorkPhase phase;

        [Header("所要時間範囲 [秒]")]
        public float minDuration = 5f;
        public float maxDuration = 10f;

        [Header("人の立ち入り発生確率 0～1")]
        [Range(0f, 1f)]
        public float errorAProbability = 0.05f;

        [Header("つり上げ失敗発生確率 0～1")]
        [Range(0f, 1f)]
        public float errorBProbability = 0.05f;

        [Header("トレーラ積込発生確率 0～1")]
        [Range(0f, 1f)]
        public float errorCProbability = 0.05f;
    }

    [System.Serializable]
    public class CraneState
    {
        public string craneName = "Crane";

        public WorkPhase currentPhase;
        public float remainingTime;
        public bool hasError;
        public bool isStopped;

        public int cycleCount;
        public int nextPlaceToTrackCycle;

        [HideInInspector]
        public Coroutine routine;

        public string AutoStopText
        {
            get { return isStopped ? "停止" : "自動"; }
        }

        public ErrorType currentErrorType = ErrorType.None;
    }

    public enum ErrorType
    {
        None,
        ErrorA,
        ErrorB,
        ErrorC
    }

    [System.Serializable]
    public class StatusUiSet
    {
        [Header("自動／停止表示")]
        public Text[] autoStopTexts = new Text[0];
        public RawImage[] autoStopImages = new RawImage[0];

        [Header("フェーズ・停止要因表示")]
        public Text[] phaseTexts = new Text[0];
        public Text[] errorTypeTexts = new Text[0];

        [Header("クレーン別UIの親（任意）")]
        [Tooltip("Crane ID順に登録します。選択基数より後ろを非表示にします。")]
        public GameObject[] craneStatusUiRoots = new GameObject[0];
    }

    [Header("クレーン登録情報")]
    [Tooltip("使用基数はCraneRegistryのActive Crane Countから取得します。")]
    [SerializeField]
    private CraneRegistry craneRegistry;

    [Header("フェーズ設定")]
    [SerializeField]
    private List<PhaseSetting> phaseSettings = new List<PhaseSetting>();

    [Header("各クレーンの現在状態（実行時に自動生成）")]
    [SerializeField]
    private List<CraneState> craneStates = new List<CraneState>();

    [Header("PlaceToTrack発生サイクル範囲")]
    [SerializeField]
    private int minPlaceToTrackCycle = 3;

    [SerializeField]
    private int maxPlaceToTrackCycle = 6;

    [Header("状態色")]
    [SerializeField]
    private Color autoColor = new Color(0.3f, 0.7f, 1.0f);

    [SerializeField]
    private Color stopColor = Color.red;

    [Header("表示モード別ステータスUI")]
    [SerializeField]
    private StatusUiSet multiDisplayUiSet = new StatusUiSet();

    [SerializeField]
    private StatusUiSet singleDisplayUiSet = new StatusUiSet();

    [Header("状態管理の有効/無効")]
    [SerializeField]
    private bool statusManagementEnabled = true;

    [Header("SimulatorStartManagerのStart後に状態管理を開始する")]
    [SerializeField]
    private bool waitForSimulatorStart = true;

    [Header("状態管理停止時に全クレーンを自動表示へ戻す")]
    [SerializeField]
    private bool resetToAutoWhenDisabled = true;

    private bool simulatorStarted;
    private bool initialized;

    public bool IsStatusManagementEnabled
    {
        get { return statusManagementEnabled; }
    }

    public int ActiveCraneCount
    {
        get { return craneStates != null ? craneStates.Count : 0; }
    }

    private void Awake()
    {
        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }
    }

    private void Start()
    {
        simulatorStarted = !waitForSimulatorStart;

        if (simulatorStarted)
        {
            if (InitializeCranesFromRegistry() && statusManagementEnabled)
            {
                StartAllCranes();
            }
        }
        else
        {
            // 準備画面で基数が確定する前には状態を作成しません。
            craneStates.Clear();
            initialized = false;
        }

        UpdateStatusTexts();
    }

    private void Update()
    {
        UpdateStatusTexts();
    }

    /// <summary>
    /// CraneRegistryで確定した使用基数に合わせて状態を作成します。
    /// </summary>
    private bool InitializeCranesFromRegistry()
    {
        if (!EnsureRegistryIsReady())
        {
            initialized = false;
            return false;
        }

        int activeCraneCount = craneRegistry.ActiveCraneCount;

        if (activeCraneCount <= 0)
        {
            Debug.LogError(
                "CraneRegistryのActive Crane Countが0です。" +
                "先にCraneCountManagerで基数を適用してください。",
                this
            );
            initialized = false;
            return false;
        }

        StopAllCranes();
        craneStates.Clear();

        for (int runtimeIndex = 0;
             runtimeIndex < activeCraneCount;
             runtimeIndex++)
        {
            CraneInstance crane =
                craneRegistry.GetCraneByRuntimeIndex(runtimeIndex);

            CraneState state = new CraneState();

            if (crane != null &&
                !string.IsNullOrWhiteSpace(crane.DisplayName))
            {
                state.craneName = crane.DisplayName;
            }
            else
            {
                state.craneName = "Crane_" + (runtimeIndex + 1);
            }

            state.currentPhase = WorkPhase.Move1;
            state.remainingTime = 0f;
            state.hasError = false;
            state.isStopped = false;
            state.currentErrorType = ErrorType.None;
            state.cycleCount = 0;
            state.nextPlaceToTrackCycle = Random.Range(
                minPlaceToTrackCycle,
                maxPlaceToTrackCycle + 1
            );

            craneStates.Add(state);
        }

        initialized = true;
        UpdateStatusUiVisibility(activeCraneCount);
        UpdateStatusTexts();

        Debug.Log(
            $"CraneStatusManager：{activeCraneCount}基分の状態を初期化しました。"
        );

        return true;
    }

    private void StartAllCranes()
    {
        if (craneStates == null)
        {
            return;
        }

        foreach (CraneState state in craneStates)
        {
            if (state == null)
            {
                continue;
            }

            // 二重起動防止
            if (state.routine == null)
            {
                state.routine = StartCoroutine(CraneWorkRoutine(state));
            }
        }
    }

    public void StartStatusManagementFromSimulator()
    {
        if (simulatorStarted)
        {
            return;
        }

        // SimulatorStartManagerでは、これより前に
        // CraneCountManager.ApplySelectedCraneCount()を実行します。
        if (!InitializeCranesFromRegistry())
        {
            Debug.LogError(
                "CraneStatusManager：状態管理を開始できませんでした。",
                this
            );
            return;
        }

        simulatorStarted = true;

        if (statusManagementEnabled)
        {
            StartAllCranes();
        }

        UpdateStatusTexts();

        Debug.Log(
            $"CraneStatusManager：{ActiveCraneCount}基の状態管理を開始しました。"
        );
    }

    private void StopAllCranes()
    {
        if (craneStates == null)
        {
            return;
        }

        foreach (CraneState state in craneStates)
        {
            if (state == null)
            {
                continue;
            }

            if (state.routine != null)
            {
                StopCoroutine(state.routine);
                state.routine = null;
            }
        }
    }

    public void SetStatusManagementEnabled(bool enabled)
    {
        if (statusManagementEnabled == enabled)
        {
            return;
        }

        statusManagementEnabled = enabled;

        if (statusManagementEnabled)
        {
            if (simulatorStarted)
            {
                if (!initialized && !InitializeCranesFromRegistry())
                {
                    return;
                }

                StartAllCranes();
                Debug.Log("CraneStatusManager：状態管理を再開しました");
            }
            else
            {
                Debug.Log(
                    "CraneStatusManager：状態管理ON。" +
                    "ただしシミュレータ開始前なので待機中です"
                );
            }
        }
        else
        {
            StopAllCranes();

            if (resetToAutoWhenDisabled)
            {
                ResetAllCraneStatusToAuto();
            }

            Debug.Log("CraneStatusManager：状態管理を停止しました");
        }

        UpdateStatusTexts();
    }

    private void ResetAllCraneStatusToAuto()
    {
        if (craneStates == null)
        {
            return;
        }

        foreach (CraneState state in craneStates)
        {
            if (state == null)
            {
                continue;
            }

            state.hasError = false;
            state.isStopped = false;
            state.currentErrorType = ErrorType.None;
        }
    }

    private IEnumerator CraneWorkRoutine(CraneState state)
    {
        while (true)
        {
            PhaseSetting setting = GetPhaseSetting(state.currentPhase);

            if (setting == null)
            {
                Debug.LogWarning(
                    state.craneName +
                    " のフェーズ設定が見つかりません: " +
                    state.currentPhase
                );
                state.routine = null;
                yield break;
            }

            float duration = Random.Range(
                setting.minDuration,
                setting.maxDuration
            );
            state.remainingTime = duration;

            bool errorA = Random.value < setting.errorAProbability;
            bool errorB = Random.value < setting.errorBProbability;
            bool errorC = Random.value < setting.errorCProbability;

            // 優先順位：A → B → C
            if (errorA)
            {
                state.currentErrorType = ErrorType.ErrorA;
            }
            else if (errorB)
            {
                state.currentErrorType = ErrorType.ErrorB;
            }
            else if (errorC)
            {
                state.currentErrorType = ErrorType.ErrorC;
            }
            else
            {
                state.currentErrorType = ErrorType.None;
            }

            state.hasError =
                state.currentErrorType != ErrorType.None;
            state.isStopped = state.hasError;

            Debug.Log(
                state.craneName +
                " Phase: " + state.currentPhase +
                " Duration: " + duration.ToString("F1") +
                " Error: " + state.hasError
            );

            // エラーが出た場合は外部から解除されるまで停止
            while (state.isStopped)
            {
                yield return null;
            }

            // 通常進行
            while (state.remainingTime > 0f)
            {
                state.remainingTime -= Time.deltaTime;
                yield return null;
            }

            GoToNextPhase(state);
        }
    }

    private PhaseSetting GetPhaseSetting(WorkPhase phase)
    {
        foreach (PhaseSetting setting in phaseSettings)
        {
            if (setting.phase == phase)
            {
                return setting;
            }
        }

        return null;
    }

    private void GoToNextPhase(CraneState state)
    {
        switch (state.currentPhase)
        {
            case WorkPhase.Move1:
                state.currentPhase = WorkPhase.LiftUp;
                break;

            case WorkPhase.LiftUp:
                state.currentPhase = WorkPhase.Move2;
                break;

            case WorkPhase.Move2:
                if (state.cycleCount >= state.nextPlaceToTrackCycle)
                {
                    state.currentPhase = WorkPhase.PlaceToTrack;
                }
                else
                {
                    state.currentPhase = WorkPhase.Place;
                }
                break;

            case WorkPhase.Place:
                state.cycleCount++;
                state.currentPhase = WorkPhase.Move1;
                break;

            case WorkPhase.PlaceToTrack:
                state.cycleCount = 0;
                state.nextPlaceToTrackCycle = Random.Range(
                    minPlaceToTrackCycle,
                    maxPlaceToTrackCycle + 1
                );
                state.currentPhase = WorkPhase.Move1;
                break;
        }
    }

    // UIボタンなどから呼び出してエラー解除
    public void ResolveError(int craneIndex)
    {
        if (!statusManagementEnabled)
        {
            return;
        }

        if (craneIndex < 0 || craneIndex >= craneStates.Count)
        {
            return;
        }

        craneStates[craneIndex].hasError = false;
        craneStates[craneIndex].isStopped = false;
        craneStates[craneIndex].currentErrorType = ErrorType.None;

        UpdateStatusTexts();

        Debug.Log(
            craneStates[craneIndex].craneName +
            " を自動に復帰しました"
        );
    }

    public CraneState GetCraneState(int craneIndex)
    {
        if (craneIndex < 0 || craneIndex >= craneStates.Count)
        {
            return null;
        }

        return craneStates[craneIndex];
    }

    private void UpdateStatusTexts()
    {
        if (craneStates == null)
        {
            return;
        }

        foreach (StatusUiSet uiSet in GetUiSets())
        {
            UpdateStatusTexts(uiSet);
        }
    }

    private void UpdateStatusTexts(StatusUiSet uiSet)
    {
        if (uiSet == null) return;

        for (int i = 0; i < craneStates.Count; i++)
        {
            CraneState state = craneStates[i];

            if (uiSet.autoStopTexts != null &&
                i < uiSet.autoStopTexts.Length &&
                uiSet.autoStopTexts[i] != null)
            {
                uiSet.autoStopTexts[i].text = state.AutoStopText;
            }

            if (uiSet.phaseTexts != null &&
                i < uiSet.phaseTexts.Length &&
                uiSet.phaseTexts[i] != null)
            {
                uiSet.phaseTexts[i].text =
                    GetPhaseDisplayName(state.currentPhase);
            }

            if (uiSet.autoStopImages != null &&
                i < uiSet.autoStopImages.Length &&
                uiSet.autoStopImages[i] != null)
            {
                uiSet.autoStopImages[i].color =
                    state.isStopped ? stopColor : autoColor;
            }

            if (uiSet.errorTypeTexts != null &&
                i < uiSet.errorTypeTexts.Length &&
                uiSet.errorTypeTexts[i] != null)
            {
                uiSet.errorTypeTexts[i].text = state.isStopped
                    ? GetErrorDisplayName(state.currentErrorType)
                    : "";
            }
        }
    }

    /// <summary>
    /// 登録されているクレーン別UIのうち、使用基数分だけを表示します。
    /// </summary>
    private void UpdateStatusUiVisibility(int activeCraneCount)
    {
        foreach (StatusUiSet uiSet in GetUiSets())
        {
            if (uiSet.craneStatusUiRoots == null) continue;

            for (int i = 0; i < uiSet.craneStatusUiRoots.Length; i++)
            {
                GameObject uiRoot = uiSet.craneStatusUiRoots[i];

                if (uiRoot != null)
                {
                    uiRoot.SetActive(i < activeCraneCount);
                }
            }
        }
    }

    private IEnumerable<StatusUiSet> GetUiSets()
    {
        if (multiDisplayUiSet != null)
        {
            yield return multiDisplayUiSet;
        }

        if (singleDisplayUiSet != null)
        {
            yield return singleDisplayUiSet;
        }
    }

    public void CompleteErrorByCraneIndex(int craneIndex)
    {
        if (!statusManagementEnabled)
        {
            return;
        }

        if (craneStates == null)
        {
            return;
        }

        if (craneIndex < 0 || craneIndex >= craneStates.Count)
        {
            return;
        }

        CraneState state = craneStates[craneIndex];

        state.hasError = false;
        state.isStopped = false;
        state.currentErrorType = ErrorType.None;

        UpdateStatusTexts();
    }

    private string GetPhaseDisplayName(WorkPhase phase)
    {
        switch (phase)
        {
            case WorkPhase.Move1:
                return "移動（つり上げへ）";

            case WorkPhase.LiftUp:
                return "つり上げ";

            case WorkPhase.Move2:
                return "移動（配置へ）";

            case WorkPhase.Place:
                return "配置";

            case WorkPhase.PlaceToTrack:
                return "配置（トレーラ）";

            default:
                return phase.ToString();
        }
    }

    private string GetErrorDisplayName(ErrorType errorType)
    {
        switch (errorType)
        {
            case ErrorType.ErrorA:
                return "人の立ち入り";

            case ErrorType.ErrorB:
                return "つり上げ失敗";

            case ErrorType.ErrorC:
                return "トレーラへの積込";

            default:
                return "";
        }
    }

    public bool TryGetCraneInterventionInfo(
        int craneIndex,
        out WorkPhase phase,
        out ErrorType errorType
    )
    {
        phase = WorkPhase.Move1;
        errorType = ErrorType.None;

        if (craneStates == null)
        {
            return false;
        }

        if (craneIndex < 0 || craneIndex >= craneStates.Count)
        {
            return false;
        }

        CraneState state = craneStates[craneIndex];

        phase = state.currentPhase;
        errorType = state.currentErrorType;

        return true;
    }

    private bool EnsureRegistryIsReady()
    {
        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }

        if (craneRegistry == null)
        {
            Debug.LogError(
                "CraneStatusManagerにCraneRegistryが設定されていません。",
                this
            );
            return false;
        }

        if (craneRegistry.TotalCraneCount == 0)
        {
            craneRegistry.RefreshRegistry();
        }

        if (craneRegistry.TotalCraneCount == 0)
        {
            Debug.LogError(
                "CraneStatusManager：CraneInstanceが見つかりません。",
                this
            );
            return false;
        }

        return true;
    }

    private void OnDisable()
    {
        StopAllCranes();
    }

    private void OnValidate()
    {
        minPlaceToTrackCycle = Mathf.Max(1, minPlaceToTrackCycle);
        maxPlaceToTrackCycle = Mathf.Max(
            minPlaceToTrackCycle,
            maxPlaceToTrackCycle
        );
    }
}
