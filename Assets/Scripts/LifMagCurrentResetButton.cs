using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifMagCurrentResetButton : MonoBehaviour
{
    [Header("対象のLifMagSystem")]
    [SerializeField] private LifMagSystem lifMagSystem;

    [Header("OFFに戻すLifMagボタン一覧")]
    [SerializeField] private LifMagCurrentButton[] lifMagButtons;

    [Header("一括OFFボタン")]
    [SerializeField] private Button resetButton;

    private void Start()
    {
        if (resetButton == null)
            resetButton = GetComponent<Button>();

        if (resetButton != null)
            resetButton.onClick.AddListener(ResetAll);
    }

    public void ResetAll()
    {
        if (lifMagSystem != null)
        {
            lifMagSystem.DetachAllFromButton();
            lifMagSystem.ResetLifMagDisplayAccumValues();
        }

        foreach (LifMagCurrentButton btn in lifMagButtons)
        {
            if (btn == null) continue;
            btn.ForceOff();
        }
    }
}
