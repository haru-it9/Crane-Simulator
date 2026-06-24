using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class CraneInterventionScenarioManager : MonoBehaviour
{
    public enum RandomSourceMode
    {
        CsvList,
        RandomRange
    }

    [Serializable]
    public class FloatRange
    {
        public float min;
        public float max;

        public float RandomValue()
        {
            return UnityEngine.Random.Range(min, max);
        }
    }

    [Serializable]
    public class Vector3Range
    {
        public Vector3 min;
        public Vector3 max;

        public Vector3 RandomValue()
        {
            return new Vector3(
                UnityEngine.Random.Range(min.x, max.x),
                UnityEngine.Random.Range(min.y, max.y),
                UnityEngine.Random.Range(min.z, max.z)
            );
        }
    }

    [Serializable]
    public class CranePoseRangeSetting
    {
        public CraneStatusManager.ErrorType errorType;

        [Header("mainCrane.localPosition.z")]
        public FloatRange mainCraneLocalZ;

        [Header("mainLifMag.localPosition.x")]
        public FloatRange mainLifMagLocalX;

        [Header("mainLifMag.localPosition.y")]
        public FloatRange mainLifMagLocalY;
    }

    [Serializable]
    public class PlateSizeRangeSetting
    {
        public CraneStatusManager.ErrorType errorType;
        public Vector3Range sizeRange;
    }

    [Serializable]
    public class HumanPositionRangeSetting
    {
        public CraneStatusManager.ErrorType errorType;
        public Vector3Range positionRange;
        public FloatRange rotationYRange;
    }

    private struct CranePose
    {
        public float mainCraneLocalZ;
        public float mainLifMagLocalX;
        public float mainLifMagLocalY;
    }

    private struct PlateSizeData
    {
        public Vector3 size;
    }

    private struct HumanPose
    {
        public Vector3 position;
        public float rotationY;
    }

    [Header("Crane Position Random Mode")]
    [SerializeField] private RandomSourceMode cranePositionMode = RandomSourceMode.RandomRange;
    [SerializeField] private TextAsset cranePositionCsv;
    [SerializeField] private CranePoseRangeSetting[] cranePoseRanges;

    [Header("Plate")]
    [SerializeField] private GameObject platePrefab;
    [SerializeField] private RandomSourceMode plateSizeMode = RandomSourceMode.RandomRange;
    [SerializeField] private TextAsset plateSizeCsv;
    [SerializeField] private PlateSizeRangeSetting[] plateSizeRanges;

    [Header("Plate Attached Pose")]
    [SerializeField] private Vector3 attachedPlateLocalPosition = new Vector3(0f, -0.5f, 0f);
    [SerializeField] private Vector3 attachedPlateLocalEuler = Vector3.zero;

    [Header("Human")]
    [SerializeField] private GameObject humanPrefab;
    [SerializeField] private RandomSourceMode humanPositionMode = RandomSourceMode.RandomRange;
    [SerializeField] private TextAsset humanPositionCsv;
    [SerializeField] private HumanPositionRangeSetting[] humanPositionRanges;

    [Header("Option")]
    [SerializeField] private bool clearPreviousScenarioObjects = true;

    private GameObject currentPlate;
    private GameObject currentHuman;
    private CraneUnit currentCraneUnit;

    private readonly List<CranePose> cranePoseCsvRows = new List<CranePose>();
    private readonly List<CraneStatusManager.ErrorType> cranePoseCsvErrorTypes = new List<CraneStatusManager.ErrorType>();

    private readonly List<PlateSizeData> plateSizeCsvRows = new List<PlateSizeData>();
    private readonly List<CraneStatusManager.ErrorType> plateSizeCsvErrorTypes = new List<CraneStatusManager.ErrorType>();

    private readonly List<HumanPose> humanPoseCsvRows = new List<HumanPose>();
    private readonly List<CraneStatusManager.ErrorType> humanPoseCsvErrorTypes = new List<CraneStatusManager.ErrorType>();

    private void Awake()
    {
        LoadCsvData();
    }

    public void SetupInterventionState(
        CraneUnit craneUnit,
        CraneStatusManager.WorkPhase phase,
        CraneStatusManager.ErrorType errorType
    )
    {
        if (craneUnit == null)
        {
            Debug.LogWarning("CraneUnit が null です");
            return;
        }

        if (clearPreviousScenarioObjects)
        {
            ClearCurrentScenarioObjects();
        }

        currentCraneUnit = craneUnit;

        CranePose cranePose = GetRandomCranePose(errorType);

        craneUnit.SetInterventionPose(
            cranePose.mainCraneLocalZ,
            cranePose.mainLifMagLocalX,
            cranePose.mainLifMagLocalY
        );

        SetupPlate(craneUnit, phase, errorType);
        SetupHuman(errorType);

        Debug.Log(
            $"介入開始状態を生成: Phase={phase}, Error={errorType}, " +
            $"Z={cranePose.mainCraneLocalZ}, X={cranePose.mainLifMagLocalX}, Y={cranePose.mainLifMagLocalY}"
        );
    }

    public void ClearCurrentScenarioObjects()
    {
        if (currentCraneUnit != null)
        {
            currentCraneUnit.ClearInterventionBoardAttachment();
        }

        if (currentPlate != null)
        {
            Destroy(currentPlate);
            currentPlate = null;
        }

        if (currentHuman != null)
        {
            Destroy(currentHuman);
            currentHuman = null;
        }
    }

    private void SetupPlate(
        CraneUnit craneUnit,
        CraneStatusManager.WorkPhase phase,
        CraneStatusManager.ErrorType errorType
    )
    {
        bool shouldAttachPlate = ShouldAttachPlate(phase, errorType);

        // 吸着しない場合は、厚板を生成しない
        if (!shouldAttachPlate)
        {
            craneUnit.ClearInterventionBoardAttachment();
            return;
        }

        if (platePrefab == null)
        {
            Debug.LogWarning("Plate Prefab が設定されていません");
            craneUnit.ClearInterventionBoardAttachment();
            return;
        }

        Vector3 plateSize = GetRandomPlateSize(errorType);

        currentPlate = Instantiate(platePrefab);
        currentPlate.transform.localScale = plateSize;

        craneUnit.SetInterventionBoardAttached(
            currentPlate,
            attachedPlateLocalPosition,
            attachedPlateLocalEuler
        );
    }

    private void SetupHuman(CraneStatusManager.ErrorType errorType)
    {
        if (errorType != CraneStatusManager.ErrorType.ErrorA)
        {
            return;
        }

        if (humanPrefab == null)
        {
            Debug.LogWarning("Human Prefab が設定されていません");
            return;
        }

        HumanPose humanPose = GetRandomHumanPose(errorType);

        currentHuman = Instantiate(
            humanPrefab,
            humanPose.position,
            Quaternion.Euler(0f, humanPose.rotationY, 0f)
        );
    }

    private bool ShouldAttachPlate(
        CraneStatusManager.WorkPhase phase,
        CraneStatusManager.ErrorType errorType
    )
    {
        // 吊り上げ失敗は、厚板を吸着していない状態として扱う
        if (errorType == CraneStatusManager.ErrorType.ErrorB)
        {
            return false;
        }

        // トレーラ積込は、厚板を持った状態で開始
        if (errorType == CraneStatusManager.ErrorType.ErrorC)
        {
            return true;
        }

        // 通常は作業フェーズで判断
        return
            phase == CraneStatusManager.WorkPhase.Move2 ||
            phase == CraneStatusManager.WorkPhase.Place ||
            phase == CraneStatusManager.WorkPhase.PlaceToTrack;
    }

    private CranePose GetRandomCranePose(CraneStatusManager.ErrorType errorType)
    {
        if (cranePositionMode == RandomSourceMode.CsvList)
        {
            if (TryGetRandomCranePoseFromCsv(errorType, out CranePose csvPose))
            {
                return csvPose;
            }

            Debug.LogWarning($"CSV内に {errorType} のクレーン位置候補がありません。範囲モードにフォールバックします");
        }

        CranePoseRangeSetting setting = FindCranePoseRange(errorType);

        if (setting == null)
        {
            Debug.LogWarning($"{errorType} のクレーン位置範囲が未設定です。仮の値を使います");

            return new CranePose
            {
                mainCraneLocalZ = 0f,
                mainLifMagLocalX = 0f,
                mainLifMagLocalY = -1f
            };
        }

        return new CranePose
        {
            mainCraneLocalZ = setting.mainCraneLocalZ.RandomValue(),
            mainLifMagLocalX = setting.mainLifMagLocalX.RandomValue(),
            mainLifMagLocalY = setting.mainLifMagLocalY.RandomValue()
        };
    }

    private Vector3 GetRandomPlateSize(CraneStatusManager.ErrorType errorType)
    {
        if (plateSizeMode == RandomSourceMode.CsvList)
        {
            if (TryGetRandomPlateSizeFromCsv(errorType, out Vector3 csvSize))
            {
                return csvSize;
            }

            Debug.LogWarning($"CSV内に {errorType} の厚板サイズ候補がありません。範囲モードにフォールバックします");
        }

        PlateSizeRangeSetting setting = FindPlateSizeRange(errorType);

        if (setting == null)
        {
            Debug.LogWarning($"{errorType} の厚板サイズ範囲が未設定です。Vector3.one を使います");
            return Vector3.one;
        }

        return setting.sizeRange.RandomValue();
    }

    private HumanPose GetRandomHumanPose(CraneStatusManager.ErrorType errorType)
    {
        if (humanPositionMode == RandomSourceMode.CsvList)
        {
            if (TryGetRandomHumanPoseFromCsv(errorType, out HumanPose csvPose))
            {
                return csvPose;
            }

            Debug.LogWarning($"CSV内に {errorType} の人位置候補がありません。範囲モードにフォールバックします");
        }

        HumanPositionRangeSetting setting = FindHumanPositionRange(errorType);

        if (setting == null)
        {
            Debug.LogWarning($"{errorType} の人位置範囲が未設定です。原点に出します");

            return new HumanPose
            {
                position = Vector3.zero,
                rotationY = 0f
            };
        }

        return new HumanPose
        {
            position = setting.positionRange.RandomValue(),
            rotationY = setting.rotationYRange.RandomValue()
        };
    }

    private bool TryGetRandomCranePoseFromCsv(
        CraneStatusManager.ErrorType errorType,
        out CranePose result
    )
    {
        List<CranePose> candidates = new List<CranePose>();

        for (int i = 0; i < cranePoseCsvRows.Count; i++)
        {
            if (cranePoseCsvErrorTypes[i] == errorType)
            {
                candidates.Add(cranePoseCsvRows[i]);
            }
        }

        // errorTypeごとの候補がない場合、Noneを共通候補として使う
        if (candidates.Count == 0)
        {
            for (int i = 0; i < cranePoseCsvRows.Count; i++)
            {
                if (cranePoseCsvErrorTypes[i] == CraneStatusManager.ErrorType.None)
                {
                    candidates.Add(cranePoseCsvRows[i]);
                }
            }
        }

        if (candidates.Count > 0)
        {
            result = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            return true;
        }

        result = default;
        return false;
    }

    private bool TryGetRandomPlateSizeFromCsv(
        CraneStatusManager.ErrorType errorType,
        out Vector3 result
    )
    {
        List<PlateSizeData> candidates = new List<PlateSizeData>();

        for (int i = 0; i < plateSizeCsvRows.Count; i++)
        {
            if (plateSizeCsvErrorTypes[i] == errorType)
            {
                candidates.Add(plateSizeCsvRows[i]);
            }
        }

        // errorTypeごとの候補がない場合、Noneを共通候補として使う
        if (candidates.Count == 0)
        {
            for (int i = 0; i < plateSizeCsvRows.Count; i++)
            {
                if (plateSizeCsvErrorTypes[i] == CraneStatusManager.ErrorType.None)
                {
                    candidates.Add(plateSizeCsvRows[i]);
                }
            }
        }

        if (candidates.Count > 0)
        {
            result = candidates[UnityEngine.Random.Range(0, candidates.Count)].size;
            return true;
        }

        result = Vector3.one;
        return false;
    }

    private bool TryGetRandomHumanPoseFromCsv(
        CraneStatusManager.ErrorType errorType,
        out HumanPose result
    )
    {
        List<HumanPose> candidates = new List<HumanPose>();

        for (int i = 0; i < humanPoseCsvRows.Count; i++)
        {
            if (humanPoseCsvErrorTypes[i] == errorType)
            {
                candidates.Add(humanPoseCsvRows[i]);
            }
        }

        if (candidates.Count > 0)
        {
            result = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            return true;
        }

        result = default;
        return false;
    }

    private CranePoseRangeSetting FindCranePoseRange(CraneStatusManager.ErrorType errorType)
    {
        if (cranePoseRanges == null) return null;

        foreach (CranePoseRangeSetting setting in cranePoseRanges)
        {
            if (setting != null && setting.errorType == errorType)
            {
                return setting;
            }
        }

        foreach (CranePoseRangeSetting setting in cranePoseRanges)
        {
            if (setting != null && setting.errorType == CraneStatusManager.ErrorType.None)
            {
                return setting;
            }
        }

        return null;
    }

    private PlateSizeRangeSetting FindPlateSizeRange(CraneStatusManager.ErrorType errorType)
    {
        if (plateSizeRanges == null) return null;

        foreach (PlateSizeRangeSetting setting in plateSizeRanges)
        {
            if (setting != null && setting.errorType == errorType)
            {
                return setting;
            }
        }

        foreach (PlateSizeRangeSetting setting in plateSizeRanges)
        {
            if (setting != null && setting.errorType == CraneStatusManager.ErrorType.None)
            {
                return setting;
            }
        }

        return null;
    }

    private HumanPositionRangeSetting FindHumanPositionRange(CraneStatusManager.ErrorType errorType)
    {
        if (humanPositionRanges == null) return null;

        foreach (HumanPositionRangeSetting setting in humanPositionRanges)
        {
            if (setting != null && setting.errorType == errorType)
            {
                return setting;
            }
        }

        return null;
    }

    private void LoadCsvData()
    {
        LoadCranePositionCsv();
        LoadPlateSizeCsv();
        LoadHumanPositionCsv();
    }

    private void LoadCranePositionCsv()
    {
        cranePoseCsvRows.Clear();
        cranePoseCsvErrorTypes.Clear();

        if (cranePositionCsv == null) return;

        string[] lines = cranePositionCsv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 4) continue;

            CraneStatusManager.ErrorType errorType = ParseErrorType(cols[0]);

            CranePose pose = new CranePose
            {
                mainCraneLocalZ = ParseFloat(cols[1]),
                mainLifMagLocalX = ParseFloat(cols[2]),
                mainLifMagLocalY = ParseFloat(cols[3])
            };

            cranePoseCsvErrorTypes.Add(errorType);
            cranePoseCsvRows.Add(pose);
        }
    }

    private void LoadPlateSizeCsv()
    {
        plateSizeCsvRows.Clear();
        plateSizeCsvErrorTypes.Clear();

        if (plateSizeCsv == null) return;

        string[] lines = plateSizeCsv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 4) continue;

            CraneStatusManager.ErrorType errorType = ParseErrorType(cols[0]);

            PlateSizeData data = new PlateSizeData
            {
                size = new Vector3(
                    ParseFloat(cols[1]),
                    ParseFloat(cols[2]),
                    ParseFloat(cols[3])
                )
            };

            plateSizeCsvErrorTypes.Add(errorType);
            plateSizeCsvRows.Add(data);
        }
    }

    private void LoadHumanPositionCsv()
    {
        humanPoseCsvRows.Clear();
        humanPoseCsvErrorTypes.Clear();

        if (humanPositionCsv == null) return;

        string[] lines = humanPositionCsv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 5) continue;

            CraneStatusManager.ErrorType errorType = ParseErrorType(cols[0]);

            HumanPose pose = new HumanPose
            {
                position = new Vector3(
                    ParseFloat(cols[1]),
                    ParseFloat(cols[2]),
                    ParseFloat(cols[3])
                ),
                rotationY = ParseFloat(cols[4])
            };

            humanPoseCsvErrorTypes.Add(errorType);
            humanPoseCsvRows.Add(pose);
        }
    }

    private float ParseFloat(string text)
    {
        return float.Parse(text.Trim(), CultureInfo.InvariantCulture);
    }

    private CraneStatusManager.ErrorType ParseErrorType(string text)
    {
        text = text.Trim();

        if (text == "None")
        {
            return CraneStatusManager.ErrorType.None;
        }

        if (text == "ErrorA" || text == "HumanIntrusion" || text == "人の立ち入り")
        {
            return CraneStatusManager.ErrorType.ErrorA;
        }

        if (text == "ErrorB" || text == "LiftFailure" || text == "吊り上げ失敗")
        {
            return CraneStatusManager.ErrorType.ErrorB;
        }

        if (text == "ErrorC" || text == "TrailerLoading" || text == "トレーラへの積込")
        {
            return CraneStatusManager.ErrorType.ErrorC;
        }

        Debug.LogWarning($"未対応の ErrorType です: {text} → None として扱います");
        return CraneStatusManager.ErrorType.None;
    }
}