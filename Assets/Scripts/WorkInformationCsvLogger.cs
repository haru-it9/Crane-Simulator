using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;
using System.Text;

public class WorkInformationCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    [SerializeField] private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("作業情報表示スクリプト")]
    [SerializeField] private CraneInformationDisplay craneInformationDisplay;

    [Header("リフマグ累積値表示スクリプト")]
    [SerializeField] private LifMagAccumValueText[] lifMagAccumValueTexts;

    [Header("記録間隔")]
    [SerializeField]
    [Min(0.001f)]
    private float logInterval = 0.02f;

    [SerializeField]
    [Min(1)]
    private int flushEveryLines = 100;

    private StreamWriter writer;
    private bool isLogging = false;
    private float startTime;
    private string filePath;
    private float timer;
    private int linesSinceFlush;

    public void StartLogging(string inputFileName)
    {
        StopLogging();

        string folderPath = saveFolderPath;

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        if (string.IsNullOrWhiteSpace(inputFileName))
        {
            inputFileName = "WorkInformationLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        string fileName = inputFileName + "_WorkInformation.csv";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(
            filePath,
            false,
            new UTF8Encoding(true)
        );

        writer.Write("Time,X,Z,DisplayWeight_t");

        if (lifMagAccumValueTexts != null)
        {
            for (int i = 0; i < lifMagAccumValueTexts.Length; i++)
            {
                writer.Write($",LifMag{i}Accum");
            }
        }

        writer.WriteLine();

        startTime = Time.time;
        timer = 0f;
        linesSinceFlush = 0;
        isLogging = true;

        Debug.Log("作業情報CSV記録開始: " + filePath);
    }

    private void Update()
    {
        if (!isLogging || writer == null) return;
        if (ExperimentPauseManager.IsPaused) return;

        timer += Time.deltaTime;
        if (timer < logInterval) return;
        timer -= logInterval;

        float elapsedTime = Time.time - startTime;

        float x = 0f;
        float z = 0f;
        float weight = 0f;

        if (craneInformationDisplay != null)
        {
            x = craneInformationDisplay.CurrentX;
            z = craneInformationDisplay.CurrentZ;
            weight = craneInformationDisplay.CurrentDisplayWeightTon;
        }

        writer.Write(
            elapsedTime.ToString("F3", CultureInfo.InvariantCulture) + "," +
            x.ToString("F2", CultureInfo.InvariantCulture) + "," +
            z.ToString("F2", CultureInfo.InvariantCulture) + "," +
            weight.ToString("F2", CultureInfo.InvariantCulture)
        );

        if (lifMagAccumValueTexts != null)
        {
            for (int i = 0; i < lifMagAccumValueTexts.Length; i++)
            {
                float value = 0f;

                if (lifMagAccumValueTexts[i] != null)
                {
                    value = lifMagAccumValueTexts[i].CurrentValue;
                }

                writer.Write(
                    "," + value.ToString(
                        "F2",
                        CultureInfo.InvariantCulture
                    )
                );
            }
        }

        writer.WriteLine();

        linesSinceFlush++;
        if (linesSinceFlush >= flushEveryLines)
        {
            writer.Flush();
            linesSinceFlush = 0;
        }
    }

    public void StopLogging()
    {
        isLogging = false;
        CloseWriter();
    }

    private void OnApplicationQuit()
    {
        CloseWriter();
    }

    private void OnDestroy()
    {
        CloseWriter();
    }

    private void CloseWriter()
    {
        if (writer == null) return;

        writer.Flush();
        writer.Close();
        writer = null;
    }

    private void OnValidate()
    {
        logInterval = Mathf.Max(0.001f, logInterval);
        flushEveryLines = Mathf.Max(1, flushEveryLines);
    }
}
