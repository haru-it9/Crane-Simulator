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

    [Header("吸着補助")]
    [SerializeField] private float attachDistance = 0.08f;
    [SerializeField] private float attractForce = 15f;
    [SerializeField] private float maxAttractDistance = 0.15f;

    [SerializeField] private HoldBoardSensor holdBoardSensor;

    private GameObject attachedBoard;
    private Rigidbody attachedRb;
    private bool isHolding;
    public bool HasAttachedBoard => attachedBoard != null;
    public GameObject AttachedBoard => attachedBoard;

    private void Update()
    {
        if (Input.GetKeyDown(attachKey) || Input.GetButtonDown(joyStick2RedButton))
        {
            TryAttach();
        }

        if (Input.GetKeyDown(detachKey) || Input.GetButtonDown(joyStick2BlackButton))
        {
            Detach();
        }

        /*if (attachedBoard != null)
        {
            FollowAttachedBoard();
        }*/
    }

    private void FixedUpdate()
    {
        if (attachedBoard != null && isHolding)
        {
            FollowAttachedBoard();
        }
    }

    private void TryAttach()
    {
        if (attachedBoard != null) 
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

        attachedBoard = targetBoard;
        attachedRb = attachedBoard.GetComponent<Rigidbody>();

        if (attachedRb != null)
        {
            attachedRb.isKinematic = true;
            attachedRb.useGravity = false;
            attachedRb.velocity = Vector3.zero;
            attachedRb.angularVelocity = Vector3.zero;
        }

        attachedBoard.transform.SetParent(transform, true);

        Debug.Log($"吸着成功: {attachedBoard.name}");

        if (holdBoardSensor != null)
        {
            holdBoardSensor.SetOwnerBoard(attachedBoard);
        }

        /*Collider col = targetBoard.GetComponent<Collider>();

        if (rb == null || col == null || holdPoint == null) return;*/

        /*if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }*/

        /*Vector3 closest = col.ClosestPoint(holdPoint.position);
        float distance = Vector3.Distance(holdPoint.position, closest);

        // かなり近いならそのまま吸着成立
        if (distance <= attachDistance)
        {
            AttachBoard(targetBoard, rb);
            return;
        }

        // 少し離れているが近距離なら軽く引き寄せる
        if (distance <= maxAttractDistance)
        {
            Vector3 dir = (holdPoint.position - rb.worldCenterOfMass).normalized;
            rb.AddForce(dir * attractForce, ForceMode.Acceleration);

            // 1回の入力で即吸着にしたいならここはreturnでOK
            // 押し続け吸着方式にしたいなら、Input.GetKey系と組み合わせて毎FixedUpdateで引力をかける
        }*/

        //attachedBoard.transform.SetParent(transform, true);
        //FollowAttachedBoard();
    }

    private void AttachBoard(GameObject board, Rigidbody rb)
    {
        attachedBoard = board;
        attachedRb = rb;
        isHolding = true;

        attachedRb.velocity = Vector3.zero;
        attachedRb.angularVelocity = Vector3.zero;
        attachedRb.useGravity = false;
        attachedRb.isKinematic = true;
    }

    private void Detach()
    {
        if (attachedBoard == null) return;

        attachedBoard.transform.SetParent(null, true);

        if (attachedRb != null)
        {
            attachedRb.isKinematic = false;
            attachedRb.useGravity = true;
            attachedRb.velocity = Vector3.zero;
            attachedRb.angularVelocity = Vector3.zero;
        }

        attachedBoard = null;
        attachedRb = null;
        isHolding = false;

        if (holdBoardSensor != null)
        {
            holdBoardSensor.ClearOwnerBoard();
        }
        
        /*Rigidbody rb = attachedBoard.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        attachedBoard = null;*/
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
        /*Dictionary<GameObject, int> boardCounts = new Dictionary<GameObject, int>();
        GameObject bestBoard = null;
        bestCount = 0;

        foreach (MagnetSensor sensor in magnetSensors)
        {
            if (sensor == null) continue;

            GameObject board = sensor.CurrentBoard;
            if (board == null) continue;

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
        }*/

        return bestBoard;
    }

    private void FollowAttachedBoard()
    {
        if (attachedBoard == null || attachedRb == null || holdPoint == null) return;

        attachedRb.MovePosition(holdPoint.position);
        attachedRb.MoveRotation(holdPoint.rotation);
        
        /*if (attachedBoard == null) return;

        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (MagnetSensor sensor in magnetSensors)
        {
            if (sensor == null) continue;

            if (sensor.CurrentBoard == attachedBoard)
            {
                sum += sensor.transform.position;
                count++;
            }
        }

        if (count == 0) return;

        Vector3 magnetCenter = sum / count;

        Collider boardCol = attachedBoard.GetComponent<Collider>();
        float halfHeight = 0f;

        if (boardCol != null)
        {
            halfHeight = boardCol.bounds.size.y / 2f;
        }

        attachedBoard.transform.position = magnetCenter - new Vector3(0f, halfHeight, 0f);*/
    }
}
