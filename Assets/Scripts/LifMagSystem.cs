using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LifMagSystem : MonoBehaviour
{
    public enum LiftJudgementMode
    {
        CumulativeSliderInput,       // 従来：スライダー累積値で判定
        CurrentSliderInputByWeight   // 新規：現在入力値と板重量で判定
    }
    
    [Header("5つのマグネットセンサ")]
    [SerializeField] private MagnetSensor[] magnetSensors;

    [Header("CraneOperationManager")]
    [SerializeField] private CraneOperationManager craneOperationManager;

    [Header("入力")]
    [SerializeField] private KeyCode attachKey = KeyCode.E;
    [SerializeField] private KeyCode detachKey = KeyCode.R;
    [SerializeField] private string joyStick2RedButton = "JoyStick2RedButton";
    [SerializeField] private string joyStick2BlackButton = "JoyStick2BlackButton";
    [SerializeField] private string joyStick2Slider = "JoyStick2Slider";

    [Header("吸着に必要な最小接触数")]
    [SerializeField] private int requiredMagnetCount = 1;

    [Header("つり上げ判定モード")]
    [SerializeField] private LiftJudgementMode liftJudgementMode =
        LiftJudgementMode.CumulativeSliderInput;

    [Header("スライダー累積吸着")]
    [SerializeField] private float sliderSampleInterval = 0.1f;   // 0.1秒ごと
    [SerializeField] private float sliderAttachThreshold = 2.0f;  // この値ごとに1枚吸着
    [SerializeField] private bool useAbsoluteSliderValue = false;  // 絶対値で積算するか

    [Header("現在入力値吸着：つり上げ能力")]
    [SerializeField] private float boardDensity = 7850f; // BoardInfoがない場合の予備

    [SerializeField] private float minLiftCapacityKg = 0f;
    [SerializeField] private float maxLiftCapacityKg = 25000f;

    [Header("入力値モード：電流値表示")]
    [SerializeField] private float maxCurrentAmpere = 100f;

    public float CurrentSliderInput01 { get; private set; }
    public float CurrentElectricCurrentA { get; private set; }
    public float CurrentLiftCapacityKg { get; private set; }
    public float CurrentAttachedWeightKg { get; private set; }

    public bool IsInputValueLiftMode =>
        liftJudgementMode == LiftJudgementMode.CurrentSliderInputByWeight;

    [Header("つり上げ能力不足時の離脱")]
    [SerializeField] private float capacityDetachMarginKg = 0f;

    [Header("確率的つり上げ失敗")]
    [SerializeField] private bool useRandomLiftFailure = false;

    [Range(0f, 1f)]
    [SerializeField] private float liftFailureProbability = 0.05f;

    private bool isAttachAccumulating = false;
    private float sliderAccumulatedValue = 0f;
    private float sliderSampleTimer = 0f;
    private bool lastAttachFailedByRandom = false;

    private float[] lifMagDisplayAccumValues = new float[5];

    [Header("板サイズ連動しきい値")]
    [SerializeField] private bool useBoardSizeThreshold = true;

    // 基準となる板サイズ（例: 0.8m × 0.8m × 0.02m の板ならこんな感じ）
    [SerializeField] private Vector3 referenceBoardSize = new Vector3(4.425f, 0.045f, 1.075f);

    // 倍率の下限・上限
    [SerializeField] private float minThresholdMultiplier = 0.125f;
    [SerializeField] private float maxThresholdMultiplier = 8.0f;

    [Header("接触数連動しきい値")]
    [SerializeField] private bool useMagnetContactThreshold = true;
    [SerializeField] private int referenceMagnetContactCount = 5;   // 基準は5個接触
    [SerializeField] private float maxContactMultiplier = 5.0f;     // 接触数が少ないときの上限

    // 互換用 // 外部スクリプトから参照用
    public bool HasAttachedBoard => attachedBoards.Count > 0;
    public GameObject AttachedBoard => attachedBoards.Count > 0 ? attachedBoards[0] : null;
    // CraneUnit からは「最後に保持した板」のセンサを見る
    public HoldBoardSensor CurrentHoldBoardSensor => attachedHoldSensors.Count > 0 ? attachedHoldSensors[attachedHoldSensors.Count - 1] : null;
    public GameObject LastAttachedBoard => attachedBoards.Count > 0 ? attachedBoards[attachedBoards.Count - 1] : null;

    public IReadOnlyList<GameObject> AttachedBoards => attachedBoards;

    // 内部管理
    private readonly List<GameObject> attachedBoards = new List<GameObject>();
    private readonly List<Rigidbody> attachedRigidbodies = new List<Rigidbody>();
    private readonly List<HoldBoardSensor> attachedHoldSensors = new List<HoldBoardSensor>();

    [Header("吸着間クールタイム")]
    [SerializeField] private float attachCooldown = 0.2f;
    private float lastAttachTime = -999f;

    [Header("リフマグ電流ON/OFF")]
    [SerializeField] private bool[] lifMagCurrentOn = new bool[5];

    [Header("接触数デバッグ")]
    [SerializeField] private bool showMagnetContactDebugLog = true;
    [SerializeField] private float magnetContactDebugInterval = 0.2f;
    private float magnetContactDebugTimer = 0f;

    [Header("Debug OverlapBox Visualization")] // デバッグ用
    [SerializeField] private bool showDebugOverlapBox = true;
    private bool debugHasOverlapBox;
    private Vector3 debugOverlapOrigin;
    private Vector3 debugOverlapHalfExtents;
    private Quaternion debugOverlapRotation = Quaternion.identity;
    private readonly List<Collider> debugOverlapHits = new List<Collider>();
    private GameObject debugSelectedCandidate;

    private void Update()
    {
        if (!SimulatorStartManager.IsOperationEnabled)
        {
            return;
        }

        if (!IsCurrentOperatingCrane())
        {
            return;
        }

        HandleAttachInput();
        HandleDetachInput();
        //DebugCurrentCandidateMagnetDetails();
    }

    private bool IsCurrentOperatingCrane()
    {
        if (craneOperationManager == null) return false;
        if (craneOperationManager.CurrentCrane == null) return false;

        return craneOperationManager.CurrentCrane.LifMagSystem == this;
    }

    private int GetEnabledMagnetCount()
    {
        int count = 0;

        foreach (bool isOn in lifMagCurrentOn)
        {
            if (isOn)
            {
                count++;
            }
        }

        // 0除算防止
        return Mathf.Max(count, 1);
    }

    public bool IsAttachedBoard(GameObject board) // 指定した板が現在吸着中かどうかを返す
    {
        return attachedBoards.Contains(board);
    }

    private bool TryAttachUnified()
    {
        lastAttachFailedByRandom = false;

        if (Time.time - lastAttachTime < attachCooldown)
        {
            return false;
        }

        bool success = false;

        if (!HasAttachedBoard)
        {
            success = TryAttach();
        }
        else
        {
            success = TryAttachAdditionalBoard();
        }

        if (success || lastAttachFailedByRandom)
        {
            lastAttachTime = Time.time;
        }

        return success;
    }

    private bool TryAttach() // 最初の1枚を吸着する
    {
        if (HasAttachedBoard)
        {
            Debug.Log("すでに板を保持中");
            return false;
        }

        GameObject targetBoard = GetBestCandidateBoard(out int count);

        Debug.Log($"吸着候補: {(targetBoard != null ? targetBoard.name : "なし")}, count = {count}");

        if (targetBoard == null)
        {
            Debug.Log("候補板なし");
            return false;
        }

        if (count < requiredMagnetCount)
        {
            Debug.Log($"接触数不足: {count} / 必要数 {requiredMagnetCount}");
            return false;
        }

        if (!PassRandomLiftFailureCheck(targetBoard))
        {
            return false;
        }

        AttachBoardInternal(targetBoard);
        return true;
    }

    private void HandleAttachInput()
    {
        UpdateCurrentInputDisplayValues();
        
        bool currentOn = IsAnyLifMagCurrentOn();

        // 電流ONが1つもなければ判定しない
        if (!currentOn)
        {
            isAttachAccumulating = false;
            sliderAccumulatedValue = 0f;
            sliderSampleTimer = 0f;
            return;
        }

        switch (liftJudgementMode)
        {
            case LiftJudgementMode.CumulativeSliderInput:
                HandleCumulativeSliderAttach();
                break;

            case LiftJudgementMode.CurrentSliderInputByWeight:
                HandleCurrentInputByWeightAttach();
                break;
        }
    }

    private void HandleCumulativeSliderAttach()
    {
        // 電流ON中は常に累積
        isAttachAccumulating = true;

        sliderSampleTimer += Time.deltaTime;

        while (sliderSampleTimer >= sliderSampleInterval)
        {
            sliderSampleTimer -= sliderSampleInterval;

            float sliderValue = Input.GetAxis(joyStick2Slider);
            float sampleValue = useAbsoluteSliderValue ? Mathf.Abs(sliderValue) : sliderValue;

            float addValue;

            // -0.8 ～ -1.0 はゼロ扱い
            if (sampleValue <= -0.8f)
            {
                addValue = 0f;
            }
            else
            {
                addValue = sampleValue + 1f;
            }

            sliderAccumulatedValue += addValue;

            for (int i = 0; i < lifMagDisplayAccumValues.Length; i++)
            {
                if (IsLifMagCurrentOn(i))
                {
                    lifMagDisplayAccumValues[i] += addValue;
                }
            }

            Debug.Log($"[SliderAccum] sample={sampleValue:F3}, total={sliderAccumulatedValue:F3}");

            while (true)
            {
                float currentThreshold = GetCurrentAttachThreshold();

                if (sliderAccumulatedValue < currentThreshold)
                {
                    break;
                }

                bool success = TryAttachUnified();

                if (success)
                {
                    sliderAccumulatedValue -= currentThreshold;
                    Debug.Log($"しきい値到達 -> 吸着成功, consumed={currentThreshold:F3}, remaining={sliderAccumulatedValue:F3}");
                }
                else
                {
                    if (lastAttachFailedByRandom)
                    {
                        sliderAccumulatedValue -= currentThreshold;
                        sliderAccumulatedValue = Mathf.Max(0f, sliderAccumulatedValue);

                        Debug.Log($"しきい値到達 -> 確率判定により吸着失敗, consumed={currentThreshold:F3}, remaining={sliderAccumulatedValue:F3}");
                    }
                    else
                    {
                        Debug.Log("しきい値到達したが吸着失敗");
                    }

                    break;
                }
            }
        }
    }

    private void HandleCurrentInputByWeightAttach()
    {
        float currentInput01 = GetCurrentSliderInput01();
        float liftCapacityKg = GetCurrentLiftCapacityKg(currentInput01);
        float attachedWeightKg = GetAttachedTotalWeightKg();

        // すでに保持している板の重量を支えられなくなったら離脱
        if (HasAttachedBoard && liftCapacityKg + capacityDetachMarginKg < attachedWeightKg)
        {
            Debug.LogWarning(
                $"つり上げ能力不足のため離脱: " +
                $"input={currentInput01:F3}, " +
                $"capacity={liftCapacityKg:F1} kg, " +
                $"attachedWeight={attachedWeightKg:F1} kg"
            );

            DetachAll();

            isAttachAccumulating = false;
            sliderAccumulatedValue = 0f;
            sliderSampleTimer = 0f;

            return;
        }

        GameObject candidate = GetCurrentCandidateBoard();

        if (candidate == null)
        {
            return;
        }

        float candidateWeightKg = GetBoardWeight(candidate);
        float remainingCapacityKg = liftCapacityKg - attachedWeightKg;

        // 余ったつり上げ能力で次の板を持てるか判定
        if (remainingCapacityKg < candidateWeightKg)
        {
            Debug.Log(
                $"現在入力値モード：能力不足のため追加吸着不可, " +
                $"input={currentInput01:F3}, " +
                $"capacity={liftCapacityKg:F1} kg, " +
                $"attached={attachedWeightKg:F1} kg, " +
                $"remaining={remainingCapacityKg:F1} kg, " +
                $"candidate={candidate.name}, " +
                $"candidateWeight={candidateWeightKg:F1} kg"
            );

            return;
        }

        bool success = TryAttachUnified();

        if (success)
        {
            Debug.Log(
                $"現在入力値モード：吸着成功, " +
                $"input={currentInput01:F3}, " +
                $"capacity={liftCapacityKg:F1} kg, " +
                $"attachedBefore={attachedWeightKg:F1} kg, " +
                $"candidate={candidate.name}, " +
                $"candidateWeight={candidateWeightKg:F1} kg"
            );
        }
    }

    private float GetCurrentSliderInput01()
    {
        float sliderValue = Input.GetAxis(joyStick2Slider);

        // 絶対値モードを使う場合
        if (useAbsoluteSliderValue)
        {
            return Mathf.Clamp01(Mathf.Abs(sliderValue));
        }

        // 既存仕様に合わせて、-0.8 ～ -1.0 は入力なし扱い
        if (sliderValue <= -0.8f)
        {
            return 0f;
        }

        // -0.8 を 0、1.0 を 1 として正規化
        return Mathf.InverseLerp(-0.8f, 1.0f, sliderValue);
    }

    private float GetCurrentLiftCapacityKg(float currentInput01)
    {
        return Mathf.Lerp(
            minLiftCapacityKg,
            maxLiftCapacityKg,
            Mathf.Clamp01(currentInput01)
        );
    }

    private void UpdateCurrentInputDisplayValues()
    {
        if (!IsInputValueLiftMode)
        {
            CurrentSliderInput01 = 0f;
            CurrentElectricCurrentA = 0f;
            CurrentLiftCapacityKg = 0f;
            CurrentAttachedWeightKg = GetAttachedTotalWeightKg();
            return;
        }

        // 電流ONが1つもない場合は、電流値0として表示
        if (!IsAnyLifMagCurrentOn())
        {
            CurrentSliderInput01 = 0f;
            CurrentElectricCurrentA = 0f;
            CurrentLiftCapacityKg = 0f;
            CurrentAttachedWeightKg = GetAttachedTotalWeightKg();
            return;
        }

        CurrentSliderInput01 = GetCurrentSliderInput01();
        CurrentElectricCurrentA = CurrentSliderInput01 * maxCurrentAmpere;
        CurrentLiftCapacityKg = GetCurrentLiftCapacityKg(CurrentSliderInput01);
        CurrentAttachedWeightKg = GetAttachedTotalWeightKg();
    }

    private float GetAttachedTotalWeightKg()
    {
        float totalWeightKg = 0f;

        foreach (GameObject board in attachedBoards)
        {
            if (board == null) continue;

            totalWeightKg += GetBoardWeight(board);
        }

        return totalWeightKg;
    }

    private float GetBoardWeight(GameObject board)
    {
        if (board == null) return 0f;

        BoardInfo boardInfo = board.GetComponent<BoardInfo>();

        if (boardInfo != null)
        {
            return boardInfo.Weight;
        }

        Collider col = board.GetComponent<Collider>();

        if (col == null)
        {
            return 0f;
        }

        Bounds b = col.bounds;
        Vector3 size = b.size;

        float volume = size.x * size.y * size.z;
        float weight = volume * boardDensity;

        return weight;
    }

    private bool PassRandomLiftFailureCheck(GameObject board)
    {
        lastAttachFailedByRandom = false;

        if (!useRandomLiftFailure)
        {
            return true;
        }

        if (Random.value < liftFailureProbability)
        {
            lastAttachFailedByRandom = true;

            Debug.LogWarning(
                $"確率判定によりつり上げ失敗: board={board.name}, " +
                $"failureProbability={liftFailureProbability:F3}"
            );

            return false;
        }

        return true;
    }

    public bool GetLifMagCurrent(int index)
    {
        if (index < 0 || index >= lifMagCurrentOn.Length) return false;
        return lifMagCurrentOn[index];
    }

    public float GetLifMagDisplayAccumValue(int index)
    {
        if (lifMagDisplayAccumValues == null) return 0f;
        if (index < 0 || index >= lifMagDisplayAccumValues.Length) return 0f;

        return lifMagDisplayAccumValues[index];
    }

    public void ResetLifMagDisplayAccumValues()
    {
        if (lifMagDisplayAccumValues == null || lifMagDisplayAccumValues.Length != magnetSensors.Length)
        {
            lifMagDisplayAccumValues = new float[magnetSensors.Length];
        }

        for (int i = 0; i < lifMagDisplayAccumValues.Length; i++)
        {
            lifMagDisplayAccumValues[i] = 0f;
        }
    }

    private float GetCurrentAttachThreshold() // 今回の吸着に必要な累積値を、板サイズと接触マグネット数から計算する
    {
        GameObject candidate = GetCurrentCandidateBoard();

        if (candidate == null)
        {
            return sliderAttachThreshold;
        }

        Vector3 size = GetBoardSize(candidate);

        // -----------------------------
        // 1. 体積ベース倍率
        // -----------------------------
        float volumeMultiplier = 1f;

        if (useBoardSizeThreshold)
        {
            float referenceVolume =
                referenceBoardSize.x *
                referenceBoardSize.y *
                referenceBoardSize.z;

            float candidateVolume =
                size.x *
                size.y *
                size.z;

            if (referenceVolume > 0.0001f)
            {
                volumeMultiplier = candidateVolume / referenceVolume;
                volumeMultiplier = Mathf.Clamp(
                    volumeMultiplier,
                    minThresholdMultiplier,
                    maxThresholdMultiplier
                );
            }
        }

        // -----------------------------
        // 2. 接触数ベース倍率
        // 基準: 5個接触なら1.0
        // 少ないほど大きくする
        // -----------------------------
        float contactMultiplier = 1f;
        int enabledCount = GetEnabledMagnetCount();

        if (useMagnetContactThreshold)
        {
            if (enabledCount <= 0)
            {
                contactMultiplier = maxContactMultiplier;
            }
            else
            {
                contactMultiplier =
                    (float)referenceMagnetContactCount / enabledCount;

                contactMultiplier = Mathf.Clamp(
                    contactMultiplier,
                    1f,
                    maxContactMultiplier
                );
            }
        }

        float threshold =
            sliderAttachThreshold *
            volumeMultiplier *
            contactMultiplier;

        Debug.Log(
            $"候補板={candidate.name}, " +
            $"size={size}, " +
            $"enabledCount={enabledCount}, " +
            $"volumeMul={volumeMultiplier:F3}, " +
            $"contactMul={contactMultiplier:F3}, " +
            $"threshold={threshold:F3}"
        );

        return threshold;
    }

    private Vector3 GetBoardSize(GameObject board)
    {
        if (board == null)
        {
            return referenceBoardSize;
        }

        BoardInfo boardInfo = board.GetComponent<BoardInfo>();

        if (boardInfo != null)
        {
            return new Vector3(
                boardInfo.SizeX,
                boardInfo.SizeY,
                boardInfo.SizeZ
            );
        }

        // BoardInfo が付いていない板だけ、従来の bounds を予備的に使う
        Collider col = board.GetComponent<Collider>();

        if (col != null)
        {
            return col.bounds.size;
        }

        return referenceBoardSize;
    }

    private GameObject GetCurrentCandidateBoard() // 現在の吸着候補板を返す（初回吸着か追加吸着かで分岐）
    {
        if (!HasAttachedBoard)
        {
            return GetBestCandidateBoard(out _);
        }
        else
        {
            return FindAdditionalCandidateBoard();
        }
    }

    private int GetTouchingMagnetCount(GameObject targetBoard) // 対象板に触れているマグネットセンサ数を数える
    {
        if (targetBoard == null) return 0;

        int count = 0;

        for (int i = 0; i < magnetSensors.Length; i++)
        {
            MagnetSensor sensor = magnetSensors[i];
            if (sensor == null) continue;

            if (!IsLifMagCurrentOn(i)) continue;

            foreach (GameObject board in sensor.TouchingBoards)
            {
                if (board == targetBoard)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    public void SetLifMagCurrent(int index, bool isOn)
    {
        if (lifMagCurrentOn == null || lifMagCurrentOn.Length != magnetSensors.Length)
        {
            lifMagCurrentOn = new bool[magnetSensors.Length];
        }

        if (lifMagDisplayAccumValues == null || lifMagDisplayAccumValues.Length != magnetSensors.Length)
        {
            lifMagDisplayAccumValues = new float[magnetSensors.Length];
        }

        if (index < 0 || index >= lifMagCurrentOn.Length) return;

        lifMagCurrentOn[index] = isOn;

        // ONになった直後からの累積値にする
        if (isOn)
        {
            lifMagDisplayAccumValues[index] = 0f;
        }

        Debug.Log($"LifMag[{index}] 電流: {(isOn ? "ON" : "OFF")}");
    }

    public bool IsLifMagCurrentOn(int index)
    {
        if (lifMagCurrentOn == null) return false;
        if (index < 0 || index >= lifMagCurrentOn.Length) return false;

        return lifMagCurrentOn[index];
    }

    private bool IsAnyLifMagCurrentOn()
    {
        if (lifMagCurrentOn == null) return false;

        foreach (bool isOn in lifMagCurrentOn)
        {
            if (isOn) return true;
        }

        return false;
    }

    private void DebugCurrentCandidateMagnetDetails()
    {
        if (!showMagnetContactDebugLog) return;

        magnetContactDebugTimer += Time.deltaTime;
        if (magnetContactDebugTimer < magnetContactDebugInterval) return;

        magnetContactDebugTimer = 0f;

        GameObject candidate = GetCurrentCandidateBoard();
        if (candidate == null)
        {
            Debug.Log("[MagnetDebug] 候補板なし");
            return;
        }

        int count = 0;
        List<string> touchingSensorNames = new List<string>();

        for (int i = 0; i < magnetSensors.Length; i++)
        {
            MagnetSensor sensor = magnetSensors[i];
            if (sensor == null) continue;

            foreach (GameObject board in sensor.TouchingBoards)
            {
                if (board == candidate)
                {
                    count++;
                    touchingSensorNames.Add($"Sensor[{i}]");
                    break;
                }
            }
        }

        string sensorList = touchingSensorNames.Count > 0
            ? string.Join(", ", touchingSensorNames)
            : "なし";

        Debug.Log($"[MagnetDebug] 候補板: {candidate.name}, 接触数: {count}, 接触センサ: {sensorList}");
    }

    private void DetachAll() // すべての板を吸着解除する
    {
        if (attachedBoards.Count == 0) return;

        foreach (GameObject board in attachedBoards)
        {
            if (board != null)
            {
                board.transform.SetParent(null, true);
            }
        }

        foreach (Rigidbody rb in attachedRigidbodies)
        {
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        foreach (HoldBoardSensor sensor in attachedHoldSensors)
        {
            if (sensor != null)
            {
                sensor.ClearOwnerBoard();
            }
        }

        attachedBoards.Clear();
        attachedRigidbodies.Clear();
        attachedHoldSensors.Clear();

        Debug.Log("全板を解除しました");
    }

    public void DetachAllFromButton()
    {
        DetachAll();

        // 念のため積算状態もリセット
        isAttachAccumulating = false;
        sliderAccumulatedValue = 0f;
        sliderSampleTimer = 0f;
    }

    private void HandleDetachInput() // 黒ボタン入力を処理し、全板解除する
    {
        if (Input.GetKeyDown(detachKey) || Input.GetButtonDown(joyStick2BlackButton))
        {
            DetachAll();

            // 念のため積算状態もリセット
            isAttachAccumulating = false;
            sliderAccumulatedValue = 0f;
            sliderSampleTimer = 0f;
        }
    }

    private bool TryAttachAdditionalBoard() // すでに保持している板の下にある追加候補板を吸着する
    {
        if (!HasAttachedBoard)
        {
            Debug.Log("まだ板を保持していないため、追加吸着できません");
            return false;
        }

        GameObject candidate = FindAdditionalCandidateBoard();

        if (candidate == null)
        {
            Debug.Log("追加吸着候補なし");
            return false;
        }

        if (!PassRandomLiftFailureCheck(candidate))
        {
            return false;
        }

        AttachBoardInternal(candidate);
        Debug.Log($"追加吸着成功: {candidate.name}");
        return true;
    }

    private GameObject FindAdditionalCandidateBoard() // 最後に吸着した板の下にある追加候補板をOverlapBoxで探す
    {
        GameObject lastBoard = LastAttachedBoard;
        if (lastBoard == null) return null;

        Collider col = lastBoard.GetComponent<Collider>();
        if (col == null) return null;

        Bounds b = col.bounds;

        // ★ここが重要（下側）
        Vector3 origin = new Vector3(
            b.center.x,
            b.min.y + 0.05f,
            b.center.z
        );

        Vector3 halfExtents = new Vector3(
            /*b.extents.x * 0.95f*/0.4f,
            0.05f + 0.001f + 0.001f,
            /*b.extents.z * 0.95f*/0.4f
        );

        Quaternion rotation = lastBoard.transform.rotation;

        // デバッグ情報を保存
        debugHasOverlapBox = true;
        debugOverlapOrigin = origin;
        debugOverlapHalfExtents = halfExtents;
        debugOverlapRotation = rotation;
        debugOverlapHits.Clear();
        debugSelectedCandidate = null;

        Collider[] hits = Physics.OverlapBox(
            origin,
            halfExtents,
            lastBoard.transform.rotation
        );

        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            debugOverlapHits.Add(hit);

            GameObject obj = hit.gameObject;

            if (obj == lastBoard) continue;
            if (!obj.CompareTag("Board")) continue;
            if (attachedBoards.Contains(obj)) continue;

            debugSelectedCandidate = obj;
            Debug.Log($"追加吸着候補(再接触対応): {obj.name}");
            return obj;
        }

        Debug.Log("追加吸着候補なし（再接触）");
        return null;
    }

    private void AttachBoardInternal(GameObject board) // 実際に板を吸着状態にする（親子付け、物理停止、センサ登録）
    {
        if (board == null) return;
        if (attachedBoards.Contains(board)) return;

        Rigidbody rb = board.GetComponent<Rigidbody>();
        HoldBoardSensor sensor = board.GetComponent<HoldBoardSensor>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        board.transform.SetParent(transform, true);

        attachedBoards.Add(board);

        if (rb != null)
        {
            attachedRigidbodies.Add(rb);
        }

        if (sensor != null)
        {
            sensor.SetOwnerBoard(board);
            attachedHoldSensors.Add(sensor);
        }
        else
        {
            Debug.LogWarning($"板 {board.name} に HoldBoardSensor が付いていません");
        }

        Debug.Log($"吸着成功: {board.name}, 保持枚数={attachedBoards.Count}");
    }

    private GameObject GetBestCandidateBoard(out int bestCount) // 初回吸着用に、最も多くのマグネットが触れている板を候補として返す
    {
        Dictionary<GameObject, int> boardCounts = new Dictionary<GameObject, int>();
        GameObject bestBoard = null;
        bestCount = 0;

        for (int i = 0; i < magnetSensors.Length; i++)
        {
            MagnetSensor sensor = magnetSensors[i];
            if (sensor == null) continue;

            if (!IsLifMagCurrentOn(i)) continue;

            foreach (GameObject board in sensor.TouchingBoards)
            {
                if (board == null) continue;
                if (attachedBoards.Contains(board)) continue;

                if (!boardCounts.ContainsKey(board))
                {
                    boardCounts[board] = 0;
                }

                boardCounts[board]++;

                if (boardCounts[board] > bestCount)
                {
                    bestCount = boardCounts[board];
                    bestBoard = board;
                }
            }
        }

        return bestBoard;
    }

    // ================================
    // 介入開始状態の再現用
    // ================================

    public void ForceAttachBoardForIntervention(GameObject board)
    {
        if (board == null) return;

        ForceDetachAllForIntervention();

        Rigidbody rb = board.GetComponent<Rigidbody>();
        HoldBoardSensor sensor = board.GetComponent<HoldBoardSensor>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (!attachedBoards.Contains(board))
        {
            attachedBoards.Add(board);
        }

        if (rb != null && !attachedRigidbodies.Contains(rb))
        {
            attachedRigidbodies.Add(rb);
        }

        if (sensor != null && !attachedHoldSensors.Contains(sensor))
        {
            sensor.SetOwnerBoard(board);
            attachedHoldSensors.Add(sensor);
        }

        if (lifMagCurrentOn == null || lifMagCurrentOn.Length != magnetSensors.Length)
        {
            lifMagCurrentOn = new bool[magnetSensors.Length];
        }

        for (int i = 0; i < lifMagCurrentOn.Length; i++)
        {
            SetLifMagCurrent(i, true);
        }

        isAttachAccumulating = false;
        sliderAccumulatedValue = 0f;
        sliderSampleTimer = 0f;
        lastAttachTime = Time.time;

        Debug.Log($"介入開始用に強制吸着状態へ設定: {board.name}");
    }

    public void ForceDetachAllForIntervention()
    {
        DetachAllFromButton();

        attachedBoards.Clear();
        attachedRigidbodies.Clear();
        attachedHoldSensors.Clear();

        if (lifMagCurrentOn == null || lifMagCurrentOn.Length != magnetSensors.Length)
        {
            lifMagCurrentOn = new bool[magnetSensors.Length];
        }

        for (int i = 0; i < lifMagCurrentOn.Length; i++)
        {
            SetLifMagCurrent(i, false);
        }

        isAttachAccumulating = false;
        sliderAccumulatedValue = 0f;
        sliderSampleTimer = 0f;

        Debug.Log("介入開始用に強制吸着解除状態へ設定");
    }

    private void OnDrawGizmos() // 毎フレームの入力監視、デバッグ描画
    {
        if (!showDebugOverlapBox) return;
        if (!Application.isPlaying) return;
        if (!debugHasOverlapBox) return;

        Matrix4x4 oldMatrix = Gizmos.matrix;

        // OverlapBox本体
        Gizmos.color = Color.green;
        Gizmos.matrix = Matrix4x4.TRS(debugOverlapOrigin, debugOverlapRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, debugOverlapHalfExtents * 2f);

        Gizmos.matrix = Matrix4x4.identity;

        // hitしたColliderを青で表示
        Gizmos.color = Color.blue;
        foreach (Collider hit in debugOverlapHits)
        {
            if (hit == null) continue;
            Gizmos.DrawSphere(hit.bounds.center, 0.015f);
        }

        // 最終候補を赤で表示
        if (debugSelectedCandidate != null)
        {
            Gizmos.color = Color.red;
            Collider selectedCol = debugSelectedCandidate.GetComponent<Collider>();
            if (selectedCol != null)
            {
                Gizmos.DrawSphere(selectedCol.bounds.center, 0.025f);
            }
        }

        Gizmos.matrix = oldMatrix;
    }
}
