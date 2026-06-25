using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Operation Mode")]
    [SerializeField] private OperationMode operationMode = OperationMode.MultiCraneManagement;

    [Tooltip("SingleCraneモードで操作するクレーン番号。Crane1なら0、Crane2なら1")]
    [SerializeField] private int singleCraneIndex = 0;

    [Header("Display5")]
    [SerializeField] private GameObject craneStatusScreen;

    [System.Serializable]
    public class CraneCameraSet
    {
        public Camera[] cameras = new Camera[7];
    }

    [System.Serializable]
    public class CraneDisplaySet
    {
        public Transform informationTarget;
        public LifMagSystem lifMagSystem;
    }

    [Header("Intervention Scenario Manager")]
    [SerializeField] private CraneInterventionScenarioManager interventionScenarioManager;

    [Header("Input Settings")]
    [SerializeField] private InputMode inputMode = InputMode.Keyboard;

    [Header("Speed Control Mode")]
    [SerializeField] private SpeedControlMode speedControlMode = SpeedControlMode.ButtonAndKeyboard;

    [Header("Speed Control UI Buttons")]
    [SerializeField] private GameObject[] speedControlUIButtons;

    [Header("Joystick Axes")]
    [SerializeField] private string joyStick2Horizontal = "JoyStick2Horizontal";
    [SerializeField] private string joyStick2Vertical = "JoyStick2Vertical";
    [SerializeField] private string joyStick3Vertical = "JoyStick3Vertical";
    [SerializeField] private string joyStick2Trigger = "JoyStick2Trigger";
    [SerializeField] private string joyStick3MiniVertical = "JoyStick3MiniVertical";

    [Header("Dead Zone")]
    [SerializeField] private float deadZone = 0.1f;
    
    [Header("Crane Settings")]
    [SerializeField] private CraneUnit[] cranes;
    [SerializeField] private CraneCameraSet[] craneCameraSets;
    [SerializeField] private int currentCraneIndex = 0;

    [Header("Waiting Screen")]
    [SerializeField] private GameObject waitingScreen;

    [Header("Display2")]
    [SerializeField] private CraneInformationDisplay craneInformationDisplay;
    [SerializeField] private CraneDisplaySet[] craneDisplaySets;

    [Header("LifMag UI")]
    [SerializeField] private LifMagCurrentButton[] lifMagCurrentButtons;

    [Header("Current Crane Display")]
    [SerializeField] private Text currentCraneNameText;

    [Header("Display5 Status")]
    [SerializeField] private CraneStatusManager craneStatusManager;

    [Header("Crane Select UI")]
    [SerializeField] private Button[] craneSelectButtons; // Crane1〜6のボタン
    [SerializeField] private Button lockUnlockButton;
    [SerializeField] private Text lockUnlockButtonText;

    [SerializeField] private Color normalButtonColor = Color.white;
    [SerializeField] private Color selectedButtonColor = Color.yellow;
    [SerializeField] private Color unlockColor = new Color(0.7f, 1.0f, 0.7f); // 淡い緑
    [SerializeField] private Color lockColor = new Color(1.0f, 0.7f, 0.7f);   // 淡い赤

    private bool isSelectionLocked = false;

    public CraneUnit CurrentCrane
    {
        get
        {
            if (cranes == null || cranes.Length == 0) return null;
            if (currentCraneIndex < 0 || currentCraneIndex >= cranes.Length) return null;

            return cranes[currentCraneIndex];
        }
    }

    private void Start()
    {
        ApplyOperationMode();
        
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();

        UpdateSpeedControlUI();

        ApplySpeedControlModeToCranes();
    }

    private void Update()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        if (CurrentCrane == null) return;

        // InputField入力中はキーボードによる速度切替を受け付けない
        if (SimulatorStartManager.IsInputFieldFocused()) return;

        HandleSpeedSwitch();
    }

    private void ApplyOperationMode()
    {
        if (operationMode == OperationMode.SingleCrane)
        {
            if (cranes == null || cranes.Length == 0)
            {
                currentCraneIndex = -1;
            }
            else
            {
                singleCraneIndex = Mathf.Clamp(singleCraneIndex, 0, cranes.Length - 1);
                currentCraneIndex = singleCraneIndex;
            }

            // 単一モードではDisplay1 WaitingScreenを表示しない
            if (waitingScreen != null)
            {
                waitingScreen.SetActive(false);
            }

            // 単一モードではDisplay5 CraneStatusScreenを表示しない
            if (craneStatusScreen != null)
            {
                craneStatusScreen.SetActive(false);
            }

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

            // 複数台管理モードではDisplay5を表示
            if (craneStatusScreen != null)
            {
                craneStatusScreen.SetActive(true);
            }

            // ★追加：複数台管理モードではCraneStatusManagerを再開
            if (craneStatusManager != null)
            {
                craneStatusManager.SetStatusManagementEnabled(true);
            }

            UpdateWaitingScreen();

            SetSelectionLock(false);
        }
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

        if (cranes == null || cranes.Length == 0) return;

        if (craneIndex < 0 || craneIndex >= cranes.Length)
        {
            Debug.LogWarning($"存在しないクレーン番号です: {craneIndex}");
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

        SetSelectionLock(true);
    }

    private void UpdateActiveCamera()
    {
        if (craneCameraSets == null || craneCameraSets.Length == 0) return;

        // 未選択状態ではカメラ状態を変更しない
        if (currentCraneIndex < 0)
        {
            return;
        }

        for (int i = 0; i < craneCameraSets.Length; i++)
        {
            bool isActiveCrane = i == currentCraneIndex;

            if (craneCameraSets[i] == null || craneCameraSets[i].cameras == null) continue;

            for (int j = 0; j < craneCameraSets[i].cameras.Length; j++)
            {
                Camera cam = craneCameraSets[i].cameras[j];

                if (cam != null)
                {
                    cam.gameObject.SetActive(isActiveCrane);
                }
            }
        }
    }

    private void UpdateWaitingScreen()
    {
        if (operationMode == OperationMode.SingleCrane)
        {
            if (waitingScreen != null)
            {
                waitingScreen.SetActive(false);
            }
            return;
        }

        if (waitingScreen != null)
        {
            waitingScreen.SetActive(CurrentCrane == null);
        }
    }

    private void UpdateDisplay2()
    {
        if (craneInformationDisplay == null) return;
        if (craneDisplaySets == null) return;
        if (currentCraneIndex < 0 || currentCraneIndex >= craneDisplaySets.Length) return;

        CraneDisplaySet set = craneDisplaySets[currentCraneIndex];

        craneInformationDisplay.SetTarget(
            set.informationTarget,
            set.lifMagSystem
        );
    }

    public void EnterWaitingMode()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;

        if (operationMode == OperationMode.SingleCrane)
        {
            Debug.Log("SingleCraneモード中のため、WaitingScreenには戻りません");
            return;
        }

        // 介入開始時に生成した厚板・人オブジェクトを削除する
        if (interventionScenarioManager != null)
        {
            interventionScenarioManager.ClearCurrentScenarioObjects();
        }

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

        if (speedControlUIButtons == null) return;

        foreach (GameObject obj in speedControlUIButtons)
        {
            if (obj != null)
            {
                obj.SetActive(showButtons);
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
        if (lifMagCurrentButtons == null) return;

        for (int i = 0; i < lifMagCurrentButtons.Length; i++)
        {
            if (lifMagCurrentButtons[i] == null) continue;

            bool isOn = CurrentCrane.LifMagSystem.GetLifMagCurrent(i);
            float currentValue = CurrentCrane.LifMagSystem.GetLifMagDisplayAccumValue(i);

            lifMagCurrentButtons[i].SetViewOnly(isOn);
            lifMagCurrentButtons[i].SetCurrentValueView(currentValue);
        }
    }

    private void UpdateCurrentCraneNameText()
    {
        if (currentCraneNameText == null) return;
        
        if (currentCraneIndex < 0 || cranes == null || currentCraneIndex >= cranes.Length)
        {
            currentCraneNameText.text = "未選択";
            return;
        }
        currentCraneNameText.text = $"Crane {currentCraneIndex + 1}";;
    }

    public void ToggleSelectionLock()
    {
        if (!SimulatorStartManager.IsOperationEnabled) return;
        
        SetSelectionLock(!isSelectionLocked);
    }

    private void SetSelectionLock(bool locked)
    {
        isSelectionLocked = locked;

        if (lockUnlockButtonText != null)
        {
            lockUnlockButtonText.text = isSelectionLocked ? "Lock" : "Unlock";
        }

        if (lockUnlockButton != null)
        {
            Image buttonImage = lockUnlockButton.GetComponent<Image>();

            if (buttonImage != null)
            {
                buttonImage.color = isSelectionLocked
                    ? lockColor
                    : unlockColor;
            }
        }
    }

    private void UpdateCraneButtonColors()
    {
        if (craneSelectButtons == null) return;

        for (int i = 0; i < craneSelectButtons.Length; i++)
        {
            if (craneSelectButtons[i] == null) continue;

            Image buttonImage = craneSelectButtons[i].GetComponent<Image>();
            if (buttonImage == null) continue;

            buttonImage.color = (i == currentCraneIndex)
                ? selectedButtonColor
                : normalButtonColor;
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
            return ApplyDeadZone(-Input.GetAxis(joyStick3Vertical));
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
            return ApplyDeadZone(Input.GetAxis(joyStick2Vertical));
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
            return ApplyDeadZone(-Input.GetAxis(joyStick2Horizontal));
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
        if (cranes == null) return;

        bool useJoystickStep = speedControlMode == SpeedControlMode.JoystickStep;

        foreach (CraneUnit crane in cranes)
        {
            if (crane != null)
            {
                crane.SetJoystickStepSpeedMode(useJoystickStep);
            }
        }
    }

    private float GetAxisFromKeys(KeyCode positive, KeyCode negative)
    {
        float input = 0f;
        if (Input.GetKey(positive)) input += 1f;
        if (Input.GetKey(negative)) input -= 1f;
        return input;
    }
}