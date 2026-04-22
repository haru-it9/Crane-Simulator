using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallController : MonoBehaviour
{
    [Header("生成するボール")]
    public GameObject ballPrefab;

    [Header("発射位置")]
    public Transform shootPoint;

    [Header("発射速度")]
    public float shootSpeed = 15f;
    public float yUpwardFactor = 0.1f;

    [Header("角度設定")]
    public float yawSpeed = 10f;   // 左右回転スピード
    public float pitchSpeed = 10f; // 上下回転スピード

    [Header("可視化")]
    public AimVisualizer aimVisualizer;

    private float yaw = 0f;   // 左右角度
    private float pitch = 0f; // 上向き角度（初期値）


    private void Update()
    {
        // ← → で左右回転
        if (Input.GetKey(KeyCode.LeftArrow))
            yaw -= yawSpeed * Time.deltaTime;

        if (Input.GetKey(KeyCode.RightArrow))
            yaw += yawSpeed * Time.deltaTime;

        // ↑ ↓ で上下（仰角）
        if (Input.GetKey(KeyCode.UpArrow))
            pitch += pitchSpeed * Time.deltaTime;

        if (Input.GetKey(KeyCode.DownArrow))
            pitch -= pitchSpeed * Time.deltaTime;

        // 角度制限（上向きすぎ・下向きすぎ防止）
        pitch = Mathf.Clamp(pitch, -30f, 10f);

        // ShootPosition 自体を回す
        if (shootPoint != null)
        {
            shootPoint.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // 可視化更新
        if (aimVisualizer != null)
        {
            aimVisualizer.SetAim(0f, 0f);
        }
        
        // Spaceで発射
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ShootBall();
        }

        if (aimVisualizer != null)
        {
            aimVisualizer.SetAim(yaw, pitch);
        }
    }

    private void ShootBall()
    {
        //Debug.Log("ShootBall called");

        if (ballPrefab == null || shootPoint == null)
        {
            Debug.LogWarning("ballPrefab または shootPoint が設定されていません");
            return;
        }

        Quaternion rotation = shootPoint.rotation;// * Quaternion.Euler(pitch, yaw, 0f);
        Quaternion rotated = rotation * Quaternion.Euler(0f, 180f, 0f);

        GameObject ball = Instantiate(ballPrefab, shootPoint.position, rotated);
        //Debug.Log("Ball instantiated: " + ball.name);

        Rigidbody rb = ball.GetComponent<Rigidbody>();

        if (rb != null)
        {
            Vector3 direction = rotation * Vector3.forward;
            rb.velocity = direction * shootSpeed;
        }
        else
        {
            Debug.LogWarning("生成したボールに Rigidbody が付いていません");
        }     
    }
}
