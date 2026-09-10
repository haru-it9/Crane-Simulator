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

    public float CurrentX { get; private set; }
    public float CurrentZ { get; private set; }
    public float CurrentDisplayWeightTon { get; private set; }

    private void Update()
    {
        UpdatePositionText();
        UpdateWeightText();
    }

    public void SetTarget(
        Transform newTargetTransform,
        LifMagSystem newLifMagSystem
    )
    {
        targetTransform = newTargetTransform;
        lifMagSystem = newLifMagSystem;

        wasHoldingLastFrame = false;
        hasReachedMaxWeight = false;
        shouldResetWeight = false;
        CurrentDisplayWeightTon = 0f;

        UpdatePositionText();
        ResetWeightDisplay();
    }

    private void UpdatePositionText()
    {
        if (targetTransform == null) return;

        Vector3 pos = targetTransform.position;

        CurrentX = pos.x + 20f;
        CurrentZ = pos.z * -1f + 50f;

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
