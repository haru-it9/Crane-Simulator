using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CraneInformationDisplay : MonoBehaviour
{
    [System.Serializable]
    public class InformationTextSet
    {
        public Text xText;
        public Text zText;
        public Text weightText;
    }

    [Header("座標取得対象")]
    [SerializeField] private Transform targetTransform;

    [Header("LifMagSystem")]
    [SerializeField] private LifMagSystem lifMagSystem;

    [Header("表示モード別UI Text")]
    [SerializeField] private InformationTextSet multiDisplayTexts =
        new InformationTextSet();

    [SerializeField] private InformationTextSet singleDisplayTexts =
        new InformationTextSet();

    [Tooltip("Task Switch Display専用の現在座標・実重量Textです。")]
    [SerializeField] private InformationTextSet taskSwitchDisplayTexts =
        new InformationTextSet();

    [Header("作業判定連携")]
    [SerializeField] private CraneWorkPhaseTracker workPhaseTracker;
    [SerializeField] private CraneWorkLoadPlanManager loadPlanManager;

    [Header("目標範囲内の表示色")]
    [SerializeField]
    private Color achievedValueColor =
        new Color(0.65f, 1f, 0.10f, 1f);

    [SerializeField]
    [Min(0f)]
    private float fineAlignTolerance = 0.05f;

    [SerializeField]
    [Min(0f)]
    private float weightToleranceKg = 100f;

    [Tooltip(
        "Moveで成立した座標色を保持するMainLifMagのLocal Y境界です。" +
        "この値以下まで下降すると通常色へ戻します。"
    )]
    [SerializeField]
    private float coordinateHighlightReleaseLocalY = -1.66f;

    [Header("板密度 [kg/m^3]")]
    [SerializeField] private float boardDensity = 7850f;

    [Header("重量表示演出")]
    [SerializeField] private float weightDisplayHeight = 0.2f;

    [Header("重量0表示時")]
    [SerializeField] private string zeroWeightText = "0.00 t";

    private float liftStartY;
    private bool wasHoldingLastFrame;
    private bool hasReachedMaxWeight;
    private bool shouldResetWeight;

    private struct WeightDisplayState
    {
        public float liftStartY;
        public bool wasHolding;
        public bool hasReachedMaxWeight;
        public bool shouldResetWeight;
        public GameObject lastAttachedBoard;
    }

    private readonly Dictionary<LifMagSystem, WeightDisplayState>
        weightDisplayStates = new Dictionary<LifMagSystem, WeightDisplayState>();

    private readonly Dictionary<Text, Color> defaultValueColors =
        new Dictionary<Text, Color>();

    private CraneUnit craneUnit;
    private bool hasObservedMajorPhase;
    private CraneStatusManager.WorkPhase lastObservedMajorPhase;
    private bool retainedXHighlight;
    private bool retainedZHighlight;
    private bool verticalPhaseHighlightReleased;

    public float CurrentX { get; private set; }
    public float CurrentZ { get; private set; }
    public float CurrentDisplayWeightTon { get; private set; }

    private void Awake()
    {
        ResolveWorkReferences(lifMagSystem);
        CacheDefaultValueColors();
    }

    private void Update()
    {
        UpdatePositionText();
        UpdateWeightText();
        UpdateAchievementColors();
    }

    private void OnDisable()
    {
        RestoreDefaultValueColors();
    }

    public void SetTarget(
        Transform newTargetTransform,
        LifMagSystem newLifMagSystem
    )
    {
        RestoreDefaultValueColors();

        // 共有UIの操作対象を変更しても、各クレーンの吊上げ表示状態を保持します。
        if (lifMagSystem != null)
        {
            weightDisplayStates[lifMagSystem] = new WeightDisplayState
            {
                liftStartY = liftStartY,
                wasHolding = wasHoldingLastFrame,
                hasReachedMaxWeight = hasReachedMaxWeight,
                shouldResetWeight = shouldResetWeight,
                lastAttachedBoard = lifMagSystem.LastAttachedBoard
            };
        }

        targetTransform = newTargetTransform;
        lifMagSystem = newLifMagSystem;
        ResolveWorkReferences(newLifMagSystem);
        ResetCoordinateHighlightState();

        bool isHolding = lifMagSystem != null && lifMagSystem.HasAttachedBoard;

        if (lifMagSystem != null &&
            weightDisplayStates.TryGetValue(lifMagSystem, out WeightDisplayState state) &&
            state.wasHolding == isHolding &&
            state.lastAttachedBoard == lifMagSystem.LastAttachedBoard)
        {
            liftStartY = state.liftStartY;
            wasHoldingLastFrame = state.wasHolding;
            hasReachedMaxWeight = state.hasReachedMaxWeight;
            shouldResetWeight = state.shouldResetWeight;
        }
        else
        {
            // 初めて選択した時点で保持中なら、新規吸着として扱わず実重量を表示します。
            liftStartY = targetTransform != null ? targetTransform.position.y : 0f;
            wasHoldingLastFrame = isHolding;
            hasReachedMaxWeight = isHolding;
            shouldResetWeight = false;
        }

        CurrentDisplayWeightTon = 0f;

        UpdatePositionText();

        if (lifMagSystem != null)
        {
            UpdateWeightText();
        }
        else
        {
            ResetWeightDisplay();
        }
    }

    private void UpdatePositionText()
    {
        if (targetTransform == null) return;

        Vector3 pos = targetTransform.position;

        CurrentX = pos.x + 20f;
        CurrentZ = pos.z * -1f + 250f;

        foreach (InformationTextSet textSet in GetTextSets())
        {
            if (textSet.xText != null)
            {
                textSet.xText.text = $"{CurrentX:F2}";
            }

            if (textSet.zText != null)
            {
                textSet.zText.text = $"{CurrentZ:F2}";
            }
        }
    }

    private void UpdateWeightText()
    {
        if (lifMagSystem == null) return;

        IReadOnlyList<GameObject> boards = lifMagSystem.AttachedBoards;
        bool isHolding = boards != null && boards.Count > 0;

        // 吸着開始瞬間
        if (isHolding && !wasHoldingLastFrame)
        {
            if (targetTransform != null)
            {
                liftStartY = targetTransform.position.y;
            }

            hasReachedMaxWeight = false;
            shouldResetWeight = false;
            CurrentDisplayWeightTon = 0f;

            if (lifMagSystem.HasInterventionForcedAttachedBoard())
            {
                float forcedWeightKg =
                    lifMagSystem.GetAttachedTotalWeightKgForDisplay();
                CurrentDisplayWeightTon = forcedWeightKg / 1000f;
                hasReachedMaxWeight = true;
                SetWeightText($"{CurrentDisplayWeightTon:F2} t");
            }
        }

        wasHoldingLastFrame = isHolding;

        if (!isHolding)
        {
            shouldResetWeight = false;
            ResetWeightDisplay();
            return;
        }

        if (shouldResetWeight)
        {
            ResetWeightDisplay();
            return;
        }

        float actualWeightKg =
            lifMagSystem.GetAttachedTotalWeightKgForDisplay();
        float actualWeightTon = actualWeightKg / 1000f;

        if (lifMagSystem.HasInterventionForcedAttachedBoard())
        {
            CurrentDisplayWeightTon = actualWeightTon;
            SetWeightText($"{CurrentDisplayWeightTon:F2} t");
            return;
        }

        if (hasReachedMaxWeight)
        {
            CurrentDisplayWeightTon = actualWeightTon;
            SetWeightText($"{CurrentDisplayWeightTon:F2} t");
            return;
        }

        if (targetTransform == null)
        {
            CurrentDisplayWeightTon = actualWeightTon;
            SetWeightText($"{CurrentDisplayWeightTon:F2} t");
            return;
        }

        float liftedHeight = Mathf.Max(
            0f,
            targetTransform.position.y - liftStartY
        );
        float ratio = Mathf.Clamp01(
            liftedHeight / weightDisplayHeight
        );

        CurrentDisplayWeightTon = actualWeightTon * ratio;

        if (ratio >= 1f)
        {
            hasReachedMaxWeight = true;
            CurrentDisplayWeightTon = actualWeightTon;
        }

        SetWeightText($"{CurrentDisplayWeightTon:F2} t");
    }

    private void ResetWeightDisplay()
    {
        CurrentDisplayWeightTon = 0f;
        hasReachedMaxWeight = false;
        SetWeightText(zeroWeightText);
    }

    private void SetWeightText(string value)
    {
        foreach (InformationTextSet textSet in GetTextSets())
        {
            if (textSet.weightText != null)
            {
                textSet.weightText.text = value;
            }
        }
    }

    private void UpdateAchievementColors()
    {
        bool isMonitoring =
            workPhaseTracker != null &&
            workPhaseTracker.IsMonitoring;

        UpdateCoordinateAchievementState(
            isMonitoring,
            out bool xWithinTarget,
            out bool zWithinTarget
        );

        bool weightWithinTarget = false;

        if (isMonitoring &&
            TryGetCurrentWeightTargetKg(out float targetWeightKg))
        {
            float displayedWeightKg =
                CurrentDisplayWeightTon * 1000f;

            weightWithinTarget =
                Mathf.Abs(displayedWeightKg - targetWeightKg) <=
                Mathf.Max(0f, weightToleranceKg);
        }

        foreach (InformationTextSet textSet in GetTextSets())
        {
            if (textSet == null)
            {
                continue;
            }

            SetAchievementColor(textSet.xText, xWithinTarget);
            SetAchievementColor(textSet.zText, zWithinTarget);
            SetAchievementColor(
                textSet.weightText,
                weightWithinTarget
            );
        }
    }

    private void UpdateCoordinateAchievementState(
        bool isMonitoring,
        out bool xWithinTarget,
        out bool zWithinTarget
    )
    {
        xWithinTarget = false;
        zWithinTarget = false;

        if (!isMonitoring || workPhaseTracker == null)
        {
            ResetCoordinateHighlightState();
            return;
        }

        CraneStatusManager.WorkPhase phase =
            workPhaseTracker.CurrentMajorPhase;

        bool phaseChanged =
            !hasObservedMajorPhase ||
            phase != lastObservedMajorPhase;

        if (phaseChanged)
        {
            hasObservedMajorPhase = true;
            lastObservedMajorPhase = phase;
            verticalPhaseHighlightReleased = false;

            if (IsVerticalWorkPhase(phase))
            {
                float tolerance = Mathf.Max(
                    0f,
                    fineAlignTolerance
                );

                retainedXHighlight =
                    workPhaseTracker.CurrentTargetErrorX <= tolerance;
                retainedZHighlight =
                    workPhaseTracker.CurrentTargetErrorZ <= tolerance;

                if (!TryGetMainLifMagLocalY(out float localY) ||
                    localY <= coordinateHighlightReleaseLocalY)
                {
                    verticalPhaseHighlightReleased = true;
                    retainedXHighlight = false;
                    retainedZHighlight = false;
                }
            }
            else
            {
                retainedXHighlight = false;
                retainedZHighlight = false;
            }
        }

        bool isMovePhase =
            phase == CraneStatusManager.WorkPhase.Move1 ||
            phase == CraneStatusManager.WorkPhase.Move2;

        if (isMovePhase)
        {
            float tolerance = Mathf.Max(0f, fineAlignTolerance);

            xWithinTarget =
                workPhaseTracker.CurrentTargetErrorX <= tolerance;
            zWithinTarget =
                workPhaseTracker.CurrentTargetErrorZ <= tolerance;

            retainedXHighlight = xWithinTarget;
            retainedZHighlight = zWithinTarget;
            verticalPhaseHighlightReleased = false;
            return;
        }

        if (!IsVerticalWorkPhase(phase) ||
            verticalPhaseHighlightReleased)
        {
            return;
        }

        if (!TryGetMainLifMagLocalY(out float currentLocalY) ||
            currentLocalY <= coordinateHighlightReleaseLocalY)
        {
            verticalPhaseHighlightReleased = true;
            retainedXHighlight = false;
            retainedZHighlight = false;
            return;
        }

        xWithinTarget = retainedXHighlight;
        zWithinTarget = retainedZHighlight;
    }

    private static bool IsVerticalWorkPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        return
            phase == CraneStatusManager.WorkPhase.LiftUp ||
            phase == CraneStatusManager.WorkPhase.Place ||
            phase == CraneStatusManager.WorkPhase.PlaceToTrack;
    }

    private bool TryGetMainLifMagLocalY(out float localY)
    {
        localY = 0f;

        return craneUnit != null &&
               craneUnit.TryGetMainLifMagLocalY(out localY);
    }

    private void ResetCoordinateHighlightState()
    {
        hasObservedMajorPhase = false;
        retainedXHighlight = false;
        retainedZHighlight = false;
        verticalPhaseHighlightReleased = false;
    }

    private bool TryGetCurrentWeightTargetKg(
        out float targetWeightKg
    )
    {
        targetWeightKg = 0f;

        if (workPhaseTracker == null || loadPlanManager == null)
        {
            return false;
        }

        switch (workPhaseTracker.CurrentMajorPhase)
        {
            case CraneStatusManager.WorkPhase.LiftUp:
                return loadPlanManager.TryGetPickupTargetWeightKg(
                    out targetWeightKg
                );

            case CraneStatusManager.WorkPhase.Place:
            case CraneStatusManager.WorkPhase.PlaceToTrack:
                return loadPlanManager.TryGetTargetRemainingWeightKg(
                    out targetWeightKg
                );

            default:
                return false;
        }
    }

    private void SetAchievementColor(Text text, bool achieved)
    {
        if (text == null)
        {
            return;
        }

        if (!defaultValueColors.ContainsKey(text))
        {
            defaultValueColors.Add(text, text.color);
        }

        text.color = achieved
            ? achievedValueColor
            : defaultValueColors[text];
    }

    private void CacheDefaultValueColors()
    {
        foreach (InformationTextSet textSet in GetTextSets())
        {
            if (textSet == null)
            {
                continue;
            }

            CacheDefaultValueColor(textSet.xText);
            CacheDefaultValueColor(textSet.zText);
            CacheDefaultValueColor(textSet.weightText);
        }
    }

    private void CacheDefaultValueColor(Text text)
    {
        if (text != null && !defaultValueColors.ContainsKey(text))
        {
            defaultValueColors.Add(text, text.color);
        }
    }

    private void RestoreDefaultValueColors()
    {
        foreach (KeyValuePair<Text, Color> entry in defaultValueColors)
        {
            if (entry.Key != null)
            {
                entry.Key.color = entry.Value;
            }
        }
    }

    private void ResolveWorkReferences(LifMagSystem sourceLifMag)
    {
        workPhaseTracker = null;
        loadPlanManager = null;
        craneUnit = null;

        if (sourceLifMag == null)
        {
            return;
        }

        CraneInstance craneInstance =
            sourceLifMag.GetComponentInParent<CraneInstance>();

        if (craneInstance == null)
        {
            return;
        }

        workPhaseTracker =
            craneInstance.GetComponent<CraneWorkPhaseTracker>();

        if (workPhaseTracker == null)
        {
            workPhaseTracker =
                craneInstance.GetComponentInChildren<
                    CraneWorkPhaseTracker
                >(true);
        }

        loadPlanManager =
            craneInstance.GetComponent<CraneWorkLoadPlanManager>();

        if (loadPlanManager == null)
        {
            loadPlanManager =
                craneInstance.GetComponentInChildren<
                    CraneWorkLoadPlanManager
                >(true);
        }

        craneUnit = craneInstance.CraneUnit;

        if (craneUnit == null)
        {
            craneUnit = craneInstance.GetComponentInChildren<
                CraneUnit
            >(true);
        }
    }

    private IEnumerable<InformationTextSet> GetTextSets()
    {
        if (multiDisplayTexts != null)
        {
            yield return multiDisplayTexts;
        }

        if (singleDisplayTexts != null)
        {
            yield return singleDisplayTexts;
        }

        if (taskSwitchDisplayTexts != null)
        {
            yield return taskSwitchDisplayTexts;
        }
    }

    public void NotifyDownwardMovementStopped()
    {
        shouldResetWeight = true;
    }

    public void NotifyUpwardMovementStarted()
    {
        if (!shouldResetWeight) return;

        shouldResetWeight = false;

        if (targetTransform != null)
        {
            liftStartY = targetTransform.position.y;
        }

        hasReachedMaxWeight = false;
    }

    private void OnValidate()
    {
        fineAlignTolerance = Mathf.Max(0f, fineAlignTolerance);
        weightToleranceKg = Mathf.Max(0f, weightToleranceKg);
    }

    private float CalculateBoardWeight(GameObject board)
    {
        if (board == null)
        {
            return 0f;
        }

        BoardInfo boardInfo = board.GetComponent<BoardInfo>();

        if (boardInfo != null)
        {
            return boardInfo.Weight;
        }

        Collider col = board.GetComponent<Collider>();

        if (col == null)
        {
            return 0f;
        }

        Bounds bounds = col.bounds;
        float volume = bounds.size.x * bounds.size.y * bounds.size.z;
        return volume * boardDensity;
    }
}
