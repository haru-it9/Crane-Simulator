using System;
using UnityEngine;

public class DisplayLayoutManager : MonoBehaviour
{
    public enum DisplayLayoutMode
    {
        MultiDisplay,
        SingleDisplay
    }

    [System.Serializable]
    public class CameraLayoutSetting
    {
        [Header("対象カメラ")]
        public Camera targetCamera;

        [Header("複数画面モード")]
        [Tooltip("0 = Display 1、1 = Display 2")]
        [Min(0)]
        public int multiTargetDisplay = 0;

        public Rect multiViewportRect =
            new Rect(0f, 0f, 1f, 1f);

        [Header("単一画面モード")]
        public Rect singleViewportRect =
            new Rect(0f, 0f, 1f, 1f);
    }

    [Header("複数画面設定")]
    [SerializeField]
    [Range(1, 8)]
    private int multiDisplayCount = 7;

    [Header("カメラ設定")]
    [SerializeField]
    private CameraLayoutSetting[] cameraLayouts;

    [Header("複数画面専用UI")]
    [SerializeField]
    private GameObject[] multiDisplayUIObjects;

    [Header("単一画面専用UI")]
    [SerializeField]
    private GameObject[] singleDisplayUIObjects;

    [Header("モードを自動設定する場合")]
    [SerializeField]
    private bool applyDefaultModeOnStart = false;

    [SerializeField]
    private DisplayLayoutMode defaultMode =
        DisplayLayoutMode.SingleDisplay;

    public DisplayLayoutMode CurrentMode { get; private set; }

    public bool IsModeSelected { get; private set; }

    private void Awake()
    {
        // モード固有UIは、選択されるまで非表示
        SetUIObjectsActive(
            multiDisplayUIObjects,
            false);

        SetUIObjectsActive(
            singleDisplayUIObjects,
            false);
    }

    private void Start()
    {
        if (applyDefaultModeOnStart)
        {
            ApplyMode(defaultMode);
        }
    }

    /// <summary>
    /// 複数画面ボタンから呼び出す
    /// </summary>
    public void SelectMultiDisplayMode()
    {
        ApplyMode(DisplayLayoutMode.MultiDisplay);
    }

    /// <summary>
    /// 単一画面ボタンから呼び出す
    /// </summary>
    public void SelectSingleDisplayMode()
    {
        ApplyMode(DisplayLayoutMode.SingleDisplay);
    }

    public void ApplyMode(DisplayLayoutMode mode)
    {
        bool isMultiDisplay =
            mode == DisplayLayoutMode.MultiDisplay;

        if (isMultiDisplay)
        {
            ActivateAdditionalDisplays();
        }

        ApplyCameraLayouts(mode);

        SetUIObjectsActive(
            multiDisplayUIObjects,
            isMultiDisplay);

        SetUIObjectsActive(
            singleDisplayUIObjects,
            !isMultiDisplay);

        CurrentMode = mode;
        IsModeSelected = true;

        Debug.Log(
            $"Display layout mode: {CurrentMode}");
    }

    private void ApplyCameraLayouts(DisplayLayoutMode mode)
    {
        if (cameraLayouts == null)
            return;

        bool isMultiDisplay =
            mode == DisplayLayoutMode.MultiDisplay;

        foreach (CameraLayoutSetting setting
                in cameraLayouts)
        {
            if (setting == null ||
                setting.targetCamera == null)
            {
                continue;
            }

            Camera targetCamera =
                setting.targetCamera;

            if (isMultiDisplay)
            {
                targetCamera.targetDisplay =
                    setting.multiTargetDisplay;

                targetCamera.rect =
                    setting.multiViewportRect;
            }
            else
            {
                // 単一画面ではすべてDisplay 1
                targetCamera.targetDisplay = 0;

                targetCamera.rect =
                    setting.singleViewportRect;
            }
        }
    }

    private void SetUIObjectsActive(
        GameObject[] uiObjects,
        bool active)
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
        int detectedDisplayCount =
            Display.displays.Length;

        if (detectedDisplayCount < multiDisplayCount)
        {
            Debug.LogWarning(
                $"必要なDisplay数は{multiDisplayCount}ですが、" +
                $"認識されているDisplayは{detectedDisplayCount}台です。");
        }

        int activateCount =
            Mathf.Min(
                multiDisplayCount,
                detectedDisplayCount);

        // Display 1は最初から有効なので、
        // Display 2以降を有効化する
        for (int i = 1; i < activateCount; i++)
        {
            if (!Display.displays[i].active)
            {
                Display.displays[i].Activate();
            }
        }

        Debug.Log(
            $"Display 1～{activateCount}を使用します。");
    #else
        Debug.Log(
            "追加Displayの有効化はStandaloneビルドで確認してください。");
    #endif
    }
}