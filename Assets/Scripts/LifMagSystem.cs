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

    private GameObject attachedBoard;

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

    private void TryAttach()
    {
        if (attachedBoard != null) return;

        GameObject targetBoard = GetBestCandidateBoard(out int count);

        if (targetBoard == null) return;
        if (count < requiredMagnetCount) return;

        attachedBoard = targetBoard;

        Rigidbody rb = attachedBoard.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        attachedBoard.transform.SetParent(transform, true);
        //FollowAttachedBoard();
    }

    private void Detach()
    {
        if (attachedBoard == null) return;

        attachedBoard.transform.SetParent(null, true);

        Rigidbody rb = attachedBoard.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        attachedBoard = null;
    }

    private GameObject GetBestCandidateBoard(out int bestCount)
    {
        Dictionary<GameObject, int> boardCounts = new Dictionary<GameObject, int>();
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
        }

        return bestBoard;
    }

    private void FollowAttachedBoard()
    {
        if (attachedBoard == null) return;

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

        attachedBoard.transform.position = magnetCenter - new Vector3(0f, halfHeight, 0f);
    }
}
