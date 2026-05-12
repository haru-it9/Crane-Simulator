using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class WorkInformationCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    [SerializeField] private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("作業情報表示スクリプト")]
    [SerializeField] private CraneInformationDisplay craneInformationDisplay;

    [Header("リフマグ累積値表示スクリプト")]
    [SerializeField] private LifMagAccumValueText[] lifMagAccumValueTexts;

    private StreamWriter writer;
    private bool isLogging = false;
    private float startTime;
    private string filePath;

    public void StartLogging(string inputFileName)
    {
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

        writer = new StreamWriter(filePath, false);

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
        isLogging = true;

        Debug.Log("作業情報CSV記録開始: " + filePath);
    }

    private void Update()
    {
        if (!isLogging || writer == null) return;

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

        writer.Write($"{elapsedTime:F3},{x:F2},{z:F2},{weight:F2}");

        if (lifMagAccumValueTexts != null)
        {
            for (int i = 0; i < lifMagAccumValueTexts.Length; i++)
            {
                float value = 0f;

                if (lifMagAccumValueTexts[i] != null)
                {
                    value = lifMagAccumValueTexts[i].CurrentValue;
                }

                writer.Write($",{value:F2}");
            }
        }

        writer.WriteLine();
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
}
