using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HoldBoardSensor : MonoBehaviour
{
    private GameObject ownerBoard;
    private readonly HashSet<GameObject> touchingOtherBoards = new HashSet<GameObject>();

    public bool IsTouchingOtherBoard => touchingOtherBoards.Count > 0;

    public void SetOwnerBoard(GameObject board)
    {
        ownerBoard = board;
        touchingOtherBoards.Clear();
        Debug.Log($"[HoldBoardSensor] owner設定: {board.name}");
    }

    public void ClearOwnerBoard()
    {
        Debug.Log("[HoldBoardSensor] owner解除");
        ownerBoard = null;
        touchingOtherBoards.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"[HoldBoardSensor] Enter: {gameObject.name} <-> {collision.gameObject.name}");
        CheckBoard(collision.gameObject, true);
    }

    private void OnCollisionStay(Collision collision)
    {
        CheckBoard(collision.gameObject, true);
    }

    private void OnCollisionExit(Collision collision)
    {
        Debug.Log($"[HoldBoardSensor] Exit: {gameObject.name} <-> {collision.gameObject.name}");
        CheckBoard(collision.gameObject, false);
    }

    private void CheckBoard(GameObject other, bool isTouching)
    {
        if (!other.CompareTag("Board")) return;
        if (other == ownerBoard) return;

        if (isTouching)
        {
            touchingOtherBoards.Add(other);
            Debug.Log($"[HoldBoardSensor] 他板接触追加: {other.name}, count={touchingOtherBoards.Count}");
        }
        else
        {
            touchingOtherBoards.Remove(other);
            Debug.Log($"[HoldBoardSensor] 他板接触解除: {other.name}, count={touchingOtherBoards.Count}");
        }
    }
}