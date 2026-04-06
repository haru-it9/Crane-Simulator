using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    [Header("生成する板Prefab")]
    [SerializeField] private GameObject boardPrefab;

    [Header("生成位置の中心")]
    [SerializeField] private Vector3 spawnCenter = Vector3.zero;

    [Header("生成位置の範囲")]
    [SerializeField] private Vector3 spawnRange = new Vector3(5f, 0f, 5f);

    [Header("板の寸法範囲")]
    [SerializeField] private Vector2 widthRange = new Vector2(1.5f, 7.35f);   // X方向
    [SerializeField] private Vector2 heightRange = new Vector2(0.00225f, 0.0675f);  // Y方向（厚み）
    [SerializeField] private Vector2 depthRange = new Vector2(0.5f, 1.65f);   // Z方向

    [Header("自動生成設定")]
    [SerializeField] private bool autoSpawn = false;
    [SerializeField] private float spawnInterval = 2.0f;

    private float timer = 0f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnBoard();
        }
        
        if (!autoSpawn) return;

        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            SpawnBoard();
            timer = 0f;
        }
    }

    public void SpawnBoard()
    {
        if (boardPrefab == null)
        {
            Debug.LogWarning("boardPrefab が設定されていません。");
            return;
        }

        // 生成位置を範囲内でランダム決定
        float randomX = Random.Range(-spawnRange.x / 2f, spawnRange.x / 2f);
        float randomY = Random.Range(-spawnRange.y / 2f, spawnRange.y / 2f);
        float randomZ = Random.Range(-spawnRange.z / 2f, spawnRange.z / 2f);

        Vector3 spawnPos = spawnCenter + new Vector3(randomX, randomY, randomZ);

        // 寸法をランダム決定
        float width = Random.Range(widthRange.x, widthRange.y);
        float height = Random.Range(heightRange.x, heightRange.y);
        float depth = Random.Range(depthRange.x, depthRange.y);

        GameObject board = Instantiate(boardPrefab, spawnPos, Quaternion.identity);

        // localScaleで寸法変更
        board.transform.localScale = new Vector3(width, height, depth);
    }

    // Sceneビューで生成範囲を見えるようにする
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(spawnCenter, spawnRange);
    }
}
