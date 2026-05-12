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
    [SerializeField] private float weightDisplayHeight = 1.0f;

    [Header("重量0表示時")]
    [SerializeField] private string zeroWeightText = "0.0 t";

    private float liftStartY;
    private bool wasHoldingLastFrame = false;

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
        }

        wasHoldingLastFrame = isHolding;

        // 非吸着時
        if (!isHolding)
        {
            CurrentDisplayWeightTon = 0f;
            weightText.text = zeroWeightText;
            return;
        }

        // 実重量計算
        float actualWeight = 0f;

        foreach (GameObject board in boards)
        {
            if (board == null) continue;

            actualWeight += CalculateBoardWeight(board);
        }

        // 持ち上げ高さ
        float liftedHeight =
            Mathf.Max(0f, targetTransform.position.y - liftStartY);

        // 0～1
        float ratio =
            Mathf.Clamp01(liftedHeight / weightDisplayHeight);

        // 見かけ重量
        float displayWeight = actualWeight * ratio / 1000f;

        CurrentDisplayWeightTon = displayWeight;
        
        weightText.text = $"{displayWeight:F2} t";
    }

    private float CalculateBoardWeight(GameObject board)
    {
        Collider col = board.GetComponent<Collider>();

        if (col == null)
        {
            return 0f;
        }

        Bounds b = col.bounds;

        float volume =
            b.size.x *
            b.size.y *
            b.size.z;

        float weight = volume * boardDensity;

        return weight;
    }
}
