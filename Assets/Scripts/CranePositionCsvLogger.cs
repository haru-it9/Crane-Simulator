using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;
using System.Text;

public class CranePositionCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    [SerializeField] private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("記録対象クレーン")]
    [SerializeField] private Transform craneTransform;

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
            inputFileName = "CranePositionLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        string fileName = inputFileName + "_CranePosition.csv";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(
            filePath,
            false,
            new UTF8Encoding(true)
        );
        writer.WriteLine("Time,X,Y,Z");

        startTime = Time.time;
        timer = 0f;
        linesSinceFlush = 0;
        isLogging = true;

        Debug.Log("クレーン座標CSV記録開始: " + filePath);
    }

    private void Update()
    {
        if (!isLogging || writer == null || craneTransform == null) return;
        if (ExperimentPauseManager.IsPaused) return;

        timer += Time.deltaTime;
        if (timer < logInterval) return;
        timer -= logInterval;

        float elapsedTime = Time.time - startTime;
        Vector3 pos = craneTransform.position;

        writer.WriteLine(string.Join(",", new string[]
        {
            elapsedTime.ToString("F3", CultureInfo.InvariantCulture),
            pos.x.ToString("F4", CultureInfo.InvariantCulture),
            pos.y.ToString("F4", CultureInfo.InvariantCulture),
            pos.z.ToString("F4", CultureInfo.InvariantCulture)
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
