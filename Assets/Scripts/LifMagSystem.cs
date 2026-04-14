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
    [SerializeField] private KeyCode attachAdditionalKey = KeyCode.T;
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

    private void Update()
    {
        if (Input.GetKeyDown(attachKey) || Input.GetButtonDown(joyStick2RedButton))
        {
            TryAttach();
        }

        if (Input.GetKeyDown(detachKey) || Input.GetButtonDown(joyStick2BlackButton))
        {
            DetachAll();
        }

        if (Input.GetKeyDown(attachAdditionalKey) || Input.GetButtonDown(joyStick2RedButton))
        {
            TryAttachAdditionalBoard();
        }
    }

    public bool IsAttachedBoard(GameObject board)
    {
        return attachedBoards.Contains(board);
    }

    private void TryAttach()
    {
        if (HasAttachedBoard)
        {
            Debug.Log("すでに板を保持中");
            return;
        }

        GameObject targetBoard = GetBestCandidateBoard(out int count);

        Debug.Log($"吸着候補: {(targetBoard != null ? targetBoard.name : "なし")}, count = {count}");

        if (targetBoard == null)
        {
            Debug.Log("候補板なし");
            return;
        }

        if (count < requiredMagnetCount)
        {
            Debug.Log($"接触数不足: {count} / 必要数 {requiredMagnetCount}");
            return;
        }

        AttachBoardInternal(targetBoard);
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

    private void TryAttachAdditionalBoard()
    {
        if (!HasAttachedBoard)
        {
            Debug.Log("まだ板を保持していないため、追加吸着できません");
            return;
        }

        GameObject candidate = FindAdditionalCandidateBoard();

        if (candidate == null)
        {
            Debug.Log("追加吸着候補なし");
            return;
        }

        AttachBoardInternal(candidate);
        Debug.Log($"追加吸着成功: {candidate.name}");
    }

    private GameObject FindAdditionalCandidateBoard()
    {
        HoldBoardSensor lastSensor = CurrentHoldBoardSensor;
        if (lastSensor == null)
        {
            Debug.Log("lastSensor が null");
            return null;
        }

        Debug.Log($"最後のセンサ: {lastSensor.gameObject.name}");

        foreach (GameObject board in lastSensor.TouchingStopTargets)
        {
            if (board == null) continue;

            Debug.Log($"候補チェック: {board.name}");

            if (!board.CompareTag("Board"))
            {
                Debug.Log($"除外: {board.name} は Board ではない");
                continue;
            }

            if (attachedBoards.Contains(board))
            {
                Debug.Log($"除外: {board.name} はすでに保持中");
                continue;
            }

            Debug.Log($"追加吸着候補決定: {board.name}");
            return board;
        }

        Debug.Log("追加吸着候補なし");
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
}
