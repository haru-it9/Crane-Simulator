using UnityEngine;

/// <summary>
/// 1基分のストック板の生成位置と、持ち出し判定範囲を保持します。
/// CraneInstanceから参照してください。
/// </summary>
[DisallowMultipleComponent]
public class CraneStockLocation : MonoBehaviour
{
    public enum SpawnCoordinateMode
    {
        World,
        Local
    }

    [Header("ストック板")]
    [SerializeField]
    private GameObject boardPrefab;

    [SerializeField]
    [Min(0)]
    private int initialStockCount;

    [Header("生成座標")]
    [Tooltip("World: ワールド座標、Local: このGameObjectを基準としたローカル座標")]
    [SerializeField]
    private SpawnCoordinateMode coordinateMode =
        SpawnCoordinateMode.World;

    [Tooltip("1枚目の板を生成する座標です。")]
    [SerializeField]
    private Vector3 spawnPosition;

    [Tooltip("生成する板の角度です。")]
    [SerializeField]
    private Vector3 spawnEulerAngles;

    [Tooltip("未設定の場合は、このGameObjectの子として生成します。")]
    [SerializeField]
    private Transform stockObjectParent;

    [Tooltip("板が1枚増えるごとに加える座標です。Z方向に並べる場合はZだけを設定します。")]
    [SerializeField]
    private Vector3 stackStep = new Vector3(0f, 0f, 1f);

    [Header("ストック持ち出し判定")]
    [Tooltip("板の中心がこのBoxColliderから出たとき、ストックを1減らします。")]
    [SerializeField]
    private BoxCollider stockArea;

    public int InitialStockCount => initialStockCount;
    public BoxCollider StockArea => stockArea;

    public GameObject CreateStockBoard(int stackIndex)
    {
        if (boardPrefab == null)
        {
            Debug.LogError($"{name}: Board Prefabが設定されていません。", this);
            return null;
        }

        Transform parent = stockObjectParent != null
            ? stockObjectParent
            : transform;

        GetSlotPose(
            stackIndex,
            out Vector3 worldPosition,
            out Quaternion worldRotation
        );

        GameObject board = Instantiate(
            boardPrefab,
            worldPosition,
            worldRotation,
            parent
        );

        board.name = $"StockBoard_{stackIndex + 1}";
        return board;
    }

    /// <summary>
    /// 既存のストック板を指定スロットへ移動します。
    /// 先頭の板が持ち出された後の詰め直しに使用します。
    /// </summary>
    public void MoveStockBoardToSlot(GameObject board, int slotIndex)
    {
        if (board == null) return;

        GetSlotPose(
            slotIndex,
            out Vector3 worldPosition,
            out Quaternion worldRotation
        );

        Rigidbody boardRigidbody = board.GetComponent<Rigidbody>();

        if (boardRigidbody != null)
        {
            boardRigidbody.velocity = Vector3.zero;
            boardRigidbody.angularVelocity = Vector3.zero;
            boardRigidbody.position = worldPosition;
            boardRigidbody.rotation = worldRotation;
        }
        else
        {
            board.transform.SetPositionAndRotation(
                worldPosition,
                worldRotation
            );
        }

        board.name = $"StockBoard_{slotIndex + 1}";
    }

    private void GetSlotPose(
        int slotIndex,
        out Vector3 worldPosition,
        out Quaternion worldRotation
    )
    {
        Vector3 indexedPosition =
            spawnPosition + stackStep * Mathf.Max(0, slotIndex);

        if (coordinateMode == SpawnCoordinateMode.World)
        {
            worldPosition = indexedPosition;
            worldRotation = Quaternion.Euler(spawnEulerAngles);
            return;
        }

        worldPosition = transform.TransformPoint(indexedPosition);
        worldRotation =
            transform.rotation * Quaternion.Euler(spawnEulerAngles);
    }

    public bool ContainsBoard(GameObject board)
    {
        if (board == null || stockArea == null)
        {
            return false;
        }

        Collider boardCollider = board.GetComponentInChildren<Collider>();
        Vector3 worldPoint = boardCollider != null
            ? boardCollider.bounds.center
            : board.transform.position;

        Vector3 localPoint =
            stockArea.transform.InverseTransformPoint(worldPoint) -
            stockArea.center;

        Vector3 halfSize = stockArea.size * 0.5f;

        return
            Mathf.Abs(localPoint.x) <= halfSize.x &&
            Mathf.Abs(localPoint.y) <= halfSize.y &&
            Mathf.Abs(localPoint.z) <= halfSize.z;
    }

    private void Reset()
    {
        coordinateMode = SpawnCoordinateMode.World;
        spawnPosition = transform.position;
        spawnEulerAngles = transform.eulerAngles;
        stockObjectParent = transform;
        stockArea = GetComponent<BoxCollider>();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 worldPosition =
            coordinateMode == SpawnCoordinateMode.World
                ? spawnPosition
                : transform.TransformPoint(spawnPosition);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(worldPosition, 0.15f);
    }

    private void OnValidate()
    {
        initialStockCount = Mathf.Max(0, initialStockCount);
    }
}
