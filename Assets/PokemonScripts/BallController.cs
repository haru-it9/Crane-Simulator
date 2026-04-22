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
    public float shootSpeed = 10f;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ShootBall();
        }
    }

    private void ShootBall()
    {
        if (ballPrefab == null || shootPoint == null)
        {
            Debug.LogWarning("ballPrefab または shootPoint が設定されていません");
            return;
        }

        // ボール生成
        GameObject ball = Instantiate(ballPrefab, shootPoint.position, shootPoint.rotation);

        // Rigidbody取得
        Rigidbody rb = ball.GetComponent<Rigidbody>();

        if (rb != null)
        {
            // forward方向へ一定速度で発射
            //rb.linearVelocity = shootPoint.forward * shootSpeed;
            // Unityのバージョンによっては ↓ を使う
            rb.velocity = shootPoint.forward * shootSpeed;
        }
        else
        {
            Debug.LogWarning("生成したボールに Rigidbody が付いていません");
        }
    }
}
