using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class UIButtonCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    [SerializeField] private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    private StreamWriter writer;
    private bool isLogging = false;
    private string filePath;

    private float startTime;

    public void StartLogging(string inputFileName)
    {
        string folderPath = saveFolderPath;

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        if (string.IsNullOrWhiteSpace(inputFileName))
        {
            inputFileName = "UIButtonLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        string fileName = inputFileName + "_UIButton.csv";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(filePath, false);
        writer.WriteLine("Time,ButtonName");

        // StartScreen消失後に呼ぶことで、ここが0秒基準になる
        startTime = Time.time;
        isLogging = true;

        Debug.Log("UIボタンCSV記録開始: " + filePath);
    }

    public void RecordButtonClick(string buttonName)
    {
        if (!isLogging || writer == null) return;

        float elapsedTime = Time.time - startTime;

        writer.WriteLine($"{elapsedTime:F3},{buttonName}");
        writer.Flush();

        Debug.Log($"UIボタン記録: {buttonName}, {elapsedTime:F3}s");
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
