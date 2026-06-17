using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;
using Tobii.Gaming;

public class TobiiGazeCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    public string saveFolderPath = @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("記録間隔")]
    public float logInterval = 0.02f; // 50Hz相当

    [Header("一定行数ごとにFlush")]
    [SerializeField] private int flushEveryLines = 100;

    private StreamWriter writer;
    private bool isLogging = false;

    private float startTime;
    private float timer = 0f;

    private int sampleIndex = 0;
    private int linesSinceFlush = 0;

    private string currentFilePath = "";

    private string F(float value, string format = "F4")
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return "";
        }

        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public void StartLogging(string inputFileName)
    {
        // すでに記録中なら一度閉じる
        if (isLogging || writer != null)
        {
            StopLogging();
        }

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

        writer = new StreamWriter(filePath, false, new System.Text.UTF8Encoding(true));

        writer.WriteLine(
            "sampleIndex," +
            "time," +
            "isConnected," +
            "appFocused," +
            "isValid," +
            "gameScreenX,gameScreenY," +
            "clampedGameScreenX,clampedGameScreenY," +
            "viewportX,viewportY," +
            "rawScreenX,rawScreenY," +
            "screenWidth,screenHeight"
        );

        currentFilePath = filePath;
        startTime = Time.time;
        timer = 0f;
        sampleIndex = 0;
        linesSinceFlush = 0;
        isLogging = true;

        Debug.Log("Tobii gaze logging started: " + filePath);
        Debug.Log("TobiiAPI.IsConnected at StartLogging = " + TobiiAPI.IsConnected);
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

        Debug.Log("Tobii gaze logging stopped: " + currentFilePath);
    }

    private void Update()
    {
        if (!isLogging) return;
        if (writer == null) return;

        timer += Time.deltaTime;

        if (timer < logInterval) return;

        // 0に戻すより、差し引いた方が周期のズレが少ない
        timer -= logInterval;

        WriteGazeLine();
    }

    private void WriteGazeLine()
    {
        float elapsedTime = Time.time - startTime;

        bool isConnected = TobiiAPI.IsConnected;
        bool appFocused = Application.isFocused;

        GazePoint gazePoint = TobiiAPI.GetGazePoint();

        int screenWidth = Screen.width;
        int screenHeight = Screen.height;

        if (gazePoint.IsValid)
        {
            Vector2 viewportPos = gazePoint.Viewport;
            Vector2 rawScreenPos = gazePoint.Screen;

            // Viewportは今回のCSVを見る限り、0～1座標として出ている
            float gameScreenX = viewportPos.x * screenWidth;
            float gameScreenY = viewportPos.y * screenHeight;

            // 解析用に画面内へ丸めた座標も保存
            float clampedGameScreenX = Mathf.Clamp(gameScreenX, 0f, screenWidth);
            float clampedGameScreenY = Mathf.Clamp(gameScreenY, 0f, screenHeight);

            writer.WriteLine(
                sampleIndex + "," +
                F(elapsedTime, "F4") + "," +
                isConnected + "," +
                appFocused + "," +
                "1," +
                F(gameScreenX, "F2") + "," +
                F(gameScreenY, "F2") + "," +
                F(clampedGameScreenX, "F2") + "," +
                F(clampedGameScreenY, "F2") + "," +
                F(viewportPos.x, "F6") + "," +
                F(viewportPos.y, "F6") + "," +
                F(rawScreenPos.x, "F2") + "," +
                F(rawScreenPos.y, "F2") + "," +
                screenWidth + "," +
                screenHeight
            );
        }
        else
        {
            writer.WriteLine(
                sampleIndex + "," +
                F(elapsedTime, "F4") + "," +
                isConnected + "," +
                appFocused + "," +
                "0," +
                "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                "," +
                screenWidth + "," +
                screenHeight
            );
        }

        sampleIndex++;
        linesSinceFlush++;

        if (linesSinceFlush >= flushEveryLines)
        {
            writer.Flush();
            linesSinceFlush = 0;
        }
    }

    private void OnApplicationQuit()
    {
        StopLogging();
    }

    private void OnDestroy()
    {
        StopLogging();
    }
}