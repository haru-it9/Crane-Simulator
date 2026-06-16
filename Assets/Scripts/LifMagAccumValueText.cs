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

    [Header("表示設定")]
    [SerializeField] private string zeroText = "0.0";
    [SerializeField] private bool showUnitInInputMode = true;
    [SerializeField] private string currentUnit = " A";

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
            valueText.text = zeroText;
            return;
        }

        // 対象リフマグの電流がOFFなら0表示
        if (!currentLifMagSystem.IsLifMagCurrentOn(lifMagIndex))
        {
            CurrentValue = 0f;
            valueText.text = zeroText;
            return;
        }

        // 入力値モードなら、累積値ではなく現在の電流値を表示
        if (currentLifMagSystem.IsInputValueLiftMode)
        {
            CurrentValue = currentLifMagSystem.CurrentElectricCurrentA;

            if (showUnitInInputMode)
            {
                valueText.text = CurrentValue.ToString("F1") + currentUnit;
            }
            else
            {
                valueText.text = CurrentValue.ToString("F1");
            }

            return;
        }

        // 従来の累積値モード
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