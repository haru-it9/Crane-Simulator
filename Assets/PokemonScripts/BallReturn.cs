using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class BallReturn : MonoBehaviour
{
    [Header("戻る位置（座標）")]
    public Vector3 returnPosition;

    [Header("戻る向き")]
    public Vector3 returnRotationEuler;

    public float returnSpeed = 5f;

    private Rigidbody rb;
    private bool isReturning = false;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Pokemon") && !isReturning)
        {
            StartCoroutine(SmoothReturn());
        }
    }

    private IEnumerator SmoothReturn()
    {
        isReturning = true;

        // 物理停止
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        Vector3 targetPos = returnPosition;
        Quaternion targetRot = Quaternion.Euler(returnRotationEuler) * Quaternion.Euler(0f, 180f, 0f);;

        // スムーズに移動
        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                returnSpeed * Time.deltaTime
            );

            yield return null;
        }

        // 最後にピッタリ合わせる
        transform.position = targetPos;
        transform.rotation = targetRot;

        rb.isKinematic = false;
        isReturning = false;
    }
}
