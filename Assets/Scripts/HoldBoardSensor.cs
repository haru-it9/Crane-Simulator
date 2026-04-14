using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HoldBoardSensor : MonoBehaviour
{
    private GameObject ownerBoard;
    private readonly HashSet<GameObject> touchingStopTargets = new HashSet<GameObject>();

    public bool IsTouchingStopTarget => touchingStopTargets.Count > 0;
    public IReadOnlyCollection<GameObject> TouchingStopTargets => touchingStopTargets;

    public void SetOwnerBoard(GameObject board)
    {
        ownerBoard = board;
        touchingStopTargets.Clear();
        //Debug.Log($"[HoldBoardSensor] owner設定: {board.name}");
    }

    public void ClearOwnerBoard()
    {
        //Debug.Log("[HoldBoardSensor] owner解除");
        ownerBoard = null;
        touchingStopTargets.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"[HoldBoardSensor] Enter: name={collision.gameObject.name}, tag={collision.gameObject.tag}");
        CheckStopTarget(collision.gameObject, true);
    }

    private void OnCollisionStay(Collision collision)
    {
        Debug.Log($"[HoldBoardSensor] Stay: name={collision.gameObject.name}, tag={collision.gameObject.tag}");
        CheckStopTarget(collision.gameObject, true);
    }

    private void OnCollisionExit(Collision collision)
    {
        Debug.Log($"[HoldBoardSensor] Exit: name={collision.gameObject.name}, tag={collision.gameObject.tag}");
        CheckStopTarget(collision.gameObject, false);
    }

    private void CheckStopTarget(GameObject other, bool isTouching)
    {
        bool isBoard = other.CompareTag("Board");
        bool isBoardStage = other.CompareTag("BoardStage");

        if (!isBoard && !isBoardStage) return;
        if (other == ownerBoard) return;

        if (isTouching)
        {
            touchingStopTargets.Add(other);
            //Debug.Log($"[HoldBoardSensor] 他板接触追加: {other.name}, count={touchingStopTargets.Count}");
        }
        else
        {
            touchingStopTargets.Remove(other);
            //Debug.Log($"[HoldBoardSensor] 他板接触解除: {other.name}, count={touchingStopTargets.Count}");
        }
    }
}