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
    }

    public void ClearOwnerBoard()
    {
        ownerBoard = null;
        touchingOtherBoards.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckBoard(collision.gameObject, true);
    }

    private void OnCollisionStay(Collision collision)
    {
        CheckBoard(collision.gameObject, true);
    }

    private void OnCollisionExit(Collision collision)
    {
        CheckBoard(collision.gameObject, false);
    }

    private void CheckBoard(GameObject other, bool isTouching)
    {
        if (!other.CompareTag("Board")) return;
        if (other == ownerBoard) return;

        if (isTouching)
        {
            touchingOtherBoards.Add(other);
        }
        else
        {
            touchingOtherBoards.Remove(other);
        }
    }
}