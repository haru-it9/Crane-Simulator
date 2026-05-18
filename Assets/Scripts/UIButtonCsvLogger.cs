using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class UIButtonCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    [SerializeField]
    private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    private StreamWriter currentWriter;
    private StreamWriter speedWriter;

    private bool isLogging = false;

    private string currentFilePath;
    private string speedFilePath;

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
            inputFileName = "CurrentButtonLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        currentFilePath = Path.Combine(folderPath, inputFileName + "_CurrentButton.csv");
        speedFilePath = Path.Combine(folderPath, inputFileName + "_SpeedChange.csv");

        currentWriter = new StreamWriter(currentFilePath, false);
        speedWriter = new StreamWriter(speedFilePath, false);

        // リフマグON/OFFボタン用
        currentWriter.WriteLine("Time,ButtonName");

        // 速度変更ボタン用
        speedWriter.WriteLine(
            "Time,ButtonName,MainLifMagXSpeed,MainLifMagYSpeed,MainCraneZSpeed"
        );

        startTime = Time.time;
        isLogging = true;

        Debug.Log("UIボタンCSV記録開始: " + currentFilePath);
        Debug.Log("速度変更CSV記録開始: " + speedFilePath);
    }

    // =========================
    // リフマグON/OFFボタン記録用
    // =========================
    public void RecordButtonClick(string buttonName)
    {
        if (!isLogging || currentWriter == null) return;

        float elapsedTime = Time.time - startTime;

        currentWriter.WriteLine($"{elapsedTime:F3},{buttonName}");
        currentWriter.Flush();

        Debug.Log($"UIボタン記録: {buttonName}, {elapsedTime:F3}s");
    }

    // =========================
    // 速度変更ボタン記録用
    // =========================
    public void RecordSpeedChange(
        string buttonName,
        float mainLifMagXSpeed,
        float mainLifMagYSpeed,
        float mainCraneZSpeed
    )
    {
        if (!isLogging || speedWriter == null) return;

        float elapsedTime = Time.time - startTime;

        speedWriter.WriteLine(
            $"{elapsedTime:F3},{buttonName},{mainLifMagXSpeed:F3},{mainLifMagYSpeed:F3},{mainCraneZSpeed:F3}"
        );

        speedWriter.Flush();

        Debug.Log(
            $"速度変更記録: {buttonName}, " +
            $"X={mainLifMagXSpeed}, Y={mainLifMagYSpeed}, Z={mainCraneZSpeed}, " +
            $"{elapsedTime:F3}s"
        );
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
        if (currentWriter != null)
        {
            currentWriter.Flush();
            currentWriter.Close();
            currentWriter = null;
        }

        if (speedWriter != null)
        {
            speedWriter.Flush();
            speedWriter.Close();
            speedWriter = null;
        }
    }
}
