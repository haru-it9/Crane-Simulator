using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MagnetSensor : MonoBehaviour
{
    public GameObject CurrentBoard { get; private set; }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Board"))
        {
            CurrentBoard = other.gameObject;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Board") && CurrentBoard == other.gameObject)
        {
            CurrentBoard = null;
        }
    }
}
