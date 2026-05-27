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

        [HideInInspector] public Coroutine routine;

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

    [Header("クレーン台数")]
    [SerializeField] private int craneCount = 6;

    [Header("フェーズ設定")]
    [SerializeField] private List<PhaseSetting> phaseSettings = new List<PhaseSetting>();

    [Header("各クレーンの現在状態")]
    [SerializeField] private List<CraneState> craneStates = new List<CraneState>();

    [Header("PlaceToTrack発生サイクル範囲")]
    [SerializeField] private int minPlaceToTrackCycle = 3;
    [SerializeField] private int maxPlaceToTrackCycle = 6;

    [Header("状態表示Text")]
    [SerializeField] private Text[] autoStopTexts;

    [Header("状態表示RawImage")]
    [SerializeField] private RawImage[] autoStopImages;

    [Header("状態色")]
    [SerializeField] private Color autoColor = new Color(0.3f, 0.7f, 1.0f);

    [SerializeField] private Color stopColor = Color.red;

    [Header("フェーズ表示Text")]
    [SerializeField] private Text[] phaseTexts;

    [Header("エラー種別表示Text")]
    [SerializeField] private Text[] errorTypeTexts;
    

    private void Start()
    {
        InitializeCranes();
        StartAllCranes();
    }

    private void Update()
    {
        UpdateStatusTexts();
    }

    private void InitializeCranes()
    {
        craneStates.Clear();

        for (int i = 0; i < craneCount; i++)
        {
            CraneState state = new CraneState();
            state.craneName = "Crane_" + (i + 1);
            state.currentPhase = WorkPhase.Move1;
            state.hasError = false;
            state.isStopped = false;
            state.cycleCount = 0;
            state.nextPlaceToTrackCycle = Random.Range(minPlaceToTrackCycle, maxPlaceToTrackCycle + 1);

            craneStates.Add(state);
        }
    }

    private void StartAllCranes()
    {
        foreach (CraneState state in craneStates)
        {
            state.routine = StartCoroutine(CraneWorkRoutine(state));
        }
    }

    private IEnumerator CraneWorkRoutine(CraneState state)
    {
        while (true)
        {
            PhaseSetting setting = GetPhaseSetting(state.currentPhase);

            if (setting == null)
            {
                Debug.LogWarning(state.craneName + " のフェーズ設定が見つかりません: " + state.currentPhase);
                yield break;
            }

            float duration = Random.Range(setting.minDuration, setting.maxDuration);
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

            state.hasError = state.currentErrorType != ErrorType.None;
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
                state.nextPlaceToTrackCycle =
                    Random.Range(minPlaceToTrackCycle, maxPlaceToTrackCycle + 1);

                state.currentPhase = WorkPhase.Move1;
                break;
        }
    }

    // UIボタンなどから呼び出してエラー解除
    public void ResolveError(int craneIndex)
    {
        if (craneIndex < 0 || craneIndex >= craneStates.Count) return;

        craneStates[craneIndex].hasError = false;
        craneStates[craneIndex].isStopped = false;

        craneStates[craneIndex].currentErrorType = ErrorType.None;

        Debug.Log(craneStates[craneIndex].craneName + " を自動に復帰しました");
    }

    public CraneState GetCraneState(int craneIndex)
    {
        if (craneIndex < 0 || craneIndex >= craneStates.Count) return null;
        return craneStates[craneIndex];
    }

    private void UpdateStatusTexts()
    {
        for (int i = 0; i < craneStates.Count; i++)
        {
            if (autoStopTexts != null && i < autoStopTexts.Length && autoStopTexts[i] != null)
            {
                autoStopTexts[i].text = craneStates[i].AutoStopText;
            }

            if (phaseTexts != null && i < phaseTexts.Length && phaseTexts[i] != null)
            {
                phaseTexts[i].text = GetPhaseDisplayName(craneStates[i].currentPhase);
            }

            // RawImage色更新
            if (autoStopImages != null &&
                i < autoStopImages.Length &&
                autoStopImages[i] != null)
            {
                autoStopImages[i].color =
                    craneStates[i].isStopped ? stopColor : autoColor;
            }

            if (errorTypeTexts != null && i < errorTypeTexts.Length && errorTypeTexts[i] != null)
            {
                if (craneStates[i].isStopped)
                {
                    errorTypeTexts[i].text = GetErrorDisplayName(craneStates[i].currentErrorType);
                }
                else
                {
                    errorTypeTexts[i].text = "";
                }
            }
        }
    }

    public void CompleteErrorByCraneIndex(int craneIndex)
    {
        if (craneStates == null) return;
        if (craneIndex < 0 || craneIndex >= craneStates.Count) return;

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
}
