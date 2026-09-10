using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 試験で使用するクレーン基数を選択し、CraneRegistryへ反映します。
/// Crane IDが小さい順に、選択された基数分を使用対象にします。
/// </summary>
[DisallowMultipleComponent]
public class CraneCountManager : MonoBehaviour
{
    [Header("クレーン管理")]
    [SerializeField]
    private CraneRegistry craneRegistry;

    [Header("基数選択UI")]
    [Tooltip("UnityEngine.UI.Dropdownを登録します。選択肢は自動生成されます。")]
    [SerializeField]
    private Dropdown craneCountDropdown;

    [Tooltip("準備画面を開いたときに最初に選択される基数です。")]
    [SerializeField]
    [Min(1)]
    private int defaultCraneCount = 6;

    [Header("適用時の処理")]
    [Tooltip("有効クレーンに対応するBoardGeneratorで板を生成します。")]
    [SerializeField]
    private bool generateBoardsForActiveCranes = true;

    [Tooltip("対象外クレーンの生成済みの板を削除します。")]
    [SerializeField]
    private bool clearBoardsForInactiveCranes = true;

    [Tooltip("適用後に基数Dropdownを操作できないようにします。")]
    [SerializeField]
    private bool lockDropdownAfterApply = true;

    public int SelectedCraneCount
    {
        get
        {
            if (craneCountDropdown != null)
            {
                return craneCountDropdown.value + 1;
            }

            return defaultCraneCount;
        }
    }

    public int AppliedCraneCount { get; private set; }
    public bool IsApplied { get; private set; }

    private void Awake()
    {
        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }
    }

    private void Start()
    {
        InitializeDropdown();
    }

    /// <summary>
    /// 検出されたクレーン数に合わせて、1基～N基の選択肢を生成します。
    /// </summary>
    public void InitializeDropdown()
    {
        if (!EnsureRegistryIsReady())
        {
            return;
        }

        if (craneCountDropdown == null)
        {
            Debug.LogWarning(
                "CraneCountManagerにCrane Count Dropdownが設定されていません。",
                this
            );
            return;
        }

        int totalCraneCount = craneRegistry.TotalCraneCount;
        int initialCraneCount = Mathf.Clamp(
            defaultCraneCount,
            1,
            totalCraneCount
        );

        List<string> options = new List<string>();

        for (int count = 1; count <= totalCraneCount; count++)
        {
            options.Add($"{count}基");
        }

        craneCountDropdown.ClearOptions();
        craneCountDropdown.AddOptions(options);
        craneCountDropdown.SetValueWithoutNotify(initialCraneCount - 1);
        craneCountDropdown.RefreshShownValue();

        Debug.Log(
            $"クレーン基数Dropdownを1～{totalCraneCount}基で初期化しました。"
        );
    }

    /// <summary>
    /// Dropdownで選択されている基数を適用します。
    /// SimulatorStartManagerのStart/Debug処理から呼び出してください。
    /// </summary>
    public bool ApplySelectedCraneCount()
    {
        return ApplyCraneCount(SelectedCraneCount);
    }

    /// <summary>
    /// 指定された基数を適用します。
    /// </summary>
    public bool ApplyCraneCount(int requestedCraneCount)
    {
        if (!EnsureRegistryIsReady())
        {
            return false;
        }

        int appliedCount = Mathf.Clamp(
            requestedCraneCount,
            1,
            craneRegistry.TotalCraneCount
        );

        craneRegistry.SetActiveCraneCount(appliedCount);

        for (int runtimeIndex = 0;
             runtimeIndex < craneRegistry.TotalCraneCount;
             runtimeIndex++)
        {
            CraneInstance crane =
                craneRegistry.GetCraneByRuntimeIndex(runtimeIndex);

            if (crane == null)
            {
                continue;
            }

            bool shouldBeActive =
                craneRegistry.IsRuntimeIndexActive(runtimeIndex);

            if (shouldBeActive)
            {
                ActivateCrane(crane);
            }
            else
            {
                DeactivateCrane(crane);
            }
        }

        AppliedCraneCount = appliedCount;
        IsApplied = true;

        if (lockDropdownAfterApply && craneCountDropdown != null)
        {
            craneCountDropdown.interactable = false;
        }

        Debug.Log(
            $"今回の試験で使用するクレーンを{AppliedCraneCount}基に設定しました。"
        );

        return true;
    }

    private void ActivateCrane(CraneInstance crane)
    {
        // MainCrane配下のCameraのON/OFFはCraneOperationManagerに任せます。
        // ここではクレーン本体が無効だった場合だけ有効に戻します。
        if (!crane.gameObject.activeSelf)
        {
            crane.gameObject.SetActive(true);
        }

        BoardGenerator boardGenerator = crane.BoardGenerator;

        if (boardGenerator == null)
        {
            Debug.LogWarning(
                $"{crane.name}にBoardGeneratorが設定されていません。",
                crane
            );
            return;
        }

        if (!boardGenerator.gameObject.activeSelf)
        {
            boardGenerator.gameObject.SetActive(true);
        }

        if (generateBoardsForActiveCranes)
        {
            boardGenerator.SpawnBoardsWithStage();
        }
    }

    private void DeactivateCrane(CraneInstance crane)
    {
        BoardGenerator boardGenerator = crane.BoardGenerator;

        if (boardGenerator != null)
        {
            if (clearBoardsForInactiveCranes)
            {
                boardGenerator.ClearGeneratedObjects();
            }

            boardGenerator.gameObject.SetActive(false);
        }

        // BirdCameraなど、MainCraneの外側にあるクレーン別Cameraを停止します。
        Camera[] externalCameras = crane.GetExternalCameras();

        foreach (Camera externalCamera in externalCameras)
        {
            if (externalCamera != null)
            {
                externalCamera.gameObject.SetActive(false);
            }
        }

        crane.gameObject.SetActive(false);
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
                "CraneRegistryが見つかりません。",
                this
            );
            return false;
        }

        if (craneRegistry.TotalCraneCount == 0)
        {
            craneRegistry.RefreshRegistry();
        }

        if (craneRegistry.TotalCraneCount == 0)
        {
            Debug.LogError(
                "使用可能なCraneInstanceが見つかりません。",
                this
            );
            return false;
        }

        return true;
    }

    private void OnValidate()
    {
        defaultCraneCount = Mathf.Max(1, defaultCraneCount);
    }
}
