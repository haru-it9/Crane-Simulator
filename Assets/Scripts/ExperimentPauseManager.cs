using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 実験全体の一時停止・再開を管理します。
/// 介入対象クレーンだけを止める処理とは独立しています。
/// </summary>
[DisallowMultipleComponent]
public class ExperimentPauseManager : MonoBehaviour
{
    private static ExperimentPauseManager instance;

    public static bool IsPaused { get; private set; }
    public static event Action<bool> PauseStateChanged;

    [Header("開始設定")]
    [Tooltip("ONの場合、シミュレータ開始後も停止状態を維持します。")]
    [SerializeField]
    private bool pauseOnSimulatorStart = true;

    [Tooltip("SimulatorStartManagerの開始操作が完了するまでTime.timeScaleを変更しません。")]
    [SerializeField]
    private bool waitForSimulatorStart = true;

    [Header("TouchPanel UI（Multiなどメイン画面）")]
    [Tooltip("メイン画面の一時停止／再開Buttonです。未設定時は同じGameObjectから取得します。")]
    [SerializeField]
    private Button pauseResumeButton;

    [Tooltip("ボタン内の表示Textです。未設定時は子から自動取得します。")]
    [SerializeField]
    private Text buttonLabel;

    [SerializeField]
    private string pausedButtonText = "開始 / 再開";

    [SerializeField]
    private string runningButtonText = "一時停止";

    [Tooltip("停止中だけ表示したいUIがある場合に設定します。")]
    [SerializeField]
    private GameObject pausedIndicator;

    [Header("追加TouchPanel UI（Single・Mixなど）")]
    [Tooltip("SingleやMixに配置した一時停止／再開Buttonを登録します。")]
    [SerializeField]
    private Button[] additionalPauseResumeButtons = new Button[0];

    [Tooltip("追加Button内のTextをButtonと同じ順番で登録します。未設定のElementは子から自動取得します。")]
    [SerializeField]
    private Text[] additionalButtonLabels = new Text[0];

    [Tooltip("各追加画面で停止中だけ表示するUIです。不要ならSize 0のままで構いません。")]
    [SerializeField]
    private GameObject[] additionalPausedIndicators =
        new GameObject[0];

    private bool timeScalePauseApplied;
    private float timeScaleBeforePause = 1f;

    private bool CanApplyTimeScale =>
        !waitForSimulatorStart ||
        SimulatorStartManager.IsOperationEnabled;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                "ExperimentPauseManagerが複数存在するため、重複側を無効化します。",
                this
            );
            enabled = false;
            return;
        }

        instance = this;
        IsPaused = pauseOnSimulatorStart;
        FindUiReferences();
        UpdateUi();
    }

    private void OnEnable()
    {
        FindUiReferences();
        RegisterButtonListeners();
        UpdateUi();
    }

    private void Update()
    {
        if (CanApplyTimeScale)
        {
            ApplyPauseToTimeScale();
        }
    }

    private void OnDisable()
    {
        UnregisterButtonListeners();

        RestoreTimeScale();

        if (instance == this && IsPaused)
        {
            IsPaused = false;
            PauseStateChanged?.Invoke(false);
        }
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        RestoreTimeScale();
        IsPaused = false;
        instance = null;
    }

    public void TogglePause()
    {
        SetPaused(!IsPaused);
    }

    public void Pause()
    {
        SetPaused(true);
    }

    public void Resume()
    {
        SetPaused(false);
    }

    public void SetPaused(bool paused)
    {
        bool stateChanged = IsPaused != paused;
        IsPaused = paused;

        if (CanApplyTimeScale)
        {
            ApplyPauseToTimeScale();
        }

        UpdateUi();

        if (stateChanged)
        {
            PauseStateChanged?.Invoke(IsPaused);
            Debug.Log(
                IsPaused
                    ? "実験全体を一時停止しました。"
                    : "実験全体を再開しました。"
            );
        }
    }

    private void ApplyPauseToTimeScale()
    {
        if (IsPaused)
        {
            if (!timeScalePauseApplied)
            {
                timeScaleBeforePause = Time.timeScale > 0f
                    ? Time.timeScale
                    : 1f;
                timeScalePauseApplied = true;
            }

            Time.timeScale = 0f;
            return;
        }

        RestoreTimeScale();
    }

    private void RestoreTimeScale()
    {
        if (!timeScalePauseApplied)
        {
            return;
        }

        Time.timeScale = Mathf.Max(0.0001f, timeScaleBeforePause);
        timeScalePauseApplied = false;
    }

    private void FindUiReferences()
    {
        if (pauseResumeButton == null)
        {
            pauseResumeButton = GetComponent<Button>();
        }

        if (buttonLabel == null && pauseResumeButton != null)
        {
            buttonLabel =
                pauseResumeButton.GetComponentInChildren<Text>(true);
        }
    }

    private void RegisterButtonListeners()
    {
        RegisterButtonListener(pauseResumeButton);

        if (additionalPauseResumeButtons == null)
        {
            return;
        }

        foreach (Button button in additionalPauseResumeButtons)
        {
            RegisterButtonListener(button);
        }
    }

    private void RegisterButtonListener(Button button)
    {
        if (button == null)
        {
            return;
        }

        // InspectorのOn Clickを設定しなくても動作します。
        // 同じButtonが重複登録されていても、Listenerは1つだけになります。
        button.onClick.RemoveListener(TogglePause);
        button.onClick.AddListener(TogglePause);
    }

    private void UnregisterButtonListeners()
    {
        UnregisterButtonListener(pauseResumeButton);

        if (additionalPauseResumeButtons == null)
        {
            return;
        }

        foreach (Button button in additionalPauseResumeButtons)
        {
            UnregisterButtonListener(button);
        }
    }

    private void UnregisterButtonListener(Button button)
    {
        if (button != null)
        {
            button.onClick.RemoveListener(TogglePause);
        }
    }

    private void UpdateUi()
    {
        UpdateButtonLabel(buttonLabel, pauseResumeButton);

        if (pausedIndicator != null)
        {
            pausedIndicator.SetActive(IsPaused);
        }

        if (additionalPauseResumeButtons != null)
        {
            for (int i = 0;
                 i < additionalPauseResumeButtons.Length;
                 i++)
            {
                Button button = additionalPauseResumeButtons[i];
                Text label =
                    additionalButtonLabels != null &&
                    i < additionalButtonLabels.Length
                        ? additionalButtonLabels[i]
                        : null;

                UpdateButtonLabel(label, button);
            }
        }

        if (additionalPausedIndicators != null)
        {
            foreach (GameObject indicator in additionalPausedIndicators)
            {
                if (indicator != null)
                {
                    indicator.SetActive(IsPaused);
                }
            }
        }
    }

    private void UpdateButtonLabel(Text label, Button button)
    {
        Text resolvedLabel = label;

        if (resolvedLabel == null && button != null)
        {
            resolvedLabel = button.GetComponentInChildren<Text>(true);
        }

        if (resolvedLabel == null)
        {
            return;
        }

        resolvedLabel.text = IsPaused
            ? pausedButtonText
            : runningButtonText;
    }
}
