using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HumanSpawnManager : MonoBehaviour
{
    [Header("人Prefab")]
    public GameObject humanPrefab;

    [Header("出現候補座標")]
    public List<float> spawnXList = new List<float> { -9f, 0f, 9f };
    public List<float> spawnYList = new List<float> { 6f };
    public List<float> spawnZList = new List<float>
    {
        -21f, -18f, -15f, -12f, -9f, -6f, -3f,
         0f,
         3f, 6f, 9f, 12f, 15f, 18f, 21f
    };

    [Header("ランダム出現設定")]
    public bool useRandomSpawn = true;
    public float minSpawnInterval = 5f;
    public float maxSpawnInterval = 15f;

    [Header("人を消すキー")]
    public KeyCode removeHumanKey = KeyCode.R;

    [Header("警告UI")]
    public Text warningText;
    public string warningMessage = "警告：作業エリア内に人を検知しました\nRキーで安全確認完了";
    
    private GameObject currentHuman;

    [SerializeField] private string joyStick3RedButton = "JoyStick3RedButton";

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

    private void Update()
    {
        /*if (currentHuman != null && Input.GetKeyDown(removeHumanKey))
        {
            RemoveCurrentHuman();
        }*/

        if (currentHuman != null && Input.GetButtonDown(joyStick3RedButton))
        {
            RemoveCurrentHuman();
            Debug.Log("人を消すキーが押されました：安全確認完了、人を消去");
        }
    }

    private IEnumerator RandomSpawnLoop()
    {
        while (true)
        {
            float wait = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(wait);

            // 人が残っている間は次を出さない
            if (currentHuman == null)
            {
                SpawnHumanRandom();
            }
        }
    }

    public void SpawnHumanRandom()
    {
        Vector3 spawnPosition = GetRandomSpawnPosition();
        SpawnHuman(spawnPosition);
    }

    private Vector3 GetRandomSpawnPosition()
    {
        if (spawnXList == null || spawnXList.Count == 0)
        {
            Debug.LogWarning("spawnXList が設定されていません");
            return Vector3.zero;
        }

        if (spawnYList == null || spawnYList.Count == 0)
        {
            Debug.LogWarning("spawnYList が設定されていません");
            return Vector3.zero;
        }

        if (spawnZList == null || spawnZList.Count == 0)
        {
            Debug.LogWarning("spawnZList が設定されていません");
            return Vector3.zero;
        }

        float x = spawnXList[Random.Range(0, spawnXList.Count)];
        float y = spawnYList[Random.Range(0, spawnYList.Count)];
        float z = spawnZList[Random.Range(0, spawnZList.Count)];

        return new Vector3(x, y, z);
    }

    public void SpawnHuman(Vector3 position)
    {
        if (humanPrefab == null)
        {
            Debug.LogWarning("humanPrefab が設定されていません");
            return;
        }

        // すでに人がいる場合は出さない
        if (currentHuman != null)
        {
            Debug.Log("人が出現中のため、新たな人は出現しません");
            return;
        }

        currentHuman = Instantiate(humanPrefab, position, Quaternion.identity);

        ShowWarning();
    }

    public void SpawnHumanByPosition(float x, float y, float z)
    {
        SpawnHuman(new Vector3(x, y, z));
    }

    private void RemoveCurrentHuman()
    {
        if (currentHuman == null) return;

        Destroy(currentHuman);
        currentHuman = null;

        HideWarning();
    }

    private void ShowWarning()
    {
        if (warningText == null) return;

        warningText.text = warningMessage;
        warningText.gameObject.SetActive(true);
    }

    private void HideWarning()
    {
        if (warningText != null)
        {
            warningText.gameObject.SetActive(false);
        }
    }
}
