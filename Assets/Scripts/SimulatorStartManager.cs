using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SimulatorStartManager : MonoBehaviour
{
    [Header("StartScreen Canvas")]
    [SerializeField] private GameObject startScreen;

    [Header("CSV Logger")]
    [SerializeField] private ControllerInputCsvLogger inputLogger;

    [Header("UI Button Logger")]
    [SerializeField] private UIButtonCsvLogger uiButtonLogger;

    [Header("Crane Position Logger")]
    [SerializeField] private CranePositionCsvLogger cranePositionLogger;

    [Header("Tobii Gaze Logger")]
    [SerializeField] private TobiiGazeCsvLogger tobiiGazeLogger;

    [Header("Work Information Logger")]
    [SerializeField] private WorkInformationCsvLogger workInformationLogger;

    [Header("CSVファイル名入力")]
    [SerializeField] private InputField fileNameInputField;

    [Header("Start前に無効化する操作系UIの親オブジェクト")]
    [SerializeField] private GameObject[] operationUiRoots;

    [Header("無効化対象から除外するUI")]
    [SerializeField] private Selectable[] excludeSelectables;

    public static bool IsOperationEnabled { get; private set; } = false;

    private void Start()
    {
        IsOperationEnabled = false;

        if (startScreen != null)
        {
            startScreen.SetActive(true);
        }

        SetOperationUIInteractable(false);
    }

    public void OnStartButtonClicked()
    {
        IsOperationEnabled = true;

        string inputFileName = "";

        if (fileNameInputField != null)
        {
            inputFileName = fileNameInputField.text;
        }

        if (startScreen != null)
        {
            startScreen.SetActive(false);
        }

        SetOperationUIInteractable(true);

        if (inputLogger != null)
        {
            inputLogger.StartLogging(inputFileName);
        }

        if (uiButtonLogger != null)
        {
            uiButtonLogger.StartLogging(inputFileName);
        }

        if (cranePositionLogger != null)
        {
            cranePositionLogger.StartLogging(inputFileName);
        }

        if (tobiiGazeLogger != null)
        {
            tobiiGazeLogger.StartLogging(inputFileName);
        }

        if (workInformationLogger != null)
        {
            workInformationLogger.StartLogging(inputFileName);
        }

        Debug.Log("Start：操作開始＋CSV記録開始");
    }

    public void OnDebugButtonClicked()
    {
        IsOperationEnabled = true;

        if (startScreen != null)
        {
            startScreen.SetActive(false);
        }

        SetOperationUIInteractable(true);

        Debug.Log("Debug：操作開始、CSV記録なし");
    }

    private void SetOperationUIInteractable(bool interactable)
    {
        if (operationUiRoots == null) return;

        foreach (GameObject root in operationUiRoots)
        {
            if (root == null) continue;

            Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);

            foreach (Selectable selectable in selectables)
            {
                if (selectable == null) continue;
                if (IsExcluded(selectable)) continue;

                selectable.interactable = interactable;
            }
        }
    }

    private bool IsExcluded(Selectable selectable)
    {
        if (excludeSelectables == null) return false;

        foreach (Selectable excluded in excludeSelectables)
        {
            if (excluded == selectable)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsInputFieldFocused()
    {
        if (EventSystem.current == null) return false;

        GameObject selectedObject = EventSystem.current.currentSelectedGameObject;

        if (selectedObject == null) return false;

        InputField inputField = selectedObject.GetComponent<InputField>();

        return inputField != null && inputField.isFocused;
    }
}