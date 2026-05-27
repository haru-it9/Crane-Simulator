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

    [Header("Input Settings")]
    [SerializeField] private InputMode inputMode = InputMode.Keyboard;

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
        currentCraneIndex = -1;
        UpdateWaitingScreen();
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();

        SetSelectionLock(false); // 初期状態はUnlock
    }

    private void Update()
    {
        if (CurrentCrane == null) return;

        //HandleCraneSelection();
        HandleSpeedSwitch();
        //HandleMovement();
    }

    private void FixedUpdate()
    {
        if (CurrentCrane == null) return;

        HandleMovement();
    }

    public void HandleCraneSelection(int craneIndex)
    {
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

        Debug.Log($"操作対象クレーン: {CurrentCrane.name}");

        CurrentCrane.ResetSpeedLevel();

        UpdateWaitingScreen();
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateDisplay2();
        UpdateLifMagButtonViews();
        UpdateCurrentCraneNameText();

        SetSelectionLock(true); // 選択後は自動Lock
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
        currentCraneIndex = -1;

        UpdateWaitingScreen();
        UpdateActiveCamera();
        UpdateCraneButtonColors();
        UpdateCurrentCraneNameText();

        SetSelectionLock(false);
    }

    public void IncreaseCurrentCraneXSpeed()
    {
        if (CurrentCrane == null) return;
        CurrentCrane.IncreaseMainLifMagXSpeed();
    }

    public void DecreaseCurrentCraneXSpeed()
    {
        if (CurrentCrane == null) return;
        CurrentCrane.DecreaseMainLifMagXSpeed();
    }

    public void IncreaseCurrentCraneYSpeed()
    {
        if (CurrentCrane == null) return;
        CurrentCrane.IncreaseMainLifMagYSpeed();
    }

    public void DecreaseCurrentCraneYSpeed()
    {
        if (CurrentCrane == null) return;
        CurrentCrane.DecreaseMainLifMagYSpeed();
    }

    public void IncreaseCurrentCraneZSpeed()
    {
        if (CurrentCrane == null) return;
        CurrentCrane.IncreaseZSpeed();
    }

    public void DecreaseCurrentCraneZSpeed()
    {
        if (CurrentCrane == null) return;
        CurrentCrane.DecreaseZSpeed();
    }

    public void SetCurrentCraneLifMagCurrent(int index, bool isOn)
    {
        if (CurrentCrane == null) return;
        if (CurrentCrane.LifMagSystem == null) return;

        CurrentCrane.LifMagSystem.SetLifMagCurrent(index, isOn);
    }

    public void ResetCurrentCraneLifMag()
    {
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
        if (craneStatusManager == null) return;

        craneStatusManager.CompleteErrorByCraneIndex(currentCraneIndex);
    }
    
    private void HandleSpeedSwitch()
    {
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
        return value > 0f ? 1f : -1f;
    }

    private float GetAxisFromKeys(KeyCode positive, KeyCode negative)
    {
        float input = 0f;
        if (Input.GetKey(positive)) input += 1f;
        if (Input.GetKey(negative)) input -= 1f;
        return input;
    }
}