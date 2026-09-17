using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SimulatorStartManager : MonoBehaviour
{
    private const int TaskSwitchCraneCount = 2;

    public enum SimulatorMode
    {
        AutomaticIntervention,
        TaskSwitchExperiment
    }

    [Header("Simulator Mode")]
    [SerializeField]
    private SimulatorMode simulatorMode =
        SimulatorMode.AutomaticIntervention;

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

    [Header("Extended Experiment Logger")]
    [SerializeField]
    private ExtendedExperimentCsvLogger extendedExperimentCsvLogger;

    [Header("Task Switch Experiment Logger")]
    [SerializeField]
    private TaskSwitchExperimentCsvLogger taskSwitchExperimentCsvLogger;

    [Header("Crane Status Manager")]
    [SerializeField] private CraneStatusManager craneStatusManager;

    [Header("CSVファイル名入力")]
    [SerializeField] private InputField fileNameInputField;

    [Header("Start前に無効化する操作系UIの親オブジェクト")]
    [SerializeField] private GameObject[] operationUiRoots;

    [Header("無効化対象から除外するUI")]
    [SerializeField] private Selectable[] excludeSelectables;

    [Header("Crane Count Manager")]
    [SerializeField]
    private CraneCountManager craneCountManager;

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
        if (craneCountManager == null)
        {
            Debug.LogError("CraneCountManagerが設定されていません。");
            return;
        }

        if (!ApplyCraneCountForSelectedMode())
        {
            return;
        }

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

        ApplySelectedSimulatorMode();

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

        if (extendedExperimentCsvLogger != null)
        {
            extendedExperimentCsvLogger.StartLogging(inputFileName);
        }

        if (simulatorMode == SimulatorMode.TaskSwitchExperiment &&
            taskSwitchExperimentCsvLogger != null)
        {
            taskSwitchExperimentCsvLogger.StartLogging(inputFileName);
        }

        Debug.Log("Start：操作開始＋CSV記録開始");
    }

    public void OnDebugButtonClicked()
    {
        if (craneCountManager == null)
        {
            Debug.LogError("CraneCountManagerが設定されていません。");
            return;
        }

        if (!ApplyCraneCountForSelectedMode())
        {
            return;
        }
        
        IsOperationEnabled = true;

        if (startScreen != null)
        {
            startScreen.SetActive(false);
        }

        SetOperationUIInteractable(true);

        ApplySelectedSimulatorMode();

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

    private void ApplySelectedSimulatorMode()
    {
        if (craneStatusManager == null)
        {
            return;
        }

        if (simulatorMode == SimulatorMode.TaskSwitchExperiment)
        {
            craneStatusManager.SetStatusManagementEnabled(false);
            Debug.Log(
                "作業切替実験モード：自動クレーン状態管理は開始しません"
            );
            return;
        }

        craneStatusManager.SetStatusManagementEnabled(true);
        craneStatusManager.StartStatusManagementFromSimulator();
    }

    /// <summary>
    /// 通常管理モードではDropdownの選択基数を使用し、
    /// 作業切替実験では選択値に関係なく2基へ固定します。
    /// </summary>
    private bool ApplyCraneCountForSelectedMode()
    {
        if (craneCountManager == null)
        {
            Debug.LogError("CraneCountManagerが設定されていません。");
            return false;
        }

        if (simulatorMode != SimulatorMode.TaskSwitchExperiment)
        {
            return craneCountManager.ApplySelectedCraneCount();
        }

        if (!craneCountManager.ApplyCraneCount(TaskSwitchCraneCount))
        {
            return false;
        }

        if (craneCountManager.AppliedCraneCount !=
            TaskSwitchCraneCount)
        {
            Debug.LogError(
                "作業切替実験にはクレーンが2基必要です。" +
                $"現在の適用基数: {craneCountManager.AppliedCraneCount}"
            );
            return false;
        }

        Debug.Log(
            "作業切替実験モードのため、管理基数の選択に関係なく" +
            "クレーンを2基に設定しました。"
        );
        return true;
    }

    public void SetSimulatorMode(int modeIndex)
    {
        simulatorMode = modeIndex == 1
            ? SimulatorMode.TaskSwitchExperiment
            : SimulatorMode.AutomaticIntervention;
    }

    /// <summary>
    /// DisplayLayoutManagerがTaskSwitchDisplayを選択したときに呼びます。
    /// Editor上でもSerialized Fieldへ反映します。
    /// </summary>
    public void SelectTaskSwitchExperimentMode()
    {
        SetSimulatorModeAndMarkDirty(
            SimulatorMode.TaskSwitchExperiment
        );
    }

    /// <summary>
    /// DisplayLayoutManagerがMulti・Single・Mixを選択したときに呼びます。
    /// Editor上でもSerialized Fieldへ反映します。
    /// </summary>
    public void SelectAutomaticInterventionMode()
    {
        SetSimulatorModeAndMarkDirty(
            SimulatorMode.AutomaticIntervention
        );
    }

    private void SetSimulatorModeAndMarkDirty(
        SimulatorMode newMode
    )
    {
        if (simulatorMode == newMode)
        {
            return;
        }

        simulatorMode = newMode;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);

            if (gameObject.scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager
                    .MarkSceneDirty(gameObject.scene);
            }
        }
#endif
    }

    public bool IsTaskSwitchExperimentSelected()
    {
        return simulatorMode == SimulatorMode.TaskSwitchExperiment;
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

    /// <summary>
    /// 起動したすべてのCSV Loggerを終了し、未書き込みのデータを確定します。
    /// </summary>
    public void StopAllLogging()
    {
        if (inputLogger != null)
        {
            inputLogger.StopLogging();
        }

        if (uiButtonLogger != null)
        {
            uiButtonLogger.StopLogging();
        }

        if (cranePositionLogger != null)
        {
            cranePositionLogger.StopLogging();
        }

        if (tobiiGazeLogger != null)
        {
            tobiiGazeLogger.StopLogging();
        }

        if (workInformationLogger != null)
        {
            workInformationLogger.StopLogging();
        }

        if (extendedExperimentCsvLogger != null)
        {
            extendedExperimentCsvLogger.StopLogging();
        }

        if (taskSwitchExperimentCsvLogger != null)
        {
            taskSwitchExperimentCsvLogger.StopLogging();
        }
    }

    private void OnApplicationQuit()
    {
        StopAllLogging();
    }

    private void OnDestroy()
    {
        StopAllLogging();
    }
}
