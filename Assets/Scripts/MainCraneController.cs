using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainCraneController : MonoBehaviour
{
    private readonly float[] zSpeedLevelsMPerMin = {7.5f, 20f, 37.5f, 70f};

    [Header("Speed Settings")]
    [SerializeField] private int currentZSpeedIndex = 0;

    [Header("Z Range")]
    [SerializeField] private float minZ = -10f;
    [SerializeField] private float maxZ = 10f;
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        HandleSpeedSwitch();
        HandleMovement();
    }

    private void HandleSpeedSwitch()
    {
        // CキーでZ方向速度切替
        if (Input.GetKeyDown(KeyCode.C))
        {
            currentZSpeedIndex = (currentZSpeedIndex + 1) % zSpeedLevelsMPerMin.Length;
            Debug.Log($"Z速度: {zSpeedLevelsMPerMin[currentZSpeedIndex]} m/min");
        }
    }
    
    private void HandleMovement()
    {
        float zInput = 0f;

        if (Input.GetKey(KeyCode.I)) zInput += 1f;
        if (Input.GetKey(KeyCode.K)) zInput -= 1f;

        float zSpeedMPerSec = zSpeedLevelsMPerMin[currentZSpeedIndex] / 60f;

        Vector3 localPos = transform.localPosition;
        float nextZ = localPos.z + zInput * zSpeedMPerSec * Time.deltaTime;

        nextZ = Mathf.Clamp(nextZ, minZ, maxZ);

        transform.localPosition = new Vector3(localPos.x, localPos.y, nextZ);
    }
}
