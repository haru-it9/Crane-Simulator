using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class HumanSpawnManager : MonoBehaviour
{
    [Header("人Prefab")]
    public GameObject humanPrefab;

    [Header("出現候補座標")]
    public List<Vector3> spawnPoints;

    [Header("ランダム出現設定")]
    public bool useRandomSpawn = true;
    public float minSpawnInterval = 5f;
    public float maxSpawnInterval = 15f;

    [Header("警告UI")]
    public TextMeshProUGUI warningText;
    public string warningMessage = "警告：作業エリア内に人を検知しました";
    public float warningDisplayTime = 3f;

    private void Start()
    {
        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }

        if (useRandomSpawn)
        {
            StartCoroutine(RandomSpawnLoop());
        }
    }

    private IEnumerator RandomSpawnLoop()
    {
        while (true)
        {
            float wait = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(wait);

            SpawnHumanRandom();
        }
    }

    // ===== ランダム出現 =====
    public void SpawnHumanRandom()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogWarning("spawnPoints が設定されていません");
            return;
        }

        int index = Random.Range(0, spawnPoints.Count);
        SpawnHuman(spawnPoints[index]);
    }

    // ===== 共通出現処理 =====
    public void SpawnHuman(Vector3 position)
    {
        if (humanPrefab == null)
        {
            Debug.LogWarning("humanPrefab が設定されていません");
            return;
        }

        Instantiate(humanPrefab, position, Quaternion.identity);

        ShowWarning();
    }

    // ===== CSV用（index指定） =====
    public void SpawnHumanByIndex(int index)
    {
        if (index < 0 || index >= spawnPoints.Count)
        {
            Debug.LogWarning("spawn index が不正です");
            return;
        }

        SpawnHuman(spawnPoints[index]);
    }

    // ===== CSV用（座標直接） =====
    public void SpawnHumanByPosition(float x, float y, float z)
    {
        SpawnHuman(new Vector3(x, y, z));
    }

    private void ShowWarning()
    {
        if (warningText == null) return;

        warningText.text = warningMessage;
        warningText.gameObject.SetActive(true);

        CancelInvoke(nameof(HideWarning));
        Invoke(nameof(HideWarning), warningDisplayTime);
    }

    private void HideWarning()
    {
        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }
    }
}
