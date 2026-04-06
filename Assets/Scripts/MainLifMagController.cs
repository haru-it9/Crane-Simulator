using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainLifMagController : MonoBehaviour
{
    private readonly float[] xSpeedLevelsMPerMin = {2.1f, 5.25f, 10.5f, 21f};
    private readonly float[] ySpeedLevelsMPerMin = {0.8f, 2f, 4f, 8f};

    [Header("Speed Settings")]
    [SerializeField] private int currentXSpeedIndex = 0;
    [SerializeField] private int currentYSpeedIndex = 0;

    [Header("X Range")]
    [SerializeField] private float minX = -0.368f;
    [SerializeField] private float maxX = 0.368f; 

    [Header("Y Range")]
    [SerializeField] private float minY = -5.31f;
    [SerializeField] private float maxY = -0.156f;
    
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
        // ZキーでX方向速度切替
        if (Input.GetKeyDown(KeyCode.Z))
        {
            currentXSpeedIndex = (currentXSpeedIndex + 1) % xSpeedLevelsMPerMin.Length;
            Debug.Log($"X速度: {xSpeedLevelsMPerMin[currentXSpeedIndex]} m/min");
        }

        // XキーでY方向速度切替
        if (Input.GetKeyDown(KeyCode.X))
        {
            currentYSpeedIndex = (currentYSpeedIndex + 1) % ySpeedLevelsMPerMin.Length;
            Debug.Log($"Y速度: {ySpeedLevelsMPerMin[currentYSpeedIndex]} m/min");
        }
    }

    private void HandleMovement()
    {
        float xInput = 0f;
        if (Input.GetKey(KeyCode.D)) xInput += 1f;
        if (Input.GetKey(KeyCode.A)) xInput -= 1f;

        float yInput = 0f;
        if (Input.GetKey(KeyCode.W)) yInput += 1f;
        if (Input.GetKey(KeyCode.S)) yInput -= 1f;

        float xSpeedMPerSec = xSpeedLevelsMPerMin[currentXSpeedIndex] / 60f * 0.368f / 6f;
        float ySpeedMPerSec = ySpeedLevelsMPerMin[currentYSpeedIndex] / 60f * 5.154f / 2.25f;

        Vector3 localPos = transform.localPosition;

        float nextX = localPos.x + xInput * xSpeedMPerSec * Time.deltaTime;
        float nextY = localPos.y + yInput * ySpeedMPerSec * Time.deltaTime;

        nextX = Mathf.Clamp(nextX, minX, maxX);
        nextY = Mathf.Clamp(nextY, minY, maxY);

        transform.localPosition = new Vector3(nextX, nextY, localPos.z);
    }
}
