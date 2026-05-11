using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifMagCurrentButton : MonoBehaviour
{
    [Header("対象のLifMagSystem")]
    [SerializeField] private LifMagSystem lifMagSystem;

    [Header("対象リフマグ番号")]
    [SerializeField] private int lifMagIndex = 0;

    [Header("UI")]
    [SerializeField] private Button button;
    [SerializeField] private Text labelText;

    [Header("色設定")]
    [SerializeField] private Color offColor = Color.white;
    [SerializeField] private Color onColor = Color.yellow;

    private bool isOn = false;
    private Image buttonImage;

    private void Start()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (button != null)
        {
            button.onClick.AddListener(ToggleCurrent);
            buttonImage = button.GetComponent<Image>();
        }

        isOn = false;

        if (lifMagSystem != null)
        {
            lifMagSystem.SetLifMagCurrent(lifMagIndex, false);
        }

        UpdateView();
    }

    private void ToggleCurrent()
    {
        isOn = !isOn;

        if (lifMagSystem != null)
        {
            lifMagSystem.SetLifMagCurrent(lifMagIndex, isOn);
        }

        UpdateView();
    }

    private void UpdateView()
    {
        if (buttonImage != null)
        {
            buttonImage.color = isOn ? onColor : offColor;
        }

        if (labelText != null)
        {
            labelText.text = $"{(isOn ? "ON" : "OFF")}";
        }
    }
}
