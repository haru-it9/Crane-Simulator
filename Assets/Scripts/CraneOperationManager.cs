using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CraneOperationManager : MonoBehaviour
{
    public enum InputMode
    {
        Keyboard,
        Joystick
    }

    public enum SpeedControlMode
    {
        ButtonAndKeyboard,
        JoystickStep
    }

    public enum OperationMode
    {
        MultiCraneManagement,
        SingleCrane
    }

    [System.Serializable]
    public class OperationUiSet
    {
        [Header("画面全体")]
        public GameObject craneStatusScreen;
        public GameObject waitingScreen;

        [Header("操作情報")]
        public CraneInformationDisplay craneInformationDisplay;
        public LifMagCurrentButton[] lifMagCurrentButtons =
            new LifMagCurrentButton[0];
        public Text currentCraneNameText;

        [Header("クレーン選択")]
        [Tooltip("Crane ID順に登録します。")]
        public Button[] craneSelectButtons = new Button[0];
        public Button lockUnlockButton;
        public Text lockUnlockButtonText;

        [Header("速度操作UI")]
        public GameObject[] speedControlUIButtons = new GameObject[0];

        [Header("速度表示Text")]
        public Text zSpeedText;
        public Text mainLifMagXSpeedText;
        public Text mainLifMagYSpeedText;
    }

    [Header("Operation Mode")]
    [SerializeField] private OperationMode operationMode = OperationMode.MultiCraneManagement;

    [Tooltip("SingleCraneモードで操作するクレーン番号。Crane1なら0、Crane2なら1")]
    [SerializeField] private int singleCraneIndex = 0;

    [Header("Crane Registry")]
    [Tooltip("クレーン本体・Camera・情報表示先をCraneInstanceから取得します。")]
    [SerializeField] private CraneRegistry craneRegistry;

    [Header("Display Layout Manager")]
    [Tooltip("Multi/Singleのうち、現在表示中のUIだけを切り替えるために使用します。")]
    [SerializeField] private DisplayLayoutManager displayLayoutManager;

    [Header("表示モード別UI")]
    [Tooltip("複数画面で使用するUIを登録します。")]
    [SerializeField] private OperationUiSet multiDisplayUiSet =
        new OperationUiSet();

    [Tooltip("単一画面で使用するUIを登録します。")]
    [SerializeField] private OperationUiSet singleDisplayUiSet =
        new OperationUiSet();

    [Header("Intervention Scenario Manager")]
    [SerializeField] private CraneInterventionScenarioManager interventionScenarioManager;

    [Header("Input Settings")]
    [SerializeField] private InputMode inputMode = InputMode.Keyboard;

    [Header("Speed Control Mode")]
    [SerializeField] private SpeedControlMode speedControlMode = SpeedControlMode.ButtonAndKeyboard;

    [Header("Debug Mode")]
    [SerializeField] private bool debugMode = false;

    [Header("Joystick Axes")]
    [SerializeField] private string joyStick2Horizontal = "JoyStick2Horizontal";
    [SerializeField] private string joyStick2Vertical = "JoyStick2Vertical";
    [SerializeField] private string joyStick3Vertical = "JoyStick3Vertical";
    [SerializeField] private string joyStick2Trigger = "JoyStick2Trigger";
    [SerializeField] private string joyStick3MiniVertical = "JoyStick3MiniVertical";

    [Header("Debug Joystick Axes")]
    [SerializeField] private string debugJoyStick2Horizontal = "JoyStick1RightHorizontal";
    [SerializeField] private string debugJoyStick2Vertical = "JoyStick1RightVertical";
    [SerializeField] private string debugJoyStick3Vertical = "JoyStick1LeftVertical";

    [Header("Dead Zone")]
    [SerializeField] private float deadZone = 0.1f;
    
    [Header("Current Crane")]
    [SerializeField] private int currentCraneIndex = 0;

    [Header("Display5 Status")]
    [SerializeField] private CraneStatusManager craneStatusManager;

    [Header("Crane Select UI")]
    [Tooltip("選択基数より後ろのクレーン選択ボタンを非表示にします。")]
    [SerializeField] private bool hideInactiveCraneButtons = true;

    [SerializeField] private Color normalButtonColor = Color.white;
    [SerializeField] private Color selectedButtonColor = Color.yellow;
    [SerializeField] private Color unlockColor = new Color(0.7f, 1.0f, 0.7f); // 淡い緑
    [SerializeField] private Color lockColor = new Color(1.0f, 0.7f, 0.7f);   // 淡い赤

    private bool isSelectionLocked = false;

    public CraneInstance CurrentCraneInstance
    {
        get
        {
            if (!IsActiveCraneIndex(currentCraneIndex)) return null;
            return craneRegistry.GetCraneByRuntimeIndex(currentCraneIndex);
        }
    }

    public CraneUnit CurrentCrane
    {
        get
        {
            CraneInstance craneInstance = CurrentCraneInstance;
            return craneInstance != null ? craneInstance.CraneUnit : null;
        }
    }

    public int CurrentCraneIndex => currentCraneIndex;
    public int ActiveCraneCount => GetActiveCraneCount();

    private void Awake()
    {
        EnsureRegistryIsReady();
        EnsureDisplayLayoutManagerIsReady();
    }

    private void OnEnable()
    {
        SubscribeToDisplayLayoutManager();
    }

    private void Start()
    {
        SubscribeToRegistry();
        ApplyOperationMode();
        
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();

        UpdateSpeedControlUI();

        ApplySpeedControlModeToCranes();
        UpdateSpeedDisplayTexts();
    }

    private void OnDestroy()
    {
        if (craneRegistry != null)
        {
            craneRegistry.ActiveCraneCountChanged -=
                HandleActiveCraneCountChanged;
        }

        UnsubscribeFromDisplayLayoutManager();
    }

    private void Update()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        if (CurrentCrane == null) return;

        // InputField入力中はキーボードによる速度切替を受け付けない
        if (SimulatorStartManager.IsInputFieldFocused()) return;

        HandleSpeedSwitch();
        UpdateSpeedDisplayTexts();
    }

    private void ApplyOperationMode()
    {
        if (operationMode == OperationMode.SingleCrane)
        {
            int activeCraneCount = GetActiveCraneCount();

            if (activeCraneCount == 0)
            {
                currentCraneIndex = -1;
            }
            else
            {
                singleCraneIndex = Mathf.Clamp(
                    singleCraneIndex,
                    0,
                    activeCraneCount - 1
                );
                currentCraneIndex = singleCraneIndex;
            }

            // 1基固定操作モードでは両レイアウトの待機・状態画面を隠します。
            SetWaitingScreensActive(false);
            SetStatusScreensActive(false);

            // ★追加：単一モードではCraneStatusManagerを停止
            if (craneStatusManager != null)
            {
                craneStatusManager.SetStatusManagementEnabled(false);
            }

            // 単一モードではクレーン選択を固定しておく
            SetSelectionLock(true);
        }
        else
        {
            // 複数台管理モードは従来通り、最初は未選択
            currentCraneIndex = -1;

            // 複数台管理モードでは両方を有効にしておきます。
            // 実際に見える側はDisplayLayoutManagerが親Rootで切り替えます。
            SetStatusScreensActive(true);

            // ★追加：複数台管理モードではCraneStatusManagerを再開
            if (craneStatusManager != null)
            {
                craneStatusManager.SetStatusManagementEnabled(true);
            }

            UpdateWaitingScreen();

            SetSelectionLock(false);
        }

        UpdateCraneButtonVisibility();
    }

    private void FixedUpdate()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        if (CurrentCrane == null) return;

        // Keyboardモード中、InputField入力中はクレーン操作を受け付けない
        if (inputMode == InputMode.Keyboard && SimulatorStartManager.IsInputFieldFocused())
        {
            return;
        }

        HandleMovement();
        UpdateSpeedDisplayTexts();
    }

    public void HandleCraneSelection(int craneIndex)
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (operationMode == OperationMode.SingleCrane)
        {
            Debug.Log("SingleCraneモード中のため、クレーン選択は無効です");
            return;
        }

        if (isSelectionLocked)
        {
            Debug.Log("クレーン選択はLock中です");
            return;
        }

        int activeCraneCount = GetActiveCraneCount();

        if (craneIndex < 0 || craneIndex >= activeCraneCount)
        {
            Debug.LogWarning(
                $"使用対象外のクレーン番号です: {craneIndex} " +
                $"（有効基数: {activeCraneCount}）"
            );
            return;
        }

        currentCraneIndex = craneIndex;

        if (CurrentCrane == null)
        {
            Debug.LogWarning($"Crane {craneIndex + 1} が取得できません");
            return;
        }

        Debug.Log($"操作対象クレーン: {CurrentCrane.name}");

        CurrentCrane.ResetSpeedLevel();

        // ================================
        // 介入開始状態の生成
        // ================================
        if (operationMode == OperationMode.MultiCraneManagement)
        {
            if (craneStatusManager == null)
            {
                Debug.LogWarning("CraneStatusManager が設定されていません");
            }
            else if (interventionScenarioManager == null)
            {
                Debug.LogWarning("InterventionScenarioManager が設定されていません");
            }
            else
            {
                bool gotInfo = craneStatusManager.TryGetCraneInterventionInfo(
                    craneIndex,
                    out CraneStatusManager.WorkPhase phase,
                    out CraneStatusManager.ErrorType errorType
                );

                if (!gotInfo)
                {
                    Debug.LogWarning($"Crane {craneIndex + 1} の作業状態・停止要因を取得できませんでした");
                    return;
                }

                interventionScenarioManager.SetupInterventionState(
                    CurrentCrane,
                    phase,
                    errorType,
                    craneIndex
                );
            }
        }

        UpdateWaitingScreen();
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();
        UpdateSpeedDisplayTexts();

        SetSelectionLock(true);
    }

    private void UpdateActiveCamera()
    {
        if (!EnsureRegistryIsReady()) return;

        // 未選択状態ではカメラ状態を変更しない
        if (currentCraneIndex < 0)
        {
            return;
        }

        for (int runtimeIndex = 0;
             runtimeIndex < craneRegistry.TotalCraneCount;
             runtimeIndex++)
        {
            CraneInstance crane =
                craneRegistry.GetCraneByRuntimeIndex(runtimeIndex);

            if (crane == null) continue;

            bool isActiveCrane =
                runtimeIndex == currentCraneIndex &&
                craneRegistry.IsRuntimeIndexActive(runtimeIndex);

            Camera[] cameras = crane.GetAllCameras();

            foreach (Camera camera in cameras)
            {
                if (camera != null)
                {
                    camera.gameObject.SetActive(isActiveCrane);
                }
            }
        }
    }

    private void UpdateWaitingScreen()
    {
        if (operationMode == OperationMode.SingleCrane)
        {
            SetWaitingScreensActive(false);
            return;
        }

        SetWaitingScreensActive(CurrentCrane == null);
    }

    private void UpdateDisplay2()
    {
        CraneInstance crane = CurrentCraneInstance;
        if (crane == null) return;

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.craneInformationDisplay == null) continue;

            uiSet.craneInformationDisplay.SetTarget(
                crane.InformationTarget,
                crane.LifMagSystem
            );
        }
    }

    public void EnterWaitingMode()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (operationMode == OperationMode.SingleCrane)
        {
            Debug.Log("SingleCraneモード中のため、WaitingScreenには戻りません");
            return;
        }

        // Waitingに戻るだけでは介入状態を消さない
        // Doneを押したときだけ ClearScenarioByCraneIndex() で削除する
        // if (interventionScenarioManager != null)
        // {
        //     interventionScenarioManager.ClearCurrentScenarioObjects();
        // }

        currentCraneIndex = -1;

        UpdateWaitingScreen();
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();

        SetSelectionLock(false);
    }

    private void UpdateSpeedControlUI()
    {
        bool showButtons = speedControlMode == SpeedControlMode.ButtonAndKeyboard;

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.speedControlUIButtons == null) continue;

            foreach (GameObject obj in uiSet.speedControlUIButtons)
            {
                if (obj != null)
                {
                    obj.SetActive(showButtons);
                }
            }
        }
    }

    private void UpdateSpeedDisplayTexts()
    {
        CraneUnit crane = CurrentCrane;

        string zValue = crane != null
            ? crane.ZSpeedDisplayText
            : "";
        string xValue = crane != null
            ? crane.MainLifMagXSpeedDisplayText
            : "";
        string yValue = crane != null
            ? crane.MainLifMagYSpeedDisplayText
            : "";

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.zSpeedText != null)
            {
                uiSet.zSpeedText.text = zValue;
            }

            if (uiSet.mainLifMagXSpeedText != null)
            {
                uiSet.mainLifMagXSpeedText.text = xValue;
            }

            if (uiSet.mainLifMagYSpeedText != null)
            {
                uiSet.mainLifMagYSpeedText.text = yValue;
            }
        }
    }

    public void IncreaseCurrentCraneXSpeed()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard) return;
        if (CurrentCrane == null) return;
        CurrentCrane.IncreaseMainLifMagXSpeed();
    }

    public void DecreaseCurrentCraneXSpeed()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard) return;
        if (CurrentCrane == null) return;
        CurrentCrane.DecreaseMainLifMagXSpeed();
    }

    public void IncreaseCurrentCraneYSpeed()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard) return;
        if (CurrentCrane == null) return;
        CurrentCrane.IncreaseMainLifMagYSpeed();
    }

    public void DecreaseCurrentCraneYSpeed()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard) return;
        if (CurrentCrane == null) return;
        CurrentCrane.DecreaseMainLifMagYSpeed();
    }

    public void IncreaseCurrentCraneZSpeed()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard) return;
        if (CurrentCrane == null) return;
        CurrentCrane.IncreaseZSpeed();
    }

    public void DecreaseCurrentCraneZSpeed()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard) return;
        if (CurrentCrane == null) return;
        CurrentCrane.DecreaseZSpeed();
    }

    public void SetCurrentCraneLifMagCurrent(int index, bool isOn)
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        if (CurrentCrane == null) return;
        if (CurrentCrane.LifMagSystem == null) return;

        CurrentCrane.LifMagSystem.SetLifMagCurrent(index, isOn);
    }

    public void ResetCurrentCraneLifMag()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (CurrentCrane == null) return;
        if (CurrentCrane.LifMagSystem == null) return;

        CurrentCrane.LifMagSystem.DetachAllFromButton();
        CurrentCrane.LifMagSystem.ResetLifMagDisplayAccumValues();
    }

    private void UpdateLifMagButtonViews()
    {
        if (CurrentCrane == null) return;
        if (CurrentCrane.LifMagSystem == null) return;

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.lifMagCurrentButtons == null) continue;

            for (int i = 0; i < uiSet.lifMagCurrentButtons.Length; i++)
            {
                LifMagCurrentButton button =
                    uiSet.lifMagCurrentButtons[i];

                if (button == null) continue;

                bool isOn =
                    CurrentCrane.LifMagSystem.GetLifMagCurrent(i);
                float currentValue =
                    CurrentCrane.LifMagSystem
                        .GetLifMagDisplayAccumValue(i);

                button.SetViewOnly(isOn);
                button.SetCurrentValueView(currentValue);
            }
        }
    }

    private void UpdateCurrentCraneNameText()
    {
        CraneInstance crane = CurrentCraneInstance;
        string displayName = crane != null
            ? crane.DisplayName
            : "未選択";

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.currentCraneNameText != null)
            {
                uiSet.currentCraneNameText.text = displayName;
            }
        }
    }

    public void ToggleSelectionLock()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        SetSelectionLock(!isSelectionLocked);
    }

    private void SetSelectionLock(bool locked)
    {
        isSelectionLocked = locked;

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.lockUnlockButtonText != null)
            {
                uiSet.lockUnlockButtonText.text =
                    isSelectionLocked ? "Lock" : "Unlock";
            }

            if (uiSet.lockUnlockButton != null)
            {
                Image buttonImage =
                    uiSet.lockUnlockButton.GetComponent<Image>();

                if (buttonImage != null)
                {
                    buttonImage.color = isSelectionLocked
                        ? lockColor
                        : unlockColor;
                }
            }
        }
    }

    private void UpdateCraneButtonColors()
    {
        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.craneSelectButtons == null) continue;

            for (int i = 0; i < uiSet.craneSelectButtons.Length; i++)
            {
                Button button = uiSet.craneSelectButtons[i];
                if (button == null) continue;

                Image buttonImage = button.GetComponent<Image>();
                if (buttonImage == null) continue;

                buttonImage.color = (i == currentCraneIndex)
                    ? selectedButtonColor
                    : normalButtonColor;
            }
        }
    }

    private void UpdateCraneButtonVisibility()
    {
        int activeCraneCount = GetActiveCraneCount();

        foreach (OperationUiSet uiSet in GetUiSets())
        {
            if (uiSet.craneSelectButtons == null) continue;

            for (int i = 0; i < uiSet.craneSelectButtons.Length; i++)
            {
                Button button = uiSet.craneSelectButtons[i];

                if (button == null) continue;

                bool isActiveCrane = i < activeCraneCount;

                if (hideInactiveCraneButtons)
                {
                    button.gameObject.SetActive(isActiveCrane);
                }
                else
                {
                    button.interactable = isActiveCrane;
                }
            }
        }
    }

    public void CompleteCurrentCraneError()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (operationMode == OperationMode.SingleCrane)
        {
            Debug.Log("SingleCraneモード中のため、エラー完了処理は行いません");
            return;
        }
        
        if (craneStatusManager == null) return;

        if (currentCraneIndex < 0)
        {
            Debug.LogWarning("操作対象クレーンが未選択です");
            return;
        }

        if (interventionScenarioManager != null)
        {
            interventionScenarioManager.ClearScenarioByCraneIndex(currentCraneIndex);
        }

        craneStatusManager.CompleteErrorByCraneIndex(currentCraneIndex);

        EnterWaitingMode();
    }
    
    private void HandleSpeedSwitch()
    {
        if (speedControlMode != SpeedControlMode.ButtonAndKeyboard)
        {
            return;
        }
        
        // 速度切替キーは例
        if (Input.GetKeyDown(KeyCode.C))
            CurrentCrane.ChangeZSpeed();

        if (Input.GetKeyDown(KeyCode.Z))
            CurrentCrane.ChangeMainLifMagXSpeed();

        if (Input.GetKeyDown(KeyCode.X))
            CurrentCrane.ChangeMainLifMagYSpeed();
    }

    private void HandleMovement()
    {
        float mainXInput = GetMainLifMagXInput();
        float mainYInput = GetMainLifMagYInput();
        float mainZInput = GetMainCraneZInput();

        CurrentCrane.MoveMainLifMagX(mainXInput);
        CurrentCrane.MoveMainLifMagY(mainYInput);
        CurrentCrane.MoveMainCraneZ(mainZInput);

        float spreadInput = Input.GetAxis(joyStick3MiniVertical); // ジョイスティック入力

        // 中央は動かさない
        CurrentCrane.MoveLifMagX(2, 0f);

        // 内側ペア（1と3）
        CurrentCrane.MoveLifMagX(1, -spreadInput);
        CurrentCrane.MoveLifMagX(3,  spreadInput);

        // 外側ペア（0と4）
        CurrentCrane.MoveLifMagX(0, -spreadInput);
        CurrentCrane.MoveLifMagX(4,  spreadInput);

        // LifMag個別X入力
        // 例として 5個分を別キーで操作
        // 正方向: U I O P [ / 負方向: 7 8 9 0 - みたいにしてもいいですが、
        // まずは仮に数字キー1〜5で正、Shift+1〜5で負 は扱いにくいので
        // 下のように2列に分ける例にします。

        /*float lif0 = GetAxisFromKeys(KeyCode.Alpha1, KeyCode.Q);
        float lif1 = GetAxisFromKeys(KeyCode.Alpha2, KeyCode.E);
        float lif2 = GetAxisFromKeys(KeyCode.Alpha3, KeyCode.R);
        float lif3 = GetAxisFromKeys(KeyCode.Alpha4, KeyCode.T);
        float lif4 = GetAxisFromKeys(KeyCode.Alpha5, KeyCode.Y);

        CurrentCrane.MoveLifMagX(0, lif0);
        CurrentCrane.MoveLifMagX(1, lif1);
        CurrentCrane.MoveLifMagX(2, lif2);
        CurrentCrane.MoveLifMagX(3, lif3);
        CurrentCrane.MoveLifMagX(4, lif4);*/
    }

    private float GetMainLifMagXInput()
    {
        if (inputMode == InputMode.Keyboard)
        {
            float input = 0f;
            if (Input.GetKey(KeyCode.D)) input += 1f;
            if (Input.GetKey(KeyCode.A)) input -= 1f;
            return input;
        }
        else
        {
            return ApplyDeadZone(-Input.GetAxis(JoyStick3VerticalAxis));
        }
    }

    private float GetMainLifMagYInput()
    {
        if (inputMode == InputMode.Keyboard)
        {
            float input = 0f;
            if (Input.GetKey(KeyCode.W)) input += 1f;
            if (Input.GetKey(KeyCode.S)) input -= 1f;
            return input;
        }
        else
        {
            return ApplyDeadZone(Input.GetAxis(JoyStick2VerticalAxis));
        }
    }

    private float GetMainCraneZInput()
    {
        if (inputMode == InputMode.Keyboard)
        {
            float input = 0f;
            if (Input.GetKey(KeyCode.L)) input += 1f;
            if (Input.GetKey(KeyCode.J)) input -= 1f;
            return input;
        }
        else
        {
            return ApplyDeadZone(-Input.GetAxis(JoyStick2HorizontalAxis));
        }
    }

    private string JoyStick2HorizontalAxis
    {
        get
        {
            return debugMode ? debugJoyStick2Horizontal : joyStick2Horizontal;
        }
    }

    private string JoyStick2VerticalAxis
    {
        get
        {
            return debugMode ? debugJoyStick2Vertical : joyStick2Vertical;
        }
    }

    private string JoyStick3VerticalAxis
    {
        get
        {
            return debugMode ? debugJoyStick3Vertical : joyStick3Vertical;
        }
    }

    private float ApplyDeadZone(float value)
    {
        if (Mathf.Abs(value) < deadZone) return 0f;
        
        if (speedControlMode == SpeedControlMode.JoystickStep)
        {
            if (Mathf.Abs(value) < deadZone) return 0f;
            return Mathf.Clamp(value, -1f, 1f);
        }
        
        if (Mathf.Abs(value) < deadZone) return 0f;
        return value > 0f ? 1f : -1f;
    }

    private void ApplySpeedControlModeToCranes()
    {
        if (!EnsureRegistryIsReady()) return;

        bool useJoystickStep = speedControlMode == SpeedControlMode.JoystickStep;

        for (int runtimeIndex = 0;
             runtimeIndex < craneRegistry.ActiveCraneCount;
             runtimeIndex++)
        {
            CraneInstance craneInstance =
                craneRegistry.GetCraneByRuntimeIndex(runtimeIndex);

            if (craneInstance != null && craneInstance.CraneUnit != null)
            {
                craneInstance.CraneUnit.SetJoystickStepSpeedMode(
                    useJoystickStep
                );
            }
        }
    }

    private void SubscribeToRegistry()
    {
        if (!EnsureRegistryIsReady()) return;

        // 二重登録を防ぎます。
        craneRegistry.ActiveCraneCountChanged -=
            HandleActiveCraneCountChanged;
        craneRegistry.ActiveCraneCountChanged +=
            HandleActiveCraneCountChanged;
    }

    private IEnumerable<OperationUiSet> GetUiSets()
    {
        if (multiDisplayUiSet != null)
        {
            yield return multiDisplayUiSet;
        }

        if (singleDisplayUiSet != null)
        {
            yield return singleDisplayUiSet;
        }
    }

    private void SetWaitingScreensActive(bool active)
    {
        // 一度両方をOFFにし、現在の表示モード側だけをONにします。
        SetWaitingScreenActive(multiDisplayUiSet, false);
        SetWaitingScreenActive(singleDisplayUiSet, false);

        if (!active) return;

        OperationUiSet activeUiSet = GetActiveDisplayUiSet();
        SetWaitingScreenActive(activeUiSet, true);
    }

    private void SetWaitingScreenActive(OperationUiSet uiSet, bool active)
    {
        if (uiSet != null && uiSet.waitingScreen != null)
        {
            uiSet.waitingScreen.SetActive(active);
        }
    }

    private void SetStatusScreensActive(bool active)
    {
        // WaitingScreenと同様、非表示側の画面を直接再有効化しません。
        SetStatusScreenActive(multiDisplayUiSet, false);
        SetStatusScreenActive(singleDisplayUiSet, false);

        if (!active) return;

        OperationUiSet activeUiSet = GetActiveDisplayUiSet();
        SetStatusScreenActive(activeUiSet, true);
    }

    private void SetStatusScreenActive(OperationUiSet uiSet, bool active)
    {
        if (uiSet != null && uiSet.craneStatusScreen != null)
        {
            uiSet.craneStatusScreen.SetActive(active);
        }
    }

    private OperationUiSet GetActiveDisplayUiSet()
    {
        if (!EnsureDisplayLayoutManagerIsReady() ||
            !displayLayoutManager.IsModeSelected)
        {
            return null;
        }

        return displayLayoutManager.CurrentMode ==
               DisplayLayoutManager.DisplayLayoutMode.MultiDisplay
            ? multiDisplayUiSet
            : singleDisplayUiSet;
    }

    private bool EnsureDisplayLayoutManagerIsReady()
    {
        if (displayLayoutManager == null)
        {
            displayLayoutManager =
                FindObjectOfType<DisplayLayoutManager>(true);
        }

        return displayLayoutManager != null;
    }

    private void SubscribeToDisplayLayoutManager()
    {
        if (!EnsureDisplayLayoutManagerIsReady()) return;

        displayLayoutManager.LayoutModeApplied -=
            HandleDisplayLayoutModeApplied;
        displayLayoutManager.LayoutModeApplied +=
            HandleDisplayLayoutModeApplied;
    }

    private void UnsubscribeFromDisplayLayoutManager()
    {
        if (displayLayoutManager == null) return;

        displayLayoutManager.LayoutModeApplied -=
            HandleDisplayLayoutModeApplied;
    }

    private void HandleDisplayLayoutModeApplied(
        DisplayLayoutManager.DisplayLayoutMode mode
    )
    {
        bool showStatusScreen =
            operationMode == OperationMode.MultiCraneManagement;

        SetStatusScreensActive(showStatusScreen);
        UpdateWaitingScreen();
    }

    private void HandleActiveCraneCountChanged(int activeCraneCount)
    {
        if (operationMode == OperationMode.SingleCrane)
        {
            if (activeCraneCount <= 0)
            {
                currentCraneIndex = -1;
            }
            else
            {
                singleCraneIndex = Mathf.Clamp(
                    singleCraneIndex,
                    0,
                    activeCraneCount - 1
                );
                currentCraneIndex = singleCraneIndex;
            }
        }
        else if (!IsActiveCraneIndex(currentCraneIndex))
        {
            currentCraneIndex = -1;
            SetSelectionLock(false);
        }

        UpdateCraneButtonVisibility();
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateWaitingScreen();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();
        UpdateSpeedDisplayTexts();
        ApplySpeedControlModeToCranes();

        Debug.Log(
            $"CraneOperationManager：操作対象を{activeCraneCount}基に更新しました。"
        );
    }

    private int GetActiveCraneCount()
    {
        if (!EnsureRegistryIsReady()) return 0;
        return craneRegistry.ActiveCraneCount;
    }

    private bool IsActiveCraneIndex(int craneIndex)
    {
        if (!EnsureRegistryIsReady()) return false;
        return craneRegistry.IsRuntimeIndexActive(craneIndex);
    }

    private bool EnsureRegistryIsReady()
    {
        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }

        if (craneRegistry == null)
        {
            Debug.LogError(
                "CraneOperationManagerにCraneRegistryが設定されていません。",
                this
            );
            return false;
        }

        if (craneRegistry.TotalCraneCount == 0)
        {
            craneRegistry.RefreshRegistry();
        }

        return craneRegistry.TotalCraneCount > 0;
    }

    private float GetAxisFromKeys(KeyCode positive, KeyCode negative)
    {
        float input = 0f;
        if (Input.GetKey(positive)) input += 1f;
        if (Input.GetKey(negative)) input -= 1f;
        return input;
    }
}
