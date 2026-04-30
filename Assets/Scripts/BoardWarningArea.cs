using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardWarningArea : MonoBehaviour
{
    [SerializeField] private CraneUnit craneUnit;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Board")) return;

        Debug.LogWarning($"警告：{other.name} がトラクター上の警告領域に侵入");

        if (craneUnit != null)
        {
            craneUnit.LockDescentByWarningArea();
        }
    }
}
