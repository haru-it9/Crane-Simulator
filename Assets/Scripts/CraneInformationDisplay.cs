using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CraneInformationDisplay : MonoBehaviour
{
    [Header("座標を取得する対象")]
    public Transform targetTransform;

    [Header("X座標表示")]
    public Text xText;

    [Header("Z座標表示")]
    public Text zText;

    void Update()
    {
        if (targetTransform == null) return;

        Vector3 pos = targetTransform.position;

        // 小数点以下第一位まで表示
        if (xText != null)
        {
            xText.text = $"{pos.x:F1}";
        }

        if (zText != null)
        {
            zText.text = $"{pos.z:F1}";
        }
    }
}
