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
            //Debug.Log("Boardに触れた（Enter）: " + other.name);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Board"))
        {
            //Debug.Log("Boardに触れている（Stay）: " + other.name);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Board") && CurrentBoard == other.gameObject)
        {
            CurrentBoard = null;
            //Debug.Log("Boardから離れた（Exit）: " + other.name);
        }
    }
}
