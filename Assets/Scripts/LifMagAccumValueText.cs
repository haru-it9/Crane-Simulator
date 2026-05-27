using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifMagAccumValueText : MonoBehaviour
{
    [Header("CraneOperationManager")]
    [SerializeField] private CraneOperationManager craneOperationManager;

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

        LifMagSystem currentLifMagSystem = GetCurrentLifMagSystem();

        if (currentLifMagSystem == null)
        {
            CurrentValue = 0f;
            valueText.text = "0.0";
            return;
        }

        if (!currentLifMagSystem.IsLifMagCurrentOn(lifMagIndex))
        {
            CurrentValue = 0f;
            valueText.text = "0.0";
            return;
        }

        CurrentValue = currentLifMagSystem.GetLifMagDisplayAccumValue(lifMagIndex);
        valueText.text = CurrentValue.ToString("F2");
    }
    
    private LifMagSystem GetCurrentLifMagSystem()
    {
        if (craneOperationManager == null) return null;
        if (craneOperationManager.CurrentCrane == null) return null;

        return craneOperationManager.CurrentCrane.LifMagSystem;
    }
}
