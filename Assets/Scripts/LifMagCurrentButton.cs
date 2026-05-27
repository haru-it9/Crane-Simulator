using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifMagCurrentButton : MonoBehaviour
{
    [Header("CraneOperationManager")]
    [SerializeField] private CraneOperationManager craneOperationManager;

    [Header("対象リフマグ番号")]
    [SerializeField] private int lifMagIndex = 0;

    [Header("UI")]
    [SerializeField] private Button button;
    [SerializeField] private Text labelText;

    [Header("電流累積値表示")]
    [SerializeField] private Text currentValueText;
    [SerializeField] private string currentValueUnit = "";

    [Header("色設定")]
    [SerializeField] private Color offColor = Color.white;
    [SerializeField] private Color onColor = Color.yellow;

    private bool isOn = false;
    private Image buttonImage;

    private void Start()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.AddListener(TurnOn);
            buttonImage = button.GetComponent<Image>();
        }

        ForceOff();
    }

    private void TurnOn()
    {
        isOn = true;

        if (craneOperationManager != null)
        craneOperationManager.SetCurrentCraneLifMagCurrent(lifMagIndex, true);

        UpdateView();
    }

    public void ForceOff()
    {
        isOn = false;

        if (craneOperationManager != null)
            craneOperationManager.SetCurrentCraneLifMagCurrent(lifMagIndex, false);

        UpdateView();
    }

    public void SetViewOnly(bool on)
    {
        isOn = on;
        UpdateView();
    }

    public void SetCurrentValueView(float value)
    {
        if (currentValueText != null)
        {
            currentValueText.text = value.ToString("F1") + currentValueUnit;
        }
    }

    private void UpdateView()
    {
        if (buttonImage != null)
            buttonImage.color = isOn ? onColor : offColor;

        if (labelText != null)
            labelText.text = $"{(isOn ? "ON" : "OFF")}";

        if (button != null)
            button.interactable = !isOn;
    }
}
