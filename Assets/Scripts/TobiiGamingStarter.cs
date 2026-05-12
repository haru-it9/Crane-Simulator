using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tobii.Gaming;

public class TobiiGamingStarter : MonoBehaviour
{
    private void Awake()
    {
        TobiiSettings settings = new TobiiSettings();

        bool result = TobiiAPI.Start(settings);

        Debug.Log("TobiiAPI.Start result = " + result);
        Debug.Log("TobiiAPI.IsConnected = " + TobiiAPI.IsConnected);
    }
}
