using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LifMagSystem : MonoBehaviour
{
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

    [Header("スライダー累積吸着")]
    [SerializeField] private bool useSliderAccumAttach = true;
    [SerializeField] private float sliderSampleInterval = 0.1f;   // 0.1秒ごと
    [SerializeField] private float sliderAttachThreshold = 2.0f;  // この値ごとに1枚吸着
    [SerializeField] private bool useAbsoluteSliderValue = false;  // 絶対値で積算するか

    private bool isAttachAccumulating = false;
    private float sliderAccumulatedValue = 0f;
    private float sliderSampleTimer = 0f;

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

    private bool TryAttachUnified() // 初回吸着か追加吸着かを判断して、吸着処理を一本化する
    {
        if (Time.time - lastAttachTime < attachCooldown)
        {
            return false;
        }

        bool success = false;

        if (!HasAttachedBoard)
        {
            success = TryAttach(); // ← bool返すように変更
        }
        else
        {
            success = TryAttachAdditionalBoard(); // ← bool返すように変更
        }

        if (success)
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

        AttachBoardInternal(targetBoard);
        return true;
    }

    private void HandleAttachInput() // 赤ボタン入力を処理し、累積値に応じて吸着を試みる
    {
        if (!useSliderAccumAttach)
        {
            if (IsAnyLifMagCurrentOn())
            {
                TryAttachUnified();
            }
            return;
        }

        bool currentOn = IsAnyLifMagCurrentOn();

        // 電流ONが1つもなければ累積しない
        if (!currentOn)
        {
            isAttachAccumulating = false;
            sliderAccumulatedValue = 0f;
            sliderSampleTimer = 0f;
            return;
        }

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
                    Debug.Log("しきい値到達したが吸着失敗");
                    break;
                }
            }
        }
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

    private float GetCurrentAttachThreshold() // 今回の吸着に必要な累積値を、板体積と接触マグネット数から計算する
    {
        GameObject candidate = GetCurrentCandidateBoard();
        if (candidate == null)
        {
            return sliderAttachThreshold;
        }

        Collider col = candidate.GetComponent<Collider>();
        if (col == null)
        {
            return sliderAttachThreshold;
        }

        Bounds b = col.bounds;
        Vector3 size = b.size;

        // -----------------------------
        // 1. 体積ベース倍率
        // -----------------------------
        float volumeMultiplier = 1f;

        if (useBoardSizeThreshold)
        {
            float referenceVolume = referenceBoardSize.x * referenceBoardSize.y * referenceBoardSize.z;
            float candidateVolume = size.x * size.y * size.z;

            if (referenceVolume > 0.0001f)
            {
                volumeMultiplier = candidateVolume / referenceVolume;
                volumeMultiplier = Mathf.Clamp(volumeMultiplier, minThresholdMultiplier, maxThresholdMultiplier);
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
                contactMultiplier = (float)referenceMagnetContactCount / enabledCount;
                contactMultiplier = Mathf.Clamp(contactMultiplier, 1f, maxContactMultiplier);
            }
        }

        float threshold = sliderAttachThreshold * volumeMultiplier * contactMultiplier;

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
