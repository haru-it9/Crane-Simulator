using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// クレーンごとのストック数、入荷タイマー、選択中クレーンの
/// ストック板オブジェクトを一括管理します。
/// </summary>
[DisallowMultipleComponent]
public class CraneStockManager : MonoBehaviour
{
    [Serializable]
    public class CraneStockState
    {
        public string craneName = "Crane";
        public int stockCount;
        public float remainingUntilNextStock;

        [HideInInspector]
        public bool stockReservedForMove1;
    }

    [Header("管理対象")]
    [SerializeField]
    private CraneRegistry craneRegistry;

    [SerializeField]
    private CraneOperationManager craneOperationManager;

    [Header("ストック追加間隔 [秒]")]
    [SerializeField]
    [Min(0.01f)]
    private float minArrivalInterval = 20f;

    [SerializeField]
    [Min(0.01f)]
    private float maxArrivalInterval = 60f;

    [Header("ストック上限")]
    [Tooltip("0以下の場合は上限なしです。")]
    [SerializeField]
    private int maximumStockCount = 20;

    [Header("開始条件")]
    [SerializeField]
    private bool waitForSimulatorStart = true;

    [Header("実行時状態")]
    [SerializeField]
    private List<CraneStockState> stockStates =
        new List<CraneStockState>();

    private readonly List<GameObject> materializedStockBoards =
        new List<GameObject>();

    private bool initialized;
    private int materializedCraneIndex = -1;

    public int ManagedCraneCount => stockStates.Count;
    public int MaximumStockCount => Mathf.Max(0, maximumStockCount);

    public float GetStockRatio(int craneIndex)
    {
        if (MaximumStockCount <= 0)
        {
            return 0f;
        }

        return Mathf.Clamp01(
            (float)GetStockCount(craneIndex) / MaximumStockCount
        );
    }

    private void Awake()
    {
        FindReferences();
    }

    private void Update()
    {
        if (waitForSimulatorStart &&
            !SimulatorStartManager.IsOperationEnabled)
        {
            return;
        }

        if (!EnsureInitialized())
        {
            return;
        }

        UpdateArrivalTimers();
        UpdateSelectedCraneStockObjects();
        DetectBoardsTakenOutOfStockArea();
    }

    public int GetStockCount(int craneIndex)
    {
        CraneStockState state = GetState(craneIndex);
        return state != null ? state.stockCount : 0;
    }

    public bool HasStock(int craneIndex)
    {
        CraneStockState state = GetState(craneIndex);
        return state != null && state.stockCount > 0;
    }

    /// <summary>
    /// Move1の開始時に呼びます。ストックがあれば1枚を予約します。
    /// この時点ではストック数を減らしません。
    /// </summary>
    public bool TryReserveStockForMove1(int craneIndex)
    {
        if (!EnsureInitialized()) return false;

        CraneStockState state = GetState(craneIndex);
        if (state == null) return false;

        if (state.stockReservedForMove1)
        {
            return true;
        }

        if (state.stockCount <= 0)
        {
            return false;
        }

        state.stockReservedForMove1 = true;
        return true;
    }

    /// <summary>
    /// 図示クレーンがPoint0へ到着したときに呼びます。
    /// 予約されていたストックを1枚消費します。
    /// </summary>
    public bool ConsumeReservedStockAtPoint0(int craneIndex)
    {
        if (!EnsureInitialized()) return false;

        CraneStockState state = GetState(craneIndex);
        if (state == null || !state.stockReservedForMove1)
        {
            return false;
        }

        // 操作対象クレーンは、実際の板がStock Areaを出た時点で減らします。
        // ここでも減らすと同じ板を二重に消費してしまうため、予約を維持します。
        if (craneIndex == GetSelectedCraneIndex())
        {
            return false;
        }

        state.stockReservedForMove1 = false;

        if (state.stockCount <= 0)
        {
            return false;
        }

        state.stockCount--;

        if (craneIndex == materializedCraneIndex)
        {
            RemoveOneMaterializedStockBoard();
        }

        Debug.Log(
            $"{state.craneName}: Point0到着でストックを1減少。" +
            $"現在数={state.stockCount}"
        );

        return true;
    }

    private bool EnsureInitialized()
    {
        if (initialized)
        {
            return true;
        }

        FindReferences();

        if (craneRegistry == null)
        {
            Debug.LogError("CraneStockManager: CraneRegistryが見つかりません。", this);
            return false;
        }

        if (craneRegistry.TotalCraneCount == 0)
        {
            craneRegistry.RefreshRegistry();
        }

        int activeCraneCount = craneRegistry.ActiveCraneCount;
        if (activeCraneCount <= 0)
        {
            return false;
        }

        stockStates.Clear();

        for (int craneIndex = 0;
             craneIndex < activeCraneCount;
             craneIndex++)
        {
            CraneInstance crane =
                craneRegistry.GetCraneByRuntimeIndex(craneIndex);

            CraneStockState state = new CraneStockState();
            state.craneName = crane != null
                ? crane.DisplayName
                : $"Crane {craneIndex + 1}";

            CraneStockLocation location = crane != null
                ? crane.StockLocation
                : null;

            state.stockCount = location != null
                ? location.InitialStockCount
                : 0;

            state.remainingUntilNextStock = GetRandomArrivalInterval();
            state.stockReservedForMove1 = false;

            stockStates.Add(state);
        }

        initialized = true;

        Debug.Log(
            $"CraneStockManager: {stockStates.Count}基分を初期化しました。"
        );

        return true;
    }

    private void FindReferences()
    {
        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }

        if (craneOperationManager == null)
        {
            craneOperationManager =
                FindObjectOfType<CraneOperationManager>(true);
        }
    }

    private void UpdateArrivalTimers()
    {
        for (int craneIndex = 0;
             craneIndex < stockStates.Count;
             craneIndex++)
        {
            CraneStockState state = stockStates[craneIndex];
            if (state == null) continue;

            state.remainingUntilNextStock -= Time.deltaTime;

            if (state.remainingUntilNextStock > 0f)
            {
                continue;
            }

            state.remainingUntilNextStock = GetRandomArrivalInterval();

            if (maximumStockCount > 0 &&
                state.stockCount >= maximumStockCount)
            {
                continue;
            }

            state.stockCount++;

            if (craneIndex == materializedCraneIndex)
            {
                CreateOneMaterializedStockBoard(craneIndex);
            }

            Debug.Log(
                $"{state.craneName}: ストックが1増加。" +
                $"現在数={state.stockCount}"
            );
        }
    }

    private float GetRandomArrivalInterval()
    {
        float minimum = Mathf.Max(0.01f, minArrivalInterval);
        float maximum = Mathf.Max(minimum, maxArrivalInterval);
        return UnityEngine.Random.Range(minimum, maximum);
    }

    private void UpdateSelectedCraneStockObjects()
    {
        int selectedCraneIndex = GetSelectedCraneIndex();

        if (selectedCraneIndex == materializedCraneIndex)
        {
            return;
        }

        ClearMaterializedStockBoards();
        materializedCraneIndex = selectedCraneIndex;

        if (materializedCraneIndex >= 0)
        {
            MaterializeCurrentStock(materializedCraneIndex);
        }
    }

    private void MaterializeCurrentStock(int craneIndex)
    {
        CraneStockState state = GetState(craneIndex);
        CraneStockLocation location = GetStockLocation(craneIndex);

        if (state == null || location == null)
        {
            return;
        }

        for (int i = 0; i < state.stockCount; i++)
        {
            GameObject board = location.CreateStockBoard(i);
            if (board != null)
            {
                materializedStockBoards.Add(board);
            }
        }
    }

    private void CreateOneMaterializedStockBoard(int craneIndex)
    {
        CraneStockLocation location = GetStockLocation(craneIndex);
        if (location == null) return;

        GameObject board = location.CreateStockBoard(
            materializedStockBoards.Count
        );

        if (board != null)
        {
            materializedStockBoards.Add(board);
        }
    }

    private void DetectBoardsTakenOutOfStockArea()
    {
        if (materializedCraneIndex < 0)
        {
            return;
        }

        CraneStockLocation location =
            GetStockLocation(materializedCraneIndex);

        if (location == null || location.StockArea == null)
        {
            return;
        }

        bool stockBoardWasRemoved = false;

        for (int i = materializedStockBoards.Count - 1;
             i >= 0;
             i--)
        {
            GameObject board = materializedStockBoards[i];

            if (board == null)
            {
                materializedStockBoards.RemoveAt(i);
                continue;
            }

            if (location.ContainsBoard(board))
            {
                continue;
            }

            // 範囲外へ出た板は通常の運搬板として残し、
            // ストック管理対象からだけ外します。
            materializedStockBoards.RemoveAt(i);
            DecreaseStockAfterManualTakeOut(materializedCraneIndex);
            stockBoardWasRemoved = true;
        }

        if (stockBoardWasRemoved)
        {
            RepositionMaterializedStockBoards(location);
        }
    }

    private void RepositionMaterializedStockBoards(
        CraneStockLocation location
    )
    {
        if (location == null) return;

        for (int slotIndex = 0;
             slotIndex < materializedStockBoards.Count;
             slotIndex++)
        {
            GameObject board = materializedStockBoards[slotIndex];

            if (board != null)
            {
                location.MoveStockBoardToSlot(board, slotIndex);
            }
        }
    }

    private void DecreaseStockAfterManualTakeOut(int craneIndex)
    {
        CraneStockState state = GetState(craneIndex);
        if (state == null || state.stockCount <= 0)
        {
            return;
        }

        state.stockCount--;
        state.stockReservedForMove1 = false;

        Debug.Log(
            $"{state.craneName}: 板がストック範囲外へ移動。" +
            $"現在数={state.stockCount}"
        );
    }

    private void RemoveOneMaterializedStockBoard()
    {
        for (int i = materializedStockBoards.Count - 1;
             i >= 0;
             i--)
        {
            GameObject board = materializedStockBoards[i];
            materializedStockBoards.RemoveAt(i);

            if (board != null)
            {
                Destroy(board);
                RepositionMaterializedStockBoards(
                    GetStockLocation(materializedCraneIndex)
                );
                return;
            }
        }
    }

    private void ClearMaterializedStockBoards()
    {
        // 先に管理対象から外すことで、削除を持ち出しと誤判定しません。
        List<GameObject> boardsToDestroy =
            new List<GameObject>(materializedStockBoards);

        materializedStockBoards.Clear();

        foreach (GameObject board in boardsToDestroy)
        {
            if (board != null)
            {
                Destroy(board);
            }
        }
    }

    private CraneStockState GetState(int craneIndex)
    {
        if (craneIndex < 0 || craneIndex >= stockStates.Count)
        {
            return null;
        }

        return stockStates[craneIndex];
    }

    private int GetSelectedCraneIndex()
    {
        int selectedCraneIndex = craneOperationManager != null
            ? craneOperationManager.CurrentCraneIndex
            : -1;

        return selectedCraneIndex >= 0 &&
               selectedCraneIndex < stockStates.Count
            ? selectedCraneIndex
            : -1;
    }

    private CraneStockLocation GetStockLocation(int craneIndex)
    {
        if (craneRegistry == null ||
            !craneRegistry.IsRuntimeIndexActive(craneIndex))
        {
            return null;
        }

        CraneInstance crane =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        return crane != null ? crane.StockLocation : null;
    }

    private void OnValidate()
    {
        minArrivalInterval = Mathf.Max(0.01f, minArrivalInterval);
        maxArrivalInterval = Mathf.Max(
            minArrivalInterval,
            maxArrivalInterval
        );
    }
}
