using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CraneInformationDisplay : MonoBehaviour
{
    [Header("座標取得対象")]
    [SerializeField] private Transform targetTransform;

    [Header("LifMagSystem")]
    [SerializeField] private LifMagSystem lifMagSystem;

    [Header("UI Text")]
    [SerializeField] private Text xText;
    [SerializeField] private Text zText;
    [SerializeField] private Text weightText;

    [Header("板密度 [kg/m^3]")]
    [SerializeField] private float boardDensity = 7850f;

    [Header("重量表示演出")]
    [SerializeField] private float weightDisplayHeight = 0.2f;

    [Header("重量0表示時")]
    [SerializeField] private string zeroWeightText = "0.00 t";

    private float liftStartY;
    private bool wasHoldingLastFrame = false;

    private bool hasReachedMaxWeight = false;
    private bool shouldResetWeight = false;

    public float CurrentX { get; private set; }
    public float CurrentZ { get; private set; }
    public float CurrentDisplayWeightTon { get; private set; }

    private void Update()
    {
        UpdatePositionText();
        UpdateWeightText();
    }

    private void UpdatePositionText()
    {
        if (targetTransform == null) return;

        Vector3 pos = targetTransform.position;

        CurrentX = pos.x;
        CurrentZ = pos.z;

        if (xText != null)
        {
            xText.text = $"{CurrentX:F2}";
        }

        if (zText != null)
        {
            zText.text = $"{CurrentZ:F2}";
        }
    }

    private void UpdateWeightText()
    {
        if (weightText == null) return;
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
        }

        wasHoldingLastFrame = isHolding;

        // 非吸着時
        if (!isHolding)
        {
            shouldResetWeight = false;
            ResetWeightDisplay();
            return;
        }

        // 板が場の板やステージに接触した後
        if (shouldResetWeight)
        {
            ResetWeightDisplay();
            return;
        }

        // 実重量計算
        float actualWeightKg = 0f;

        foreach (GameObject board in boards)
        {
            if (board == null) continue;

            actualWeightKg += CalculateBoardWeight(board);
        }

        float actualWeightTon = actualWeightKg / 1000f;

        // 一度最大表示に達したら、その後は高さで減らさない
        if (hasReachedMaxWeight)
        {
            CurrentDisplayWeightTon = actualWeightTon;
            weightText.text = $"{CurrentDisplayWeightTon:F2} t";
            return;
        }

        // 持ち上げ高さ
        float liftedHeight = Mathf.Max(0f, targetTransform.position.y - liftStartY);

        float ratio = Mathf.Clamp01(liftedHeight / weightDisplayHeight);

        CurrentDisplayWeightTon = actualWeightTon * ratio;

        if (ratio >= 1f)
        {
            hasReachedMaxWeight = true;
            CurrentDisplayWeightTon = actualWeightTon;
        }

        weightText.text = $"{CurrentDisplayWeightTon:F2} t";
    }

    private void ResetWeightDisplay()
    {
        CurrentDisplayWeightTon = 0f;
        hasReachedMaxWeight = false;

        if (weightText != null)
        {
            weightText.text = zeroWeightText;
        }
    }

    // 追加：下降停止が確定したときに外部から呼ぶ
    public void NotifyDownwardMovementStopped()
    {
        shouldResetWeight = true;
    }

    public void NotifyUpwardMovementStarted()
    {
        // 0表示解除
        if (shouldResetWeight)
        {
            shouldResetWeight = false;

            // 再度持ち上げ開始位置を記録
            if (targetTransform != null)
            {
                liftStartY = targetTransform.position.y;
            }

            // もう一度 0→重量 の線形変化を行う
            hasReachedMaxWeight = false;
        }
    }

    private float CalculateBoardWeight(GameObject board)
    {
        Collider col = board.GetComponent<Collider>();

        if (col == null)
        {
            return 0f;
        }

        Bounds b = col.bounds;

        float volume = b.size.x * b.size.y * b.size.z;

        float weight = volume * boardDensity;

        return weight;
    }
}
