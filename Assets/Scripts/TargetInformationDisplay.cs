using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TargetInformationDisplay : MonoBehaviour
{
    private enum GenerateMode
    {
        CSV,
        Random
    }

    [System.Serializable]
    public class TargetTextSet
    {
        public Text targetXText;
        public Text targetZText;
        public Text targetWeightText;
    }

    [Header("生成モード")]
    [SerializeField] private GenerateMode generateMode = GenerateMode.CSV;

    [Header("ランダム候補")]
    [SerializeField] private List<float> randomTargetXList =
        new List<float>();
    [SerializeField] private List<float> randomTargetZList =
        new List<float>();
    [SerializeField] private List<float> randomTargetWeightList =
        new List<float>();

    [Header("CSVファイル")]
    [SerializeField] private TextAsset csvFile;

    [Header("表示モード別UI Text")]
    [SerializeField] private TargetTextSet multiDisplayTexts =
        new TargetTextSet();
    [SerializeField] private TargetTextSet singleDisplayTexts =
        new TargetTextSet();

    [Header("次の値へ進むキー")]
    [SerializeField] private KeyCode nextKey = KeyCode.Tab;

    [Header("表示フォーマット")]
    [SerializeField] private string weightUnit = " t";

    private class TargetData
    {
        public float targetX;
        public float targetZ;
        public float targetWeight;
    }

    private readonly List<TargetData> targetDataList =
        new List<TargetData>();
    private int currentIndex;

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

        TargetData data = new TargetData
        {
            targetX = randomTargetXList[
                Random.Range(0, randomTargetXList.Count)
            ],
            targetZ = randomTargetZList[
                Random.Range(0, randomTargetZList.Count)
            ],
            targetWeight = randomTargetWeightList[
                Random.Range(0, randomTargetWeightList.Count)
            ]
        };

        ShowTarget(data);
    }

    private void ShowTarget(TargetData data)
    {
        if (data == null) return;

        foreach (TargetTextSet textSet in GetTextSets())
        {
            if (textSet.targetXText != null)
            {
                textSet.targetXText.text = data.targetX.ToString("F2");
            }

            if (textSet.targetZText != null)
            {
                textSet.targetZText.text = data.targetZ.ToString("F2");
            }

            if (textSet.targetWeightText != null)
            {
                textSet.targetWeightText.text =
                    data.targetWeight.ToString("F2") + weightUnit;
            }
        }
    }

    private IEnumerable<TargetTextSet> GetTextSets()
    {
        if (multiDisplayTexts != null)
        {
            yield return multiDisplayTexts;
        }

        if (singleDisplayTexts != null)
        {
            yield return singleDisplayTexts;
        }
    }

    private void LoadCsv()
    {
        targetDataList.Clear();
        currentIndex = 0;

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
                Debug.LogWarning(
                    $"CSV {i + 1}行目の列数が不足しています: {line}"
                );
                continue;
            }

            if (!TryParseFloat(values[0], out float targetX) ||
                !TryParseFloat(values[1], out float targetZ) ||
                !TryParseFloat(values[2], out float targetWeight))
            {
                Debug.LogWarning(
                    $"CSV {i + 1}行目を数値として読み込めません: {line}"
                );
                continue;
            }

            TargetData data = new TargetData
            {
                targetX = targetX,
                targetZ = targetZ,
                targetWeight = targetWeight
            };

            targetDataList.Add(data);
        }

        Debug.Log(
            $"ターゲット情報を {targetDataList.Count} 件読み込みました。"
        );
    }

    private bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result
        );
    }
}
