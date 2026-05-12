using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TargetInformationDisplay : MonoBehaviour
{
    private enum GenerateMode
    {
        CSV,
        Random
    }

    [Header("生成モード")]
    [SerializeField] private GenerateMode generateMode = GenerateMode.CSV;

    [Header("ランダム候補")]
    [SerializeField] private List<float> randomTargetXList = new List<float>();

    [SerializeField] private List<float> randomTargetZList = new List<float>();

    [SerializeField] private List<float> randomTargetWeightList = new List<float>();
    
    [Header("CSVファイル")]
    [SerializeField] private TextAsset csvFile;

    [Header("UI Text")]
    [SerializeField] private Text targetXText;
    [SerializeField] private Text targetZText;
    [SerializeField] private Text targetWeightText;

    [Header("次の値へ進むキー")]
    [SerializeField] private KeyCode nextKey = KeyCode.N;

    [Header("表示フォーマット")]
    [SerializeField] private string weightUnit = " t";

    private class TargetData
    {
        public float targetX;
        public float targetZ;
        public float targetWeight;
    }

    private readonly List<TargetData> targetDataList = new List<TargetData>();
    private int currentIndex = 0;

    private void Start()
    {
        if (generateMode == GenerateMode.CSV)
        {
            LoadCsv();

            if (targetDataList.Count > 0)
            {
                ShowTarget(targetDataList[0]);
            }
        }
        else
        {
            ShowRandomTarget();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(nextKey))
        {
            ShowNextTarget();
        }
    }

    public void ShowNextTarget()
    {
        if (generateMode == GenerateMode.CSV)
        {
            ShowNextCsvTarget();
        }
        else
        {
            ShowRandomTarget();
        }
    }

    private void ShowNextCsvTarget()
    {
        if (targetDataList.Count == 0) return;

        currentIndex++;

        if (currentIndex >= targetDataList.Count)
        {
            currentIndex = 0;
        }

        ShowTarget(targetDataList[currentIndex]);
    }

    private void ShowRandomTarget()
    {
        if (randomTargetXList.Count == 0 ||
            randomTargetZList.Count == 0 ||
            randomTargetWeightList.Count == 0)
        {
            Debug.LogWarning("ランダム候補リストが空です。");
            return;
        }

        TargetData data = new TargetData();

        data.targetX =
            randomTargetXList[Random.Range(0, randomTargetXList.Count)];

        data.targetZ =
            randomTargetZList[Random.Range(0, randomTargetZList.Count)];

        data.targetWeight =
            randomTargetWeightList[Random.Range(0, randomTargetWeightList.Count)];

        ShowTarget(data);
    }

    private void ShowTarget(TargetData data)
    {
        targetXText.text =
            data.targetX.ToString("F2");

        targetZText.text =
            data.targetZ.ToString("F2");

        targetWeightText.text =
            data.targetWeight.ToString("F2") + weightUnit;
    }

    private void LoadCsv()
    {
        targetDataList.Clear();

        if (csvFile == null)
        {
            Debug.LogError("CSVファイルが設定されていません。");
            return;
        }

        string[] lines = csvFile.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrEmpty(line)) continue;

            string[] values = line.Split(',');

            if (values.Length < 3)
            {
                Debug.LogWarning($"CSV {i + 1}行目の列数が不足しています: {line}");
                continue;
            }

            TargetData data = new TargetData();

            data.targetX = float.Parse(values[0]);
            data.targetZ = float.Parse(values[1]);
            data.targetWeight = float.Parse(values[2]);

            targetDataList.Add(data);
        }

        Debug.Log($"ターゲット情報を {targetDataList.Count} 件読み込みました。");
    }
}
