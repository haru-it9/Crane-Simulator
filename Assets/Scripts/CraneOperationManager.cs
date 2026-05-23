using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CraneOperationManager : MonoBehaviour
{
    public enum InputMode
    {
        Keyboard,
        Joystick
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
    [SerializeField] private Camera[] craneCameras;
    [SerializeField] private int currentCraneIndex = 0;

    private CraneUnit CurrentCrane
    {
        get
        {
            if (cranes == null || cranes.Length == 0) return null;
            return cranes[currentCraneIndex];
        }
    }

    private void Start()
    {
        UpdateActiveCamera();
    }

    private void Update()
    {
        if (CurrentCrane == null) return;

        HandleCraneSelection();
        HandleSpeedSwitch();
        HandleMovement();
    }

    private void HandleCraneSelection()
    {
        if (inputMode == InputMode.Keyboard)
        {
            // 例: Tabで操作対象クレーン切替
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                currentCraneIndex = (currentCraneIndex + 1) % cranes.Length;
                Debug.Log($"操作対象クレーン: {CurrentCrane.name}");
                UpdateActiveCamera();
            }
        }
        // ジョイスティックでのクレーン切替例
        if (Input.GetButtonDown("JoyStick2Trigger"))
        {
            currentCraneIndex = (currentCraneIndex + 1) % cranes.Length;
            Debug.Log($"操作対象クレーン: {CurrentCrane.name}");
            UpdateActiveCamera();
        }
        
    }

    private void UpdateActiveCamera()
    {
        if (craneCameras == null || craneCameras.Length == 0) return;

        for (int i = 0; i < craneCameras.Length; i++)
        {
            if (craneCameras[i] != null)
            {
                craneCameras[i].gameObject.SetActive(i == currentCraneIndex);
            }
        }
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