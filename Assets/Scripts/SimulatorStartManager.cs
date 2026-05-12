using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("CSVファイル名入力")]
    [SerializeField] private InputField fileNameInputField;

    public static bool IsOperationEnabled { get; private set; } = false;

    private void Start()
    {
        IsOperationEnabled = false;

        if (startScreen != null)
        {
            startScreen.SetActive(true);
        }
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

        Debug.Log("Start：操作開始＋CSV記録開始");
    }

    public void OnDebugButtonClicked()
    {
        IsOperationEnabled = true;

        if (startScreen != null)
        {
            startScreen.SetActive(false);
        }

        Debug.Log("Debug：操作開始、CSV記録なし");
    }
}
