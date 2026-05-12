using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tobii.Gaming;

public class TobiiDebugCheck : MonoBehaviour
{
    void Update()
    {
        var gazePoint = TobiiAPI.GetGazePoint();

        if (gazePoint.IsValid)
        {
            Debug.Log(
                "Gaze = " +
                gazePoint.Screen.x + ", " +
                gazePoint.Screen.y
            );
        }
        else
        {
            Debug.Log("Invalid");
        }
    }
}
