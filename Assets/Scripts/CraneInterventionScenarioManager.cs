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

    private struct TrailerPose
    {
        public Vector3 position;
        public float rotationY;
    }

    private class InterventionScenarioState
    {
        public bool isInitialized;
        public CraneUnit craneUnit;

        public GameObject plate;
        public GameObject human;
        public GameObject trailer;

        public Vector3 attachedPlateTargetSize;
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

    [Header("Human X Offset By Crane Index")]
    [SerializeField] private float[] humanXOffsetsByCraneIndex;

    [Header("Trailer")]
    [SerializeField] private GameObject[] trailerObjectsByCraneIndex;
    [SerializeField] private TextAsset trailerPoseCsv;

    [Header("Trailer X Offset By Crane Index")]
    [SerializeField] private float[] trailerXOffsetsByCraneIndex;

    [Header("Trailer Size By Attached Board")]
    [SerializeField] private bool resizeTrailerByAttachedBoardSize = true;

    [SerializeField] private float trailerSizeMarginX = 0.5f;
    [SerializeField] private float trailerSizeMarginZ = 0.5f;

    [Tooltip("トレーラのY方向サイズは変えず、X/Zのみ変更する")]
    [SerializeField] private bool keepTrailerYSize = true;

    [Header("Board Generator By Crane Index")]
    [SerializeField] private BoardGenerator[] boardGeneratorsByCraneIndex;

    [SerializeField] private bool resetBoardsOnDone = true;

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

    private readonly List<TrailerPose> trailerPoseCsvRows = new List<TrailerPose>();
    private readonly List<CraneStatusManager.ErrorType> trailerPoseCsvErrorTypes =
        new List<CraneStatusManager.ErrorType>();

    private Vector3 currentAttachedPlateTargetSize = Vector3.zero;
    private GameObject currentTrailer;

    private readonly Dictionary<int, InterventionScenarioState> scenarioStates =
        new Dictionary<int, InterventionScenarioState>();

    private int currentScenarioCraneIndex = -1;

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
        SetupInterventionState(craneUnit, phase, errorType, -1);
    }

    public void SetupInterventionState(
        CraneUnit craneUnit,
        CraneStatusManager.WorkPhase phase,
        CraneStatusManager.ErrorType errorType,
        int craneIndex
    )
    {
        if (craneUnit == null)
        {
            Debug.LogWarning("CraneUnit が null です");
            return;
        }

        currentScenarioCraneIndex = craneIndex;
        currentCraneUnit = craneUnit;

        // ================================
        // すでにそのクレーンの介入状態がある場合
        // ================================
        if (scenarioStates.TryGetValue(craneIndex, out InterventionScenarioState existingState) &&
            existingState != null &&
            existingState.isInitialized)
        {
            currentPlate = existingState.plate;
            currentHuman = existingState.human;
            currentTrailer = existingState.trailer;
            currentAttachedPlateTargetSize = existingState.attachedPlateTargetSize;

            Debug.Log(
                $"既存の介入状態に復帰: CraneIndex={craneIndex}, " +
                $"plate={(currentPlate != null ? currentPlate.name : "なし")}, " +
                $"human={(currentHuman != null ? currentHuman.name : "なし")}, " +
                $"trailer={(currentTrailer != null ? currentTrailer.name : "なし")}"
            );

            return;
        }

        // ================================
        // 初回選択時だけ新規生成
        // ================================
        InterventionScenarioState newState = new InterventionScenarioState
        {
            isInitialized = false,
            craneUnit = craneUnit
        };

        scenarioStates[craneIndex] = newState;

        currentPlate = null;
        currentHuman = null;
        currentTrailer = null;
        currentAttachedPlateTargetSize = Vector3.zero;

        CranePose cranePose = GetRandomCranePose(errorType);

        craneUnit.SetInterventionPose(
            cranePose.mainCraneLocalZ,
            cranePose.mainLifMagLocalX,
            cranePose.mainLifMagLocalY
        );

        SetupPlate(craneUnit, phase, errorType);
        SetupHuman(errorType, craneIndex);
        SetupTrailer(errorType, craneIndex);

        newState.isInitialized = true;
        newState.plate = currentPlate;
        newState.human = currentHuman;
        newState.trailer = currentTrailer;
        newState.attachedPlateTargetSize = currentAttachedPlateTargetSize;

        Debug.Log(
            $"介入開始状態を新規生成: CraneIndex={craneIndex}, Phase={phase}, Error={errorType}, " +
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

    public void ClearScenarioByCraneIndex(int craneIndex)
    {
        if (!scenarioStates.TryGetValue(craneIndex, out InterventionScenarioState state))
        {
            Debug.Log($"削除対象の介入状態なし: CraneIndex={craneIndex}");

            if (resetBoardsOnDone)
            {
                BoardGenerator generator = GetBoardGeneratorByCraneIndex(craneIndex);
                if (generator != null)
                {
                    generator.ResetBoards();
                }
            }

            return;
        }

        // 1. 吸着中の板を解除する
        // BoardGenerator由来の板を把持している場合もあるため、先にLifMagSystem側をクリア
        if (state.craneUnit != null)
        {
            state.craneUnit.ClearInterventionBoardAttachment();
        }

        // 2. 介入開始時に生成した強制吸着板を削除する
        if (state.plate != null)
        {
            Destroy(state.plate);
            state.plate = null;
        }

        // 3. 人オブジェクトを削除する
        if (state.human != null)
        {
            Destroy(state.human);
            state.human = null;
        }

        // 4. トレーラは既存オブジェクトなのでDestroyしない
        state.trailer = null;

        // 5. そのクレーンの板置き場を初期状態に戻す
        if (resetBoardsOnDone)
        {
            BoardGenerator generator = GetBoardGeneratorByCraneIndex(craneIndex);

            if (generator != null)
            {
                generator.ResetBoards();
            }
            else
            {
                Debug.LogWarning($"CraneIndex={craneIndex} の BoardGenerator が設定されていません");
            }
        }

        scenarioStates.Remove(craneIndex);

        if (currentScenarioCraneIndex == craneIndex)
        {
            currentScenarioCraneIndex = -1;
            currentPlate = null;
            currentHuman = null;
            currentTrailer = null;
            currentAttachedPlateTargetSize = Vector3.zero;
        }

        Debug.Log($"介入状態を削除し、板配置をリセット: CraneIndex={craneIndex}");
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
            currentAttachedPlateTargetSize = Vector3.zero;
            craneUnit.ClearInterventionBoardAttachment();
            return;
        }

        if (platePrefab == null)
        {
            Debug.LogWarning("Plate Prefab が設定されていません");
            craneUnit.ClearInterventionBoardAttachment();
            return;
        }

        // CSVまたは範囲指定から「目標サイズ」を取得
        Vector3 targetPlateSize = GetRandomPlateSize(errorType);
        currentAttachedPlateTargetSize = targetPlateSize;

        currentPlate = Instantiate(platePrefab);

        // まずリフマグに吸着させる
        craneUnit.SetInterventionBoardAttached(
            currentPlate,
            attachedPlateLocalPosition,
            attachedPlateLocalEuler
        );

        // その後、CSVで指定された実サイズになるように拡大縮小する
        ResizeObjectToWorldSize(currentPlate, targetPlateSize);

        Debug.Log(
            $"厚板サイズ設定: targetSize={targetPlateSize}, " +
            $"actualBounds={GetObjectWorldBoundsSize(currentPlate)}"
        );
    }

    private void ResizeObjectToWorldSize(GameObject obj, Vector3 targetWorldSize)
    {
        if (obj == null) return;

        Vector3 currentWorldSize = GetObjectWorldBoundsSize(obj);

        if (currentWorldSize.x <= 0f ||
            currentWorldSize.y <= 0f ||
            currentWorldSize.z <= 0f)
        {
            Debug.LogWarning(
                $"サイズ調整失敗: 現在サイズが不正です。currentWorldSize={currentWorldSize}"
            );
            return;
        }

        Vector3 currentLocalScale = obj.transform.localScale;

        Vector3 scaleRatio = new Vector3(
            targetWorldSize.x / currentWorldSize.x,
            targetWorldSize.y / currentWorldSize.y,
            targetWorldSize.z / currentWorldSize.z
        );

        obj.transform.localScale = new Vector3(
            currentLocalScale.x * scaleRatio.x,
            currentLocalScale.y * scaleRatio.y,
            currentLocalScale.z * scaleRatio.z
        );
    }

    private Vector3 GetObjectWorldBoundsSize(GameObject obj)
    {
        if (obj == null) return Vector3.zero;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.size;
        }

        Collider[] colliders = obj.GetComponentsInChildren<Collider>();

        if (colliders != null && colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;

            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds.size;
        }

        Debug.LogWarning($"Renderer も Collider も見つかりません: {obj.name}");
        return Vector3.zero;
    }

    private void SetupHuman(
        CraneStatusManager.ErrorType errorType,
        int craneIndex
    )
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

        float xOffset = GetHumanXOffsetByCraneIndex(craneIndex);

        Vector3 spawnPosition = humanPose.position;
        spawnPosition.x += xOffset;

        currentHuman = Instantiate(
            humanPrefab,
            spawnPosition,
            Quaternion.Euler(0f, humanPose.rotationY, 0f)
        );

        Debug.Log(
            $"人オブジェクト生成: CraneIndex={craneIndex}, " +
            $"csvPosition={humanPose.position}, " +
            $"xOffset={xOffset:F2}, " +
            $"spawnPosition={spawnPosition}"
        );
    }

    private void SetupTrailer(
        CraneStatusManager.ErrorType errorType,
        int craneIndex
    )
    {
        // トレーラ配置は ErrorC のときだけ
        if (errorType != CraneStatusManager.ErrorType.ErrorC)
        {
            currentTrailer = null;
            return;
        }

        GameObject trailer = GetTrailerByCraneIndex(craneIndex);

        if (trailer == null)
        {
            Debug.LogWarning($"CraneIndex={craneIndex} のトレーラが設定されていません");
            return;
        }

        if (!TryGetRandomTrailerPoseFromCsv(errorType, out TrailerPose trailerPose))
        {
            Debug.LogWarning($"TrailerPose CSV に {errorType} の候補がありません");
            return;
        }

        float xOffset = GetTrailerXOffsetByCraneIndex(craneIndex);

        Vector3 spawnPosition = trailerPose.position;
        spawnPosition.x += xOffset;

        trailer.SetActive(true);
        trailer.transform.position = spawnPosition;
        trailer.transform.rotation = Quaternion.Euler(0f, trailerPose.rotationY, 0f);

        currentTrailer = trailer;

        if (resizeTrailerByAttachedBoardSize &&
            currentAttachedPlateTargetSize.x > 0f &&
            currentAttachedPlateTargetSize.z > 0f)
        {
            Vector3 targetTrailerSize = new Vector3(
                currentAttachedPlateTargetSize.x + trailerSizeMarginX,
                0f,
                currentAttachedPlateTargetSize.z + trailerSizeMarginZ
            );

            ResizeObjectWorldXZ(
                trailer,
                targetTrailerSize,
                keepTrailerYSize
            );
        }

        Debug.Log(
            $"トレーラ配置: CraneIndex={craneIndex}, " +
            $"csvPosition={trailerPose.position}, " +
            $"xOffset={xOffset:F2}, " +
            $"finalPosition={spawnPosition}, " +
            $"rotY={trailerPose.rotationY:F1}, " +
            $"plateSize={currentAttachedPlateTargetSize}"
        );
    }

    private float GetHumanXOffsetByCraneIndex(int craneIndex)
    {
        if (humanXOffsetsByCraneIndex == null)
        {
            return 0f;
        }

        if (craneIndex < 0 || craneIndex >= humanXOffsetsByCraneIndex.Length)
        {
            return 0f;
        }

        return humanXOffsetsByCraneIndex[craneIndex];
    }

    private GameObject GetTrailerByCraneIndex(int craneIndex)
    {
        if (trailerObjectsByCraneIndex == null) return null;

        if (craneIndex < 0 || craneIndex >= trailerObjectsByCraneIndex.Length)
        {
            return null;
        }

        return trailerObjectsByCraneIndex[craneIndex];
    }

    private float GetTrailerXOffsetByCraneIndex(int craneIndex)
    {
        if (trailerXOffsetsByCraneIndex == null)
        {
            return 0f;
        }

        if (craneIndex < 0 || craneIndex >= trailerXOffsetsByCraneIndex.Length)
        {
            return 0f;
        }

        return trailerXOffsetsByCraneIndex[craneIndex];
    }

    private BoardGenerator GetBoardGeneratorByCraneIndex(int craneIndex)
    {
        if (boardGeneratorsByCraneIndex == null)
        {
            return null;
        }

        if (craneIndex < 0 || craneIndex >= boardGeneratorsByCraneIndex.Length)
        {
            return null;
        }

        return boardGeneratorsByCraneIndex[craneIndex];
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

    private bool TryGetRandomTrailerPoseFromCsv(
        CraneStatusManager.ErrorType errorType,
        out TrailerPose result
    )
    {
        List<TrailerPose> candidates = new List<TrailerPose>();

        for (int i = 0; i < trailerPoseCsvRows.Count; i++)
        {
            if (trailerPoseCsvErrorTypes[i] == errorType)
            {
                candidates.Add(trailerPoseCsvRows[i]);
            }
        }

        // ErrorCの候補がない場合、Noneを共通候補として使えるようにする
        if (candidates.Count == 0)
        {
            for (int i = 0; i < trailerPoseCsvRows.Count; i++)
            {
                if (trailerPoseCsvErrorTypes[i] == CraneStatusManager.ErrorType.None)
                {
                    candidates.Add(trailerPoseCsvRows[i]);
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
        LoadTrailerPoseCsv();
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

    private void LoadTrailerPoseCsv()
    {
        trailerPoseCsvRows.Clear();
        trailerPoseCsvErrorTypes.Clear();

        if (trailerPoseCsv == null) return;

        string[] lines = trailerPoseCsv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 5) continue;

            CraneStatusManager.ErrorType errorType = ParseErrorType(cols[0]);

            TrailerPose pose = new TrailerPose
            {
                position = new Vector3(
                    ParseFloat(cols[1]),
                    ParseFloat(cols[2]),
                    ParseFloat(cols[3])
                ),
                rotationY = ParseFloat(cols[4])
            };

            trailerPoseCsvErrorTypes.Add(errorType);
            trailerPoseCsvRows.Add(pose);
        }

        Debug.Log($"TrailerPose CSV 読み込み完了: {trailerPoseCsvRows.Count} 件");
    }

    private void ResizeObjectWorldXZ(
        GameObject obj,
        Vector3 targetWorldSizeXZ,
        bool keepY
    )
    {
        if (obj == null) return;

        Vector3 currentWorldSize = GetObjectWorldBoundsSize(obj);

        if (currentWorldSize.x <= 0f || currentWorldSize.z <= 0f)
        {
            Debug.LogWarning(
                $"トレーラサイズ変更失敗: 現在サイズが不正です。size={currentWorldSize}"
            );
            return;
        }

        Vector3 currentLocalScale = obj.transform.localScale;

        float scaleRatioX = targetWorldSizeXZ.x / currentWorldSize.x;
        float scaleRatioZ = targetWorldSizeXZ.z / currentWorldSize.z;

        float newScaleY = currentLocalScale.y;

        obj.transform.localScale = new Vector3(
            currentLocalScale.x * scaleRatioX,
            keepY ? newScaleY : currentLocalScale.y,
            currentLocalScale.z * scaleRatioZ
        );

        Debug.Log(
            $"トレーラサイズ変更: targetXZ=({targetWorldSizeXZ.x:F2}, {targetWorldSizeXZ.z:F2}), " +
            $"beforeBounds={currentWorldSize}, " +
            $"afterBounds={GetObjectWorldBoundsSize(obj)}"
        );
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