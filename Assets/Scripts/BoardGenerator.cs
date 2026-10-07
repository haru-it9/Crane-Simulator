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

    [Header("板重量設定")]
    [Tooltip("Random生成時だけ使用する板密度[kg/m^3]です。")]
    [SerializeField] private float boardDensity = 7850f;

    [Tooltip(
        "選択座標の上側から、目標重量へ加算する板枚数です。"
    )]
    [SerializeField]
    [Min(1)]
    private int defaultPickupCount = 1;

    [Tooltip(
        "目標座標と板置場座標を対応付けるXZ距離の許容値[m]です。"
    )]
    [SerializeField]
    [Min(0f)]
    private float targetMatchTolerance = 0.75f;

    [Header("ランダム生成サイズの丸め")]
    [SerializeField] private bool roundRandomBoardSize = true;
    [SerializeField] private int boardXDecimalDigits = 1;
    [SerializeField] private int boardYDecimalDigits = 4;
    [SerializeField] private int boardZDecimalDigits = 1;

    [Header("BoardStageのサイズ")]
    [SerializeField] private float boardStageSizeX = 7.5f;
    [SerializeField] private Vector2 boardStageRandomYRange = new Vector2(0.05f, 1f);
    [SerializeField] private float boardStageSizeZ = 1.7f;

    [Header("板の積み上げ間隔")]
    [Tooltip(
        "OFFの場合は板同士を接触状態で積層します。" +
        "通常の吊り上げ実験ではOFFを使用してください。"
    )]
    [SerializeField] private bool useBoardGapY = false;

    [Tooltip("Use Board Gap YがONの場合だけ使用する板間隔[m]です。")]
    [SerializeField] private float boardGapY = 0.05f;

    [Header("生成先の親オブジェクト")]
    [SerializeField] private Transform parentTransform;

    [Header("クレーン別の生成先コンテナ")]
    [SerializeField] private bool createContainerUnderThisObject = true;
    [SerializeField] private string containerName = "GeneratedBoards";

    private Transform runtimeParentTransform;
    private readonly List<GameObject> generatedObjects =
        new List<GameObject>();

    private readonly Dictionary<int, List<GameObject>>
        generatedBoardsBySpawnIndex =
            new Dictionary<int, List<GameObject>>();

     private class CsvBoardData
    {
        public int spawnIndex;
        public float stageY;
        public float boardX;
        public float boardY;
        public float boardZ;
        public float boardWeightKg;
    }

    [Header("起動時の生成")]
    [SerializeField]
    private bool generateOnStart = false;

    private void Start()
    {
        if (generateOnStart)
        {
            SpawnBoardsWithStage();
        }
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

        ClearGeneratedObjects();

        GetSpawnParent();

        if (generateMode == GenerateMode.Random)
        {
            SpawnRandom();
        }
        else
        {
            SpawnFromCsv();
        }

        Debug.Log($"{name}: BoardStageと板の生成が完了しました。");
    }

    public void ResetBoards()
    {
        Debug.Log($"{name}: 板配置をリセットします");
        SpawnBoardsWithStage();
    }

    public void ClearGeneratedObjects()
    {
        for (int i = generatedObjects.Count - 1; i >= 0; i--)
        {
            if (generatedObjects[i] != null)
            {
                SafeDestroy(generatedObjects[i]);
            }
        }

        generatedObjects.Clear();
        generatedBoardsBySpawnIndex.Clear();

        Transform spawnParent = GetSpawnParent();

        if (spawnParent == null) return;

        // 念のため、生成済みリストから外れている Board / BoardStage も削除
        for (int i = spawnParent.childCount - 1; i >= 0; i--)
        {
            Transform child = spawnParent.GetChild(i);

            if (child == null) continue;

            if (child.name.StartsWith("Board_") ||
                child.name.StartsWith("BoardStage_"))
            {
                SafeDestroy(child.gameObject);
            }
        }
    }

    private void SafeDestroy(GameObject obj)
    {
        if (obj == null) return;

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private Transform GetSpawnParent()
    {
        if (!createContainerUnderThisObject)
        {
            return parentTransform;
        }

        if (runtimeParentTransform != null)
        {
            return runtimeParentTransform;
        }

        Transform existing = transform.Find(containerName);

        if (existing != null)
        {
            runtimeParentTransform = existing;
            return runtimeParentTransform;
        }

        GameObject container = new GameObject(containerName);
        container.transform.SetParent(transform, false);

        runtimeParentTransform = container.transform;

        return runtimeParentTransform;
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

                if (roundRandomBoardSize)
                {
                    boardX = RoundToDigits(boardX, boardXDecimalDigits);
                    boardY = RoundToDigits(boardY, boardYDecimalDigits);
                    boardZ = RoundToDigits(boardZ, boardZDecimalDigits);
                }

                CreateBoard(i, j, basePos, currentTopY, boardX, boardY, boardZ);

                currentTopY += boardY + GetEffectiveBoardGapY();
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
                    data.boardZ,
                    data.boardWeightKg
                );

                currentTopY +=
                    data.boardY + GetEffectiveBoardGapY();
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
            GetSpawnParent()
        );

        stage.transform.localScale = new Vector3(boardStageSizeX, stageY, boardStageSizeZ);
        stage.name = $"BoardStage_{index}";

        generatedObjects.Add(stage);

        return stage;
    }

    private GameObject CreateBoard(
        int spawnIndex,
        int boardIndex,
        Vector3 basePos,
        float currentTopY,
        float boardX,
        float boardY,
        float boardZ,
        float explicitWeightKg = -1f
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
            GetSpawnParent()
        );

        board.transform.localScale = new Vector3(boardX, boardY, boardZ);
        board.name = $"Board_{spawnIndex}_{boardIndex}";
        generatedObjects.Add(board);

        if (!generatedBoardsBySpawnIndex.TryGetValue(
                spawnIndex,
                out List<GameObject> boardsAtPoint
            ))
        {
            boardsAtPoint = new List<GameObject>();
            generatedBoardsBySpawnIndex.Add(
                spawnIndex,
                boardsAtPoint
            );
        }

        boardsAtPoint.Add(board);

        BoardInfo boardInfo = board.GetComponent<BoardInfo>();

        if (boardInfo == null)
        {
            boardInfo = board.AddComponent<BoardInfo>();
        }

        if (explicitWeightKg > 0f)
        {
            boardInfo.SetBoardInfoWithWeight(
                boardX,
                boardY,
                boardZ,
                explicitWeightKg
            );
        }
        else
        {
            boardInfo.SetBoardInfo(
                boardX,
                boardY,
                boardZ,
                boardDensity
            );
        }

        return board;
    }

    public bool TryGetPickupTargetWeightKg(
        float targetX,
        float targetZ,
        out float targetWeightKg,
        out int pickupCount,
        out int matchedSpawnIndex
    )
    {
        return TryGetPickupTargetWeightKg(targetX,targetZ,Mathf.Max(1,defaultPickupCount),out targetWeightKg,out pickupCount,out matchedSpawnIndex);
    }

    public bool TryGetPickupTargetWeightKg(float targetX,float targetZ,int requestedCount,out float targetWeightKg,out int pickupCount,out int matchedSpawnIndex)
    {
        List<GameObject> boards;
        bool success=TryGetPickupBoards(targetX,targetZ,requestedCount,out boards,out targetWeightKg,out matchedSpawnIndex);
        pickupCount=success ? boards.Count : 0;
        return success;
    }

    public bool TryGetPickupBoards(float targetX,float targetZ,int requestedCount,out List<GameObject> boards,out float targetWeightKg,out int matchedSpawnIndex)
    {
        boards=new List<GameObject>();targetWeightKg=0;matchedSpawnIndex=-1;
        if(requestedCount<1) return false;
        float bestDistance=float.PositiveInfinity;
        for(int i=0;i<spawnPositions.Count;i++)
        {
            float distance=Vector2.Distance(new Vector2(targetX,targetZ),new Vector2(spawnPositions[i].x,spawnPositions[i].z));
            if(distance<bestDistance) { bestDistance=distance;matchedSpawnIndex=i; }
        }
        List<GameObject> generatedBoards;
        if(matchedSpawnIndex<0 || bestDistance>Mathf.Max(0,targetMatchTolerance) || !generatedBoardsBySpawnIndex.TryGetValue(matchedSpawnIndex,out generatedBoards)) return false;
        var available=generatedBoards.Where(board=>board!=null && Vector2.Distance(new Vector2(board.transform.position.x,board.transform.position.z),
            new Vector2(targetX,targetZ))<=Mathf.Max(0,targetMatchTolerance)).OrderByDescending(board=>board.transform.position.y).ToList();
        if(available.Count<requestedCount) return false;
        boards=available.Take(requestedCount).ToList();
        foreach(var board in boards)
        {
            var info=board.GetComponent<BoardInfo>();
            if(info==null || info.Weight<=0) { boards.Clear();targetWeightKg=0;return false; }
            targetWeightKg+=info.Weight;
        }
        return targetWeightKg>0;
    }

    private float GetEffectiveBoardGapY()
    {
        return useBoardGapY
            ? Mathf.Max(0f, boardGapY)
            : 0f;
    }

    private float RoundToDigits(float value, int digits)
    {
        float scale = Mathf.Pow(10f, digits);
        return Mathf.Round(value * scale) / scale;
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

            if (values.Length < 6)
            {
                Debug.LogWarning(
                    $"CSV {i + 1}行目の列数が不足しています。" +
                    "必要列: spawnIndex,stageY,boardX,boardY,boardZ,boardWeightKg: " +
                    line
                );
                continue;
            }

            CsvBoardData data = new CsvBoardData();

            data.spawnIndex = int.Parse(values[0]);
            data.stageY = float.Parse(values[1]);
            data.boardX = float.Parse(values[2]);
            data.boardY = float.Parse(values[3]);
            data.boardZ = float.Parse(values[4]);
            data.boardWeightKg = float.Parse(values[5]);

            if (data.boardWeightKg <= 0f)
            {
                Debug.LogWarning(
                    $"CSV {i + 1}行目のboardWeightKgは" +
                    $"0より大きい値にしてください: {line}"
                );
                continue;
            }

            dataList.Add(data);
        }

        return dataList;
    }
}
