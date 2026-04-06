using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CraneController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform mainLifMag;
    [SerializeField] private Transform mainCrane;

    [Header("MainLifMag X Speed [m/min]")]
    [SerializeField] private float[] mainLifMagXSpeeds = { 2.1f, 5.25f, 10.5f, 21f };
    [SerializeField] private int mainLifMagCurrentXSpeedIndex = 0;

    [Header("MainLifMag Y Speed [m/min]")]
    [SerializeField] private float[] mainLifMagYSpeeds = { 0.8f, 2f, 4f, 8f };
    [SerializeField] private int mainLifMagCurrentYSpeedIndex = 0;

    [Header("MainCrane Z Speed [m/min]")]
    [SerializeField] private float[] mainCraneZSpeeds = { 7.5f, 20f, 37.5f, 70f };
    [SerializeField] private int mainCraneCurrentZSpeedIndex = 0;

    [Header("MainLifMag X Range")]
    [SerializeField] private float mainLifMagMinX = -6f;
    [SerializeField] private float mainLifMagMaxX = 6f;

    [Header("MainLifMag Y Range")]
    [SerializeField] private float mainLifMagMinY = -3f;
    [SerializeField] private float mainLifMagMaxY = 3f;

    [Header("MainCrane Z Range")]
    [SerializeField] private float mainCraneMinZ = -10f;
    [SerializeField] private float mainCraneMaxZ = 10f;

    public void MoveX(float input)
    {
        if (mainLifMag == null) return;

        float speed = mainLifMagXSpeeds[mainLifMagCurrentXSpeedIndex] / 60f;
        Vector3 pos = mainLifMag.localPosition;
        pos.x += input * speed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, mainLifMagMinX, mainLifMagMaxX);
        mainLifMag.localPosition = pos;
    }

    public void MoveY(float input)
    {
        if (mainLifMag == null) return;

        float speed = mainLifMagYSpeeds[mainLifMagCurrentYSpeedIndex] / 60f;
        Vector3 pos = mainLifMag.localPosition;
        pos.y += input * speed * Time.deltaTime;
        pos.y = Mathf.Clamp(pos.y, mainLifMagMinY, mainLifMagMaxY);
        mainLifMag.localPosition = pos;
    }

    public void MoveZ(float input)
    {
        if (mainCrane == null) return;

        float speed = mainCraneZSpeeds[mainCraneCurrentZSpeedIndex] / 60f;
        Vector3 pos = mainCrane.localPosition;
        pos.z += input * speed * Time.deltaTime;
        pos.z = Mathf.Clamp(pos.z, mainCraneMinZ, mainCraneMaxZ);
        mainCrane.localPosition = pos;
    }

    public void ChangeXSpeed()
    {
        mainLifMagCurrentXSpeedIndex = (mainLifMagCurrentXSpeedIndex + 1) % mainLifMagXSpeeds.Length;
        Debug.Log($"{name} X速度: {mainLifMagXSpeeds[mainLifMagCurrentXSpeedIndex]} m/min");
    }

    public void ChangeYSpeed()
    {
        mainLifMagCurrentYSpeedIndex = (mainLifMagCurrentYSpeedIndex + 1) % mainLifMagYSpeeds.Length;
        Debug.Log($"{name} Y速度: {mainLifMagYSpeeds[mainLifMagCurrentYSpeedIndex]} m/min");
    }

    public void ChangeZSpeed()
    {
        mainCraneCurrentZSpeedIndex = (mainCraneCurrentZSpeedIndex + 1) % mainCraneZSpeeds.Length;
        Debug.Log($"{name} Z速度: {mainCraneZSpeeds[mainCraneCurrentZSpeedIndex]} m/min");
    }
}
