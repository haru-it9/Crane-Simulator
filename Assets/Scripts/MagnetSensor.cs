using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MagnetSensor : MonoBehaviour
{
    private readonly HashSet<GameObject> touchingBoards = new HashSet<GameObject>();

    public bool IsTouchingBoard => touchingBoards.Count > 0;

    public IReadOnlyCollection<GameObject> TouchingBoards => touchingBoards;

    
    //public GameObject CurrentBoard { get; private set; }
    //public bool IsTouchingBoard { get; private set; }


    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"TriggerEnter: {other.name}, tag={other.tag}");
        if (other.CompareTag("Board"))
        {
            touchingBoards.Add(other.gameObject);
            //CurrentBoard = other.gameObject;
            //IsTouchingBoard = true;
            Debug.Log("Boardに触れた（Enter）: " + other.name);
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
        if (other.CompareTag("Board")/* && CurrentBoard == other.gameObject*/)
        {
            touchingBoards.Remove(other.gameObject);
            //CurrentBoard = null;
            //IsTouchingBoard = false;
            Debug.Log("Boardから離れた（Exit）: " + other.name);
        }
    }

    private void OnDisable()
    {
        touchingBoards.Clear();
    }
}
