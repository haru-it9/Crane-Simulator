using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject boardPrefab;
    [SerializeField] private GameObject boardStagePrefab;

    [Header("配置座標一覧")]
    [SerializeField] private List<Vector3> spawnPositions = new List<Vector3>();

    [Header("各座標に生成する枚数")]
    [SerializeField] private int boardsPerPoint = 3;

    [Header("板サイズのランダム範囲")]
    [SerializeField] private Vector2 boardRandomXRange = new Vector2(1.5f, 7.35f);
    [SerializeField] private Vector2 boardRandomYRange = new Vector2(0.00225f, 0.0675f);
    [SerializeField] private Vector2 boardRandomZRange = new Vector2(0.5f, 1.65f);

    [Header("BoardStageのサイズ")]
    [SerializeField] private float boardStageSizeX = 7.5f;
    [SerializeField] private Vector2 boardStageRandomYRange = new Vector2(0.05f, 1f);
    [SerializeField] private float boardStageSizeZ = 1.7f;

    [Header("板の積み上げ間隔")]
    [SerializeField] private float boardGapY = 0.05f;

    [Header("生成先の親オブジェクト")]
    [SerializeField] private Transform parentTransform;

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

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Vector3 basePos = spawnPositions[i];

            // =========================
            // 1. BoardStageを生成
            // =========================
            float stageY = Random.Range(boardStageRandomYRange.x, boardStageRandomYRange.y);

            // 指定座標を「床面位置」とみなし、中心を半分上にずらす
            Vector3 stageCenterPos = basePos + new Vector3(0f, stageY / 2f, 0f);

            GameObject stage = Instantiate(
                boardStagePrefab,
                stageCenterPos,
                Quaternion.identity,
                parentTransform
            );

            stage.transform.localScale = new Vector3(boardStageSizeX, stageY, boardStageSizeZ);
            stage.name = $"BoardStage_{i}";

            // Stage上面の高さ
            float stageTopY = basePos.y + stageY;

            // =========================
            // 2. Stageの上に板を3枚生成
            // =========================
            float currentTopY = stageTopY;

            for (int j = 0; j < boardsPerPoint; j++)
            {
                float boardX = Random.Range(boardRandomXRange.x, boardRandomXRange.y);
                float boardY = Random.Range(boardRandomYRange.x, boardRandomYRange.y);
                float boardZ = Random.Range(boardRandomZRange.x, boardRandomZRange.y);

                // 板の中心位置 = 現在の上端 + 板の高さの半分
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
                board.name = $"Board_{i}_{j}";

                // 次の板のために上端を更新
                currentTopY += boardY + boardGapY;
            }
        }

        Debug.Log("BoardStageと板の生成が完了しました。");
    }
}
