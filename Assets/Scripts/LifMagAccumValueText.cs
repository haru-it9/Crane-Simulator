using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifMagAccumValueText : MonoBehaviour
{
    [Header("対象のLifMagSystem")]
    [SerializeField] private LifMagSystem lifMagSystem;

    [Header("対象リフマグ番号")]
    [SerializeField] private int lifMagIndex = 0;

    [Header("表示Text")]
    [SerializeField] private Text valueText;

    public float CurrentValue { get; private set; }

    private void Start()
    {
        if (valueText == null)
        {
            valueText = GetComponent<Text>();
        }
    }

    private void Update()
    {
        if (valueText == null) return;

        if (lifMagSystem == null)
        {
            CurrentValue = 0f;
            valueText.text = "0.0";
            return;
        }

        if (!lifMagSystem.IsLifMagCurrentOn(lifMagIndex))
        {
            CurrentValue = 0f;
            valueText.text = "0.0";
            return;
        }

        CurrentValue = lifMagSystem.GetLifMagDisplayAccumValue(lifMagIndex);
        valueText.text = CurrentValue.ToString("F2");
    }
}
