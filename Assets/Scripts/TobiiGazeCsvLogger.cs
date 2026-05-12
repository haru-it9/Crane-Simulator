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

        string timeStamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName;

        if (string.IsNullOrEmpty(inputFileName))
        {
            fileName = "gaze_log_" + timeStamp;
        }
        else
        {
            fileName = inputFileName + "_gaze";
        }

        string filePath = Path.Combine(saveFolderPath, fileName + ".csv");

        writer = new StreamWriter(filePath, false);

        writer.WriteLine(
            "time," +
            "gameScreenX,gameScreenY," +
            "windowsX,windowsY," +
            "viewportX,viewportY," +
            "rawScreenX,rawScreenY," +
            "screenWidth,screenHeight," +
            "isValid"
        );

        startTime = Time.time;
        timer = 0f;
        isLogging = true;

        Debug.Log("Tobii gaze logging started: " + filePath);
    }

    public void StopLogging()
    {
        if (!isLogging && writer == null) return;

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

        if (gazePoint.IsValid)
        {
            Vector2 viewportPos = gazePoint.Viewport;
            Vector2 rawScreenPos = gazePoint.Screen;

            // Unity Game画面座標
            // 左下 = (0,0), 右上 = (Screen.width, Screen.height)
            // 現在のViewportが中央原点系として出ているため補正
            float gameScreenX = (viewportPos.x / 2f + 0.5f) * Screen.width;
            float gameScreenY = (viewportPos.y / 2f + 0.5f) * Screen.height;

            // Windows画面座標系
            // 左下 = (0,0), 右上 = (Screen.width, Screen.height)
            // rawScreenPosは左上原点系に近いためYを反転
            float windowsX = rawScreenPos.x;
            float windowsY = Screen.height - rawScreenPos.y;

            writer.WriteLine(
                elapsedTime.ToString("F4") + "," +
                gameScreenX.ToString("F2") + "," +
                gameScreenY.ToString("F2") + "," +
                windowsX.ToString("F2") + "," +
                windowsY.ToString("F2") + "," +
                viewportPos.x.ToString("F6") + "," +
                viewportPos.y.ToString("F6") + "," +
                rawScreenPos.x.ToString("F2") + "," +
                rawScreenPos.y.ToString("F2") + "," +
                Screen.width + "," +
                Screen.height + "," +
                "1"
            );
        }
        else
        {
            writer.WriteLine(
                elapsedTime.ToString("F4") + "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                Screen.width + "," +
                Screen.height + "," +
                "0"
            );
        }
    }

    private void OnApplicationQuit()
    {
        StopLogging();
    }
}
