using UnityEngine;
using UnityEngine.UI;

/// <summary>選択中のクレーンの実際の電流入力ゲートを表示します。</summary>
[DisallowMultipleComponent]
public class LifMagCurrentControlIndicator : MonoBehaviour
{
    [SerializeField] private CraneOperationManager craneOperationManager;
    [SerializeField] private Image stateImage;
    [SerializeField] private Text stateText;
    [SerializeField] private Color availableColor;
    [SerializeField] private Color unavailableColor;

    public bool IsControlAvailable
    {
        get
        {
            if (craneOperationManager == null || !SimulatorStartManager.IsOperationEnabled ||
                ExperimentPauseManager.IsPaused || craneOperationManager.IsOperationInputLocked)
                return false;
            CraneUnit crane = craneOperationManager.CurrentCrane;
            return crane != null && crane.LifMagSystem != null &&
                crane.LifMagSystem.IsElectricCurrentInputReady;
        }
    }

    public static LifMagCurrentControlIndicator Create(CraneOperationManager manager,
        RectTransform anchor, Vector2 offset, Vector2 size, Color available, Color unavailable)
    {
        var obj = new GameObject("ElectricCurrentControlIndicator",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = anchor.gameObject.layer;
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(anchor, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
        Image image = obj.GetComponent<Image>();
        image.raycastTarget = false;

        var labelObj = new GameObject("StateText",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObj.layer = obj.layer;
        var labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        Text label = labelObj.GetComponent<Text>();
        Text existingLabel = anchor.GetComponentInChildren<Text>(true);
        label.font = existingLabel != null && existingLabel.font != null
            ? existingLabel.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 32;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.black;
        label.raycastTarget = false;

        var indicator = obj.AddComponent<LifMagCurrentControlIndicator>();
        indicator.craneOperationManager = manager;
        indicator.stateImage = image;
        indicator.stateText = label;
        indicator.availableColor = available;
        indicator.unavailableColor = unavailable;
        indicator.RefreshDisplay();
        return indicator;
    }

    private void OnEnable() { RefreshDisplay(); }
    private void LateUpdate() { RefreshDisplay(); }

    public void RefreshDisplay()
    {
        bool available = IsControlAvailable;
        if (stateImage != null) stateImage.color = available ? availableColor : unavailableColor;
        if (stateText != null) stateText.text = available ? "電流制御\n可" : "電流制御\n不可";
    }
}
