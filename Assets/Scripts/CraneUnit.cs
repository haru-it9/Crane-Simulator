using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CraneUnit : MonoBehaviour
{
    [System.Serializable]
    public class LifMagSetting
    {
        public Transform target;
        public float minX;
        public float maxX;
        public bool movable = true;
    }
    
    [Header("References")]
    [SerializeField] private Transform mainCrane;
    [SerializeField] private Transform mainLifMag;

    [Header("LifMag Settings (left to right: lif0, lif1, lif2, lif3, lif4)")]
    [SerializeField] private LifMagSetting[] lifMags = new LifMagSetting[5];

    [Header("Z Speed (MainCrane) [m/min]")]
    [SerializeField] private float[] zSpeeds = { 7.5f, 20f, 37.5f, 70f };
    [SerializeField] private int zSpeedIndex = 0;

    [Header("MainLifMag X Speed [m/min]")]
    [SerializeField] private float[] mainLifMagXSpeeds = { 2.1f, 5.25f, 10.5f, 21f };
    [SerializeField] private int mainLifMagXSpeedIndex = 0;

    [Header("MainLifMag Y Speed [m/min]")]
    [SerializeField] private float[] mainLifMagYSpeeds = { 0.8f, 2f, 4f, 8f };
    [SerializeField] private int mainLifMagYSpeedIndex = 0;

    [Header("LifMag Outer Speed (lif0, lif4) [m/min]")]
    [SerializeField] private float lifOuterSpeeds = 3.54f;
    [SerializeField] private int lifOuterSpeedIndex = 0;

    [Header("LifMag Inner Speed (lif1, lif3) [m/min]")]
    [SerializeField] private float lifInnerSpeeds = 1.785f;
    [SerializeField] private int lifInnerSpeedIndex = 0;

    [Header("MainCrane Z Range")]
    [SerializeField] private float minZ = -10f;
    [SerializeField] private float maxZ = 10f;

    [Header("MainLifMag X Range")]
    [SerializeField] private float minMainX = -0.368f;
    [SerializeField] private float maxMainX = 0.368f;

    [Header("MainLifMag Y Range")]
    [SerializeField] private float minMainY = -5.31f;
    [SerializeField] private float maxMainY = -0.156f;

    [Header("Board Contact Check")]
    [SerializeField] private MagnetSensor[] sensors;
    private HoldBoardSensor holdBoardSensor;
    private GameObject holdingBoard;

    public void MoveMainCraneZ(float input)
    {
        if (mainCrane == null) return;

        float speed = zSpeeds[zSpeedIndex] / 60f;
        Vector3 pos = mainCrane.localPosition;
        pos.z += input * speed * Time.fixedDeltaTime;
        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        mainCrane.localPosition = pos;
    }

    public void MoveMainLifMagX(float input)
    {
        if (mainLifMag == null) return;

        float speed = mainLifMagXSpeeds[mainLifMagXSpeedIndex] / 60f * 0.368f / 6f;
        Vector3 pos = mainLifMag.localPosition;
        pos.x += input * speed * Time.fixedDeltaTime;
        pos.x = Mathf.Clamp(pos.x, minMainX, maxMainX);
        mainLifMag.localPosition = pos;
    }

    public void MoveMainLifMagY(float input)
    {
        if (mainLifMag == null) return;

        // 下方向に動かそうとしていて、接触していたら止める
        if (input < 0f && IsTouchingBoard())
        {
            input = 0f;
        }

        float speed = mainLifMagYSpeeds[mainLifMagYSpeedIndex] / 60f * 5.154f / 2.25f;
        Vector3 pos = mainLifMag.localPosition;
        pos.y += input * speed * Time.fixedDeltaTime;
        pos.y = Mathf.Clamp(pos.y, minMainY, maxMainY);
        mainLifMag.localPosition = pos;
    }

    public void MoveLifMagX(int index, float input)
    {
        if (lifMags == null || index < 0 || index >= lifMags.Length) return;
        if (lifMags[index] == null) return;
        if (lifMags[index].target == null) return;
        if (!lifMags[index].movable) return;

        float speed = GetLifMagSpeed(index) / 60f * 0.4515f / 0.8375f;

        Vector3 pos = lifMags[index].target.localPosition;
        pos.x += input * speed * Time.fixedDeltaTime;
        pos.x = Mathf.Clamp(pos.x, lifMags[index].minX, lifMags[index].maxX);
        lifMags[index].target.localPosition = pos;
    }

    private float GetLifMagSpeed(int index)
    {
        // lif0, lif4
        if (index == 0 || index == 4)
        {
            return lifOuterSpeeds;
        }

        // lif1, lif3
        if (index == 1 || index == 3)
        {
            return lifInnerSpeeds;
        }

        // lif2 は動かない前提
        return 0f;
    }

    public void ChangeZSpeed()
    {
        zSpeedIndex = (zSpeedIndex + 1) % zSpeeds.Length;
        Debug.Log($"{name} MainCrane Z速度: {zSpeeds[zSpeedIndex]} m/min");
    }

    public void ChangeMainLifMagXSpeed()
    {
        mainLifMagXSpeedIndex = (mainLifMagXSpeedIndex + 1) % mainLifMagXSpeeds.Length;
        Debug.Log($"{name} MainLifMag X速度: {mainLifMagXSpeeds[mainLifMagXSpeedIndex]} m/min");
    }

    public void ChangeMainLifMagYSpeed()
    {
        mainLifMagYSpeedIndex = (mainLifMagYSpeedIndex + 1) % mainLifMagYSpeeds.Length;
        Debug.Log($"{name} MainLifMag Y速度: {mainLifMagYSpeeds[mainLifMagYSpeedIndex]} m/min");
    }

    private bool IsTouchingBoard()
    {
        foreach (var s in sensors)
        {
            if (s != null && s.IsTouchingBoard)
            {
                return true;
            }
        }

        if (holdBoardSensor != null && holdBoardSensor.IsTouchingOtherBoard)
        {
        return true;
        }

        return false;
    }
}
