using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class BoardGenerator : MonoBehaviour
{
    private enum GenerateMode
    {
        Random,
        CSV
    }

    [Header("生成モード")]
    [SerializeField] private GenerateMode generateMode = GenerateMode.Random;

    [Header("CSV設定")]
    [SerializeField] private TextAsset csvFile;
    
    [Header("Prefab")]
    [SerializeField] private GameObject boardPrefab;
    [SerializeField] private GameObject boardStagePrefab;

    [Header("配置座標一覧")]
    [SerializeField] private List<Vector3> spawnPositions = new List<Vector3>();

    [Header("各座標に生成する枚数")]
    [SerializeField] private int boardsPerPoint = 3;

    [Header("板サイズのランダム範囲")]
    [SerializeField] private Vector2 boardRandomXRange = new Vector2(1.5f, 7.35f);
    [SerializeField] private Vector2 boardRandomYRange = new Vector2(0.0225f, 0.0675f);
    [SerializeField] private Vector2 boardRandomZRange = new Vector2(0.5f, 1.65f);

    [Header("BoardStageのサイズ")]
    [SerializeField] private float boardStageSizeX = 7.5f;
    [SerializeField] private Vector2 boardStageRandomYRange = new Vector2(0.05f, 1f);
    [SerializeField] private float boardStageSizeZ = 1.7f;

    [Header("板の積み上げ間隔")]
    [SerializeField] private float boardGapY = 0.05f;

    [Header("生成先の親オブジェクト")]
    [SerializeField] private Transform parentTransform;

    private class CsvBoardData
    {
        public int spawnIndex;
        public float stageY;
        public float boardX;
        public float boardY;
        public float boardZ;
    }

    private void Start()
    {
        SpawnBoardsWithStage();
    }

    [ContextMenu("板とBoardStageを生成")]
    public void SpawnBoardsWithStage()
    {
        if (boardPrefab == null)
        {
            Debug.LogError("boardPrefabが設定されていません。");
            return;
        }

        if (boardStagePrefab == null)
        {
            Debug.LogError("boardStagePrefabが設定されていません。");
            return;
        }

        if (generateMode == GenerateMode.Random)
        {
            SpawnRandom();
        }
        else
        {
            SpawnFromCsv();
        }

        Debug.Log("BoardStageと板の生成が完了しました。");
    }

    private void SpawnRandom()
    {
        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Vector3 basePos = spawnPositions[i];

            float stageY = Random.Range(boardStageRandomYRange.x, boardStageRandomYRange.y);

            GameObject stage = CreateStage(i, basePos, stageY);

            float currentTopY = basePos.y + stageY;

            for (int j = 0; j < boardsPerPoint; j++)
            {
                float boardX = Random.Range(boardRandomXRange.x, boardRandomXRange.y);
                float boardY = Random.Range(boardRandomYRange.x, boardRandomYRange.y);
                float boardZ = Random.Range(boardRandomZRange.x, boardRandomZRange.y);

                CreateBoard(i, j, basePos, currentTopY, boardX, boardY, boardZ);

                currentTopY += boardY + boardGapY;
            }
        }
    }

    private void SpawnFromCsv()
    {
        if (csvFile == null)
        {
            Debug.LogError("CSVファイルが設定されていません。");
            return;
        }

        List<CsvBoardData> csvDataList = LoadCsv(csvFile);

        var groupedData = csvDataList.GroupBy(data => data.spawnIndex);

        foreach (var group in groupedData)
        {
            int spawnIndex = group.Key;

            if (spawnIndex < 0 || spawnIndex >= spawnPositions.Count)
            {
                Debug.LogWarning($"spawnIndex {spawnIndex} は spawnPositions の範囲外です。");
                continue;
            }

            Vector3 basePos = spawnPositions[spawnIndex];

            List<CsvBoardData> boards = group.ToList();

            if (boards.Count == 0) continue;

            float stageY = boards[0].stageY;

            CreateStage(spawnIndex, basePos, stageY);

            float currentTopY = basePos.y + stageY;

            for (int j = 0; j < boards.Count; j++)
            {
                CsvBoardData data = boards[j];

                CreateBoard(
                    spawnIndex,
                    j,
                    basePos,
                    currentTopY,
                    data.boardX,
                    data.boardY,
                    data.boardZ
                );

                currentTopY += data.boardY + boardGapY;
            }
        }
    }

    private GameObject CreateStage(int index, Vector3 basePos, float stageY)
    {
        Vector3 stageCenterPos = basePos + new Vector3(0f, stageY / 2f, 0f);

        GameObject stage = Instantiate(
            boardStagePrefab,
            stageCenterPos,
            Quaternion.identity,
            parentTransform
        );

        stage.transform.localScale = new Vector3(boardStageSizeX, stageY, boardStageSizeZ);
        stage.name = $"BoardStage_{index}";

        return stage;
    }

    private GameObject CreateBoard(
        int spawnIndex,
        int boardIndex,
        Vector3 basePos,
        float currentTopY,
        float boardX,
        float boardY,
        float boardZ
    )
    {
        Vector3 boardPos = new Vector3(
            basePos.x,
            currentTopY + boardY / 2f,
            basePos.z
        );

        GameObject board = Instantiate(
            boardPrefab,
            boardPos,
            Quaternion.identity,
            parentTransform
        );

        board.transform.localScale = new Vector3(boardX, boardY, boardZ);
        board.name = $"Board_{spawnIndex}_{boardIndex}";

        return board;
    }

    private List<CsvBoardData> LoadCsv(TextAsset csv)
    {
        List<CsvBoardData> dataList = new List<CsvBoardData>();

        string[] lines = csv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrEmpty(line)) continue;

            string[] values = line.Split(',');

            if (values.Length < 5)
            {
                Debug.LogWarning($"CSV {i + 1}行目の列数が不足しています: {line}");
                continue;
            }

            CsvBoardData data = new CsvBoardData();

            data.spawnIndex = int.Parse(values[0]);
            data.stageY = float.Parse(values[1]);
            data.boardX = float.Parse(values[2]);
            data.boardY = float.Parse(values[3]);
            data.boardZ = float.Parse(values[4]);

            dataList.Add(data);
        }

        return dataList;
    }
}
