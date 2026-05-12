using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

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
            inputFileName = "ControllerInputLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        string fileName = inputFileName + "_ControllerInput.csv";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(filePath, false);

        writer.WriteLine(
            "Time,JoyStick2Horizontal,JoyStick2Vertical,JoyStick3Vertical,JoyStick2Slider"
        );
 
        startTime = Time.time;
        isLogging = true;

        Debug.Log("CSV記録開始: " + filePath);
    }

    private void Update()
    {
        if (!isLogging || writer == null) return;

        float elapsedTime = Time.time - startTime;

        float js2H = Input.GetAxis(joyStick2Horizontal);
        float js2V = Input.GetAxis(joyStick2Vertical);
        float js3V = Input.GetAxis(joyStick3Vertical);
        float slider = Input.GetAxis(joyStick3Slider);

        writer.WriteLine(
            $"{elapsedTime:F3},{js2H:F4},{js2V:F4},{js3V:F4},{slider:F4}"
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
        if (writer == null) return;

        writer.Flush();
        writer.Close();
        writer = null;
    }
}
