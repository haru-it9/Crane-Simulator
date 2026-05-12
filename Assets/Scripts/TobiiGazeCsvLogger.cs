using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Tobii.Gaming;

public class TobiiGazeCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    public string saveFolderPath = @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("記録間隔")]
    public float logInterval = 0.02f; // 50Hz相当

    private StreamWriter writer;
    private bool isLogging = false;
    private float startTime;
    private float timer = 0f;

    public void StartLogging(string inputFileName)
    {
        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        string fileName;

        if (string.IsNullOrEmpty(inputFileName))
        {
            fileName = "gaze_log_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
        else
        {
            fileName = inputFileName + "_gaze";
        }

        string filePath = Path.Combine(saveFolderPath, fileName + ".csv");

        writer = new StreamWriter(filePath, false);
        writer.WriteLine("time,gazeX,gazeY,isValid");

        startTime = Time.time;
        timer = 0f;
        isLogging = true;
    }

    public void StopLogging()
    {
        isLogging = false;

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }

        Debug.Log("Tobii gaze logging stopped");
    }

    private void Update()
    {
        if (!isLogging) return;

        timer += Time.deltaTime;
        if (timer < logInterval) return;
        timer = 0f;

        float elapsedTime = Time.time - startTime;

        GazePoint gazePoint = TobiiAPI.GetGazePoint();

        Debug.Log("Gaze Valid: " + gazePoint.IsValid);

        if (gazePoint.IsValid)
        {
            Vector2 viewportPos = gazePoint.Viewport;

            float screenX = viewportPos.x * Screen.width;
            float screenY = viewportPos.y * Screen.height;

            writer.WriteLine(
                elapsedTime.ToString("F4") + "," +
                screenX.ToString("F2") + "," +
                screenY.ToString("F2") + "," +
                "1"
            );
        }
        else
        {
            writer.WriteLine(
                elapsedTime.ToString("F4") + "," +
                "," +
                "," +
                "0"
            );
        }
    }

    private void OnApplicationQuit()
    {
        StopLogging();
    }
}
