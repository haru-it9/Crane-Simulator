using System;
using UnityEngine;
using UnityEngine.UI;

public class DisplayLayoutManager : MonoBehaviour
{
    public enum DisplayLayoutMode
    {
        MultiDisplay,
        SingleDisplay
    }

    public enum CameraNameMatchMode
    {
        Exact,
        StartsWith,
        Contains
    }

    [Serializable]
    public class CameraLayoutRule
    {
        [Header("カメラ名の検索条件")]
        [Tooltip("Cameraコンポーネントが付いているGameObjectの名前を指定します。")]
        public string cameraName = "Camera";

        [Tooltip("完全一致、前方一致、部分一致から選択します。")]
        public CameraNameMatchMode nameMatchMode = CameraNameMatchMode.Exact;

        [Header("複数画面モード")]
        [Tooltip("複数画面モードで、この種類のCameraを描画するかどうかです。")]
        public bool enableInMultiDisplay = true;

        [Tooltip("0 = Display 1、1 = Display 2、...、6 = Display 7")]
        [Range(0, 7)]
        public int multiTargetDisplay = 0;

        [Tooltip("複数画面モードでのViewport Rectです。")]
        public Rect multiViewportRect = new Rect(0f, 0f, 1f, 1f);

        [Header("単一画面モード")]
        [Tooltip("単一画面モードで、この種類のCameraを描画するかどうかです。")]
        public bool enableInSingleDisplay = true;

        [Tooltip("単一画面モードでのDisplay 1上のViewport Rectです。")]
        public Rect singleViewportRect = new Rect(0f, 0f, 1f, 1f);
    }

    [Header("カメラ名ごとのレイアウト設定")]
    [Tooltip("上から順に照合し、最初に一致したルールを適用します。")]
    [SerializeField]
    private CameraLayoutRule[] cameraLayoutRules = new CameraLayoutRule[0];

    [Header("モード別UI")]
    [Tooltip("複数画面モードのときだけ表示するUIを登録します。")]
    [SerializeField]
    private GameObject[] multiDisplayUIObjects = new GameObject[0];

    [Tooltip("単一画面モードのときだけ表示するUIを登録します。")]
    [SerializeField]
    private GameObject[] singleDisplayUIObjects = new GameObject[0];

    [Header("複数画面設定")]
    [Tooltip("Display 1を含む使用画面数です。Display 7まで使う場合は7です。")]
    [SerializeField]
    [Range(1, 8)]
    private int multiDisplayCount = 7;

    [Header("表示モード選択Dropdown")]
    [Tooltip("StartScreenに配置した表示モード選択Dropdownです。")]
    [SerializeField]
    private Dropdown displayModeDropdown;

    [Header("起動時設定")]
    [Tooltip("ONの場合、Start時にSelected Modeを自動適用します。")]
    [SerializeField]
    private bool applySelectedModeOnStart = false;

    [Tooltip("起動時またはStartボタンから適用する表示モードです。")]
    [SerializeField]
    private DisplayLayoutMode selectedMode = DisplayLayoutMode.SingleDisplay;

    [Tooltip("ONの場合、表示モードが適用されるまでモード別UIを両方非表示にします。")]
    [SerializeField]
    private bool hideModeSpecificUIUntilSelected = true;

    public DisplayLayoutMode CurrentMode { get; private set; }
    public bool IsModeSelected { get; private set; }

    private bool additionalDisplaysWereActivated;

    private void Awake()
    {
        if (displayModeDropdown != null)
        {
            displayModeDropdown.onValueChanged.RemoveListener(
                SetSelectedModeFromDropdown
            );

            displayModeDropdown.onValueChanged.AddListener(
                SetSelectedModeFromDropdown
            );

            // Dropdownの初期表示とselectedModeを一致させます。
            SetSelectedModeFromDropdown(displayModeDropdown.value);
        }

        if (hideModeSpecificUIUntilSelected)
        {
            SetUIObjectsActive(multiDisplayUIObjects, false);
            SetUIObjectsActive(singleDisplayUIObjects, false);
        }
    }

    private void OnDestroy()
    {
        if (displayModeDropdown != null)
        {
            displayModeDropdown.onValueChanged.RemoveListener(
                SetSelectedModeFromDropdown
            );
        }
    }

    private void Start()
    {
        if (applySelectedModeOnStart)
        {
            ApplySelectedMode();
        }
    }

    /// <summary>
    /// 現在InspectorまたはDropdownで選択されているモードを適用します。
    /// StartボタンのOnClickから呼び出せます。
    /// </summary>
    public void ApplySelectedMode()
    {
        ApplyMode(selectedMode);
    }

    /// <summary>
    /// 複数画面モードを直ちに適用します。
    /// </summary>
    public void SelectMultiDisplayMode()
    {
        selectedMode = DisplayLayoutMode.MultiDisplay;
        ApplySelectedMode();
    }

    /// <summary>
    /// 単一画面モードを直ちに適用します。
    /// </summary>
    public void SelectSingleDisplayMode()
    {
        selectedMode = DisplayLayoutMode.SingleDisplay;
        ApplySelectedMode();
    }

    /// <summary>
    /// Unity UIのDropdownから選択モードだけを変更します。
    /// 0 = 複数画面、1 = 単一画面です。この時点では適用しません。
    /// </summary>
    public void SetSelectedModeFromDropdown(int optionIndex)
    {
        switch (optionIndex)
        {
            case 0:
                selectedMode = DisplayLayoutMode.MultiDisplay;
                break;

            case 1:
                selectedMode = DisplayLayoutMode.SingleDisplay;
                break;

            default:
                Debug.LogWarning($"表示モードのDropdown値が不正です: {optionIndex}");
                break;
        }
    }

    /// <summary>
    /// 現在のモードを、新しく追加・生成されたCameraにも再適用します。
    /// </summary>
    public void RefreshCurrentLayout()
    {
        if (!IsModeSelected)
        {
            Debug.LogWarning("表示モードがまだ選択されていません。");
            return;
        }

        ApplyCameraLayouts(CurrentMode);
    }

    private void ApplyMode(DisplayLayoutMode mode)
    {
        bool isMultiDisplay = mode == DisplayLayoutMode.MultiDisplay;

        if (isMultiDisplay)
        {
            ActivateAdditionalDisplays();
        }
        else if (additionalDisplaysWereActivated)
        {
            Debug.LogWarning(
                "実行中に有効化した追加Displayは完全には無効化できません。" +
                "次回起動時に単一画面モードを選択してください。"
            );
        }

        ApplyCameraLayouts(mode);

        SetUIObjectsActive(multiDisplayUIObjects, isMultiDisplay);
        SetUIObjectsActive(singleDisplayUIObjects, !isMultiDisplay);

        CurrentMode = mode;
        IsModeSelected = true;

        Debug.Log($"Display layout mode: {CurrentMode}");
    }

    private void ApplyCameraLayouts(DisplayLayoutMode mode)
    {
        if (cameraLayoutRules == null || cameraLayoutRules.Length == 0)
        {
            Debug.LogWarning("Camera Layout Rulesが登録されていません。");
            return;
        }

        // 非アクティブなクレーン配下のCameraや、BirdCameras配下のCameraも取得します。
        Camera[] sceneCameras = FindObjectsOfType<Camera>(true);
        int appliedCameraCount = 0;

        foreach (Camera targetCamera in sceneCameras)
        {
            if (targetCamera == null)
                continue;

            CameraLayoutRule matchedRule = FindMatchingRule(targetCamera.gameObject.name);

            // ルールに一致しないCameraは、現在の設定を変更しません。
            if (matchedRule == null)
                continue;

            bool enableCamera;

            if (mode == DisplayLayoutMode.MultiDisplay)
            {
                targetCamera.targetDisplay = matchedRule.multiTargetDisplay;
                targetCamera.rect = matchedRule.multiViewportRect;
                enableCamera = matchedRule.enableInMultiDisplay;
            }
            else
            {
                // 単一画面モードでは、対象CameraをすべてDisplay 1へ集約します。
                targetCamera.targetDisplay = 0;
                targetCamera.rect = matchedRule.singleViewportRect;
                enableCamera = matchedRule.enableInSingleDisplay;
            }

            // Cameraコンポーネントだけを切り替えます。
            // GameObjectのActive状態はCraneOperationManagerに任せます。
            targetCamera.enabled = enableCamera;
            appliedCameraCount++;
        }

        Debug.Log($"{appliedCameraCount}台のCameraに表示レイアウトを適用しました。");
    }

    private CameraLayoutRule FindMatchingRule(string targetCameraName)
    {
        foreach (CameraLayoutRule rule in cameraLayoutRules)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.cameraName))
                continue;

            if (IsCameraNameMatch(targetCameraName, rule))
                return rule;
        }

        return null;
    }

    private bool IsCameraNameMatch(string targetCameraName, CameraLayoutRule rule)
    {
        switch (rule.nameMatchMode)
        {
            case CameraNameMatchMode.Exact:
                return string.Equals(
                    targetCameraName,
                    rule.cameraName,
                    StringComparison.Ordinal
                );

            case CameraNameMatchMode.StartsWith:
                return targetCameraName.StartsWith(
                    rule.cameraName,
                    StringComparison.Ordinal
                );

            case CameraNameMatchMode.Contains:
                return targetCameraName.IndexOf(
                    rule.cameraName,
                    StringComparison.Ordinal
                ) >= 0;

            default:
                return false;
        }
    }

    private void SetUIObjectsActive(GameObject[] uiObjects, bool active)
    {
        if (uiObjects == null)
            return;

        foreach (GameObject uiObject in uiObjects)
        {
            if (uiObject != null)
            {
                uiObject.SetActive(active);
            }
        }
    }

    private void ActivateAdditionalDisplays()
    {
#if UNITY_STANDALONE
        int detectedDisplayCount = Display.displays.Length;

        if (detectedDisplayCount < multiDisplayCount)
        {
            Debug.LogWarning(
                $"必要なDisplay数は{multiDisplayCount}ですが、" +
                $"認識されているDisplayは{detectedDisplayCount}台です。"
            );
        }

        int activateCount = Mathf.Min(multiDisplayCount, detectedDisplayCount);

        // Display 1は起動時から有効なので、Display 2以降を有効化します。
        for (int i = 1; i < activateCount; i++)
        {
            if (!Display.displays[i].active)
            {
                Display.displays[i].Activate();
            }
        }

        additionalDisplaysWereActivated = activateCount > 1;

        Debug.Log($"Display 1～{activateCount}を使用します。");
#else
        Debug.Log(
            "追加Displayの有効化はStandaloneビルドで確認してください。"
        );
#endif
    }
}
