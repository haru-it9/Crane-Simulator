using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class CranePositionCsvLogger : MonoBehaviour
{
    [Header("保存先フォルダ")]
    [SerializeField] private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("記録対象クレーン")]
    [SerializeField] private Transform craneTransform;

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
            inputFileName = "CranePositionLog";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            inputFileName = inputFileName.Replace(c, '_');
        }

        string fileName = inputFileName + "_CranePosition.csv";
        filePath = Path.Combine(folderPath, fileName);

        writer = new StreamWriter(filePath, false);
        writer.WriteLine("Time,X,Y,Z");

        startTime = Time.time;
        isLogging = true;

        Debug.Log("クレーン座標CSV記録開始: " + filePath);
    }

    private void Update()
    {
        if (!isLogging || writer == null || craneTransform == null) return;

        float elapsedTime = Time.time - startTime;
        Vector3 pos = craneTransform.position;

        writer.WriteLine($"{elapsedTime:F3},{pos.x:F4},{pos.y:F4},{pos.z:F4}");
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
