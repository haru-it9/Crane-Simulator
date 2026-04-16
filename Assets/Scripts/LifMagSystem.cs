using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LifMagSystem : MonoBehaviour
{
    [Header("5つのマグネットセンサ")]
    [SerializeField] private MagnetSensor[] magnetSensors;

    [Header("入力")]
    [SerializeField] private KeyCode attachKey = KeyCode.E;
    [SerializeField] private KeyCode detachKey = KeyCode.R;
    [SerializeField] private string joyStick2RedButton = "JoyStick2RedButton";
    [SerializeField] private string joyStick2BlackButton = "JoyStick2BlackButton";

    [Header("吸着に必要な最小接触数")]
    [SerializeField] private int requiredMagnetCount = 1;

    [Header("保持位置")]
    [SerializeField] private Transform holdPoint;

    // 互換用
    public bool HasAttachedBoard => attachedBoards.Count > 0;
    public GameObject AttachedBoard => attachedBoards.Count > 0 ? attachedBoards[0] : null;

    // CraneUnit からは「最後に保持した板」のセンサを見る
    public HoldBoardSensor CurrentHoldBoardSensor => attachedHoldSensors.Count > 0 ? attachedHoldSensors[attachedHoldSensors.Count - 1] : null;
    public GameObject LastAttachedBoard => attachedBoards.Count > 0 ? attachedBoards[attachedBoards.Count - 1] : null;

    // 内部管理
    private readonly List<GameObject> attachedBoards = new List<GameObject>();
    private readonly List<Rigidbody> attachedRigidbodies = new List<Rigidbody>();
    private readonly List<HoldBoardSensor> attachedHoldSensors = new List<HoldBoardSensor>();

    [SerializeField] private float attachCooldown = 0.2f;
    private float lastAttachTime = -999f;


    ///////////////////////////////////////////////////////////////////
    [Header("Debug OverlapBox Visualization")]
    [SerializeField] private bool showDebugOverlapBox = true;

    private bool debugHasOverlapBox;
    private Vector3 debugOverlapOrigin;
    private Vector3 debugOverlapHalfExtents;
    private Quaternion debugOverlapRotation = Quaternion.identity;

    private readonly List<Collider> debugOverlapHits = new List<Collider>();
    private GameObject debugSelectedCandidate;
    ///////////////////////////////////////////////////////////////////

    private void Update()
    {
        if (Input.GetKeyDown(attachKey) || Input.GetButtonDown(joyStick2RedButton))
        {
            TryAttachUnified();
        }

        if (Input.GetKeyDown(detachKey) || Input.GetButtonDown(joyStick2BlackButton))
        {
            DetachAll();
        }
    }

    public bool IsAttachedBoard(GameObject board)
    {
        return attachedBoards.Contains(board);
    }

    private void TryAttachUnified()
    {
        if (Time.time - lastAttachTime < attachCooldown)
        {
            return;
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
    }

    private bool TryAttach()
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

    private void DetachAll()
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

    private bool TryAttachAdditionalBoard()
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

    private GameObject FindAdditionalCandidateBoard()
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

        ///////////////////////////////////////////////////
        Quaternion rotation = lastBoard.transform.rotation;

        // デバッグ情報を保存
        debugHasOverlapBox = true;
        debugOverlapOrigin = origin;
        debugOverlapHalfExtents = halfExtents;
        debugOverlapRotation = rotation;
        debugOverlapHits.Clear();
        debugSelectedCandidate = null;
        //////////////////////////////////////////////////

        Collider[] hits = Physics.OverlapBox(
            origin,
            halfExtents,
            lastBoard.transform.rotation
        );

        foreach (Collider hit in hits)
        {
            if (hit == null) continue;////////
            debugOverlapHits.Add(hit);/////////

            GameObject obj = hit.gameObject;

            if (obj == lastBoard) continue;
            if (!obj.CompareTag("Board")) continue;
            if (attachedBoards.Contains(obj)) continue;

            debugSelectedCandidate = obj;/////////
            Debug.Log($"追加吸着候補(再接触対応): {obj.name}");
            return obj;
        }

        Debug.Log("追加吸着候補なし（再接触）");
        return null;
    }

    private void AttachBoardInternal(GameObject board)
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

    private GameObject GetBestCandidateBoard(out int bestCount)
    {
        Dictionary<GameObject, int> boardCounts = new Dictionary<GameObject, int>();
        GameObject bestBoard = null;
        bestCount = 0;

        foreach (MagnetSensor sensor in magnetSensors)
        {
            if (sensor == null) continue;

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

    /*private void OnDrawGizmos()
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
    }*/
}
