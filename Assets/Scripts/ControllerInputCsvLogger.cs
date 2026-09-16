using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;
using System.Text;

public class ControllerInputCsvLogger : MonoBehaviour
{
    [Header("入力軸名")]
    [SerializeField] private string joyStick2Horizontal = "JoyStick2Horizontal";
    [SerializeField] private string joyStick2Vertical = "JoyStick2Vertical";
    [SerializeField] private string joyStick3Vertical = "JoyStick3Vertical";
    [SerializeField] private string joyStick3Slider = "JoyStick3Slider";

    [Header("保存先フォルダ")]
    [SerializeField] private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("記録間隔")]
    [SerializeField]
    [Min(0.001f)]
    private float logInterval = 0.02f;

    [SerializeField]
    [Min(1)]
    private int flushEveryLines = 100;

    private StreamWriter writer;
    private bool isLogging = false;
    private string filePath;

    private float startTime;
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
            inputFileName = "ControllerInputLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        string fileName = inputFileName + "_ControllerInput.csv";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(
            filePath,
            false,
            new UTF8Encoding(true)
        );

        writer.WriteLine(
            "Time,JoyStick2Horizontal,JoyStick2Vertical,JoyStick3Vertical,JoyStick3Slider"
        );

        startTime = Time.time;
        timer = 0f;
        linesSinceFlush = 0;
        isLogging = true;

        Debug.Log("CSV記録開始: " + filePath);
    }

    private void Update()
    {
        if (!isLogging || writer == null) return;
        if (ExperimentPauseManager.IsPaused) return;

        timer += Time.deltaTime;
        if (timer < logInterval) return;
        timer -= logInterval;

        float elapsedTime = Time.time - startTime;

        float js2H = Input.GetAxis(joyStick2Horizontal);
        float js2V = Input.GetAxis(joyStick2Vertical);
        float js3V = Input.GetAxis(joyStick3Vertical);
        float slider = Input.GetAxis(joyStick3Slider);

        writer.WriteLine(string.Join(",", new string[]
        {
            elapsedTime.ToString("F3", CultureInfo.InvariantCulture),
            js2H.ToString("F4", CultureInfo.InvariantCulture),
            js2V.ToString("F4", CultureInfo.InvariantCulture),
            js3V.ToString("F4", CultureInfo.InvariantCulture),
            slider.ToString("F4", CultureInfo.InvariantCulture)
        }));

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
