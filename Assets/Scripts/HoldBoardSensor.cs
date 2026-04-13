using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HoldBoardSensor : MonoBehaviour
{
    public GameObject OwnerBoard { get; private set; }
    public bool IsTouchingOtherBoard { get; private set; }

    public void Initialize(GameObject ownerBoard)
    {
        OwnerBoard = ownerBoard;
        IsTouchingOtherBoard = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Board")) return;

        GameObject otherBoard = GetBoardRoot(other.gameObject);
        if (otherBoard == null) return;

        if (otherBoard != OwnerBoard)
        {
            IsTouchingOtherBoard = true;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Board")) return;

        GameObject otherBoard = GetBoardRoot(other.gameObject);
        if (otherBoard == null) return;

        if (otherBoard != OwnerBoard)
        {
            IsTouchingOtherBoard = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Board")) return;

        GameObject otherBoard = GetBoardRoot(other.gameObject);
        if (otherBoard == null) return;

        if (otherBoard != OwnerBoard)
        {
            IsTouchingOtherBoard = false;
        }
    }

    private GameObject GetBoardRoot(GameObject obj)
    {
        Transform t = obj.transform;
        while (t != null)
        {
            if (t.CompareTag("Board"))
                return t.gameObject;
            t = t.parent;
        }
        return null;
    }
}
