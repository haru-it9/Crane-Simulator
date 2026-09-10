using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// クレーンのストック数に応じて、RawImageを下端から上方向へ伸縮します。
/// CraneStockManagerのMaximum Stock Countを100%として扱います。
/// </summary>
[DisallowMultipleComponent]
public class CraneStockGauge : MonoBehaviour
{
    [Header("ストック状態")]
    [SerializeField]
    private CraneStockManager craneStockManager;

    [Tooltip("Crane1なら0、Crane2なら1です。")]
    [SerializeField]
    private int craneIndex;

    [Header("ゲージ表示")]
    [Tooltip("高さを変化させるStockのRawImageです。")]
    [SerializeField]
    private RawImage stockImage;

    [Tooltip("ONの場合、高さを滑らかに変化させます。")]
    [SerializeField]
    private bool smoothChange = true;

    [Tooltip("1秒間に変化するUI上の高さです。")]
    [SerializeField]
    [Min(0.01f)]
    private float heightChangeSpeed = 300f;

    [Header("数値表示（任意）")]
    [SerializeField]
    private Text stockCountText;

    [SerializeField]
    private bool showPercentage = true;

    private RectTransform stockRectTransform;
    private float maximumHeight;
    private float fixedBottomPositionY;
    private bool layoutCaptured;
    private bool warnedAboutUnlimitedMaximum;

    private void Awake()
    {
        FindReferences();
        CaptureMaximumLayout();
    }

    private void OnEnable()
    {
        FindReferences();

        if (!layoutCaptured)
        {
            CaptureMaximumLayout();
        }

        UpdateGauge(false);
    }

    private void Update()
    {
        UpdateGauge(smoothChange);
    }

    private void UpdateGauge(bool useSmoothChange)
    {
        if (craneStockManager == null ||
            stockRectTransform == null ||
            !layoutCaptured)
        {
            return;
        }

        int maximumStockCount =
            craneStockManager.MaximumStockCount;

        if (maximumStockCount <= 0)
        {
            if (!warnedAboutUnlimitedMaximum)
            {
                Debug.LogWarning(
                    $"{name}: CraneStockManagerのMaximum Stock Countを" +
                    "1以上に設定してください。",
                    this
                );

                warnedAboutUnlimitedMaximum = true;
            }

            SetGaugeHeight(0f);
            UpdateCountText(0, maximumStockCount, 0f);
            return;
        }

        warnedAboutUnlimitedMaximum = false;

        int stockCount =
            craneStockManager.GetStockCount(craneIndex);

        float fillRatio =
            craneStockManager.GetStockRatio(craneIndex);

        float targetHeight = maximumHeight * fillRatio;
        float currentHeight = stockRectTransform.rect.height;

        float nextHeight = useSmoothChange
            ? Mathf.MoveTowards(
                currentHeight,
                targetHeight,
                heightChangeSpeed * Time.deltaTime
            )
            : targetHeight;

        SetGaugeHeight(nextHeight);
        UpdateCountText(stockCount, maximumStockCount, fillRatio);
    }

    private void SetGaugeHeight(float height)
    {
        float clampedHeight = Mathf.Clamp(height, 0f, maximumHeight);

        stockRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            clampedHeight
        );

        // Pivot位置に関係なく、下端を固定して上方向へ伸ばします。
        Vector2 anchoredPosition =
            stockRectTransform.anchoredPosition;

        anchoredPosition.y =
            fixedBottomPositionY +
            clampedHeight * stockRectTransform.pivot.y;

        stockRectTransform.anchoredPosition = anchoredPosition;
    }

    private void UpdateCountText(
        int stockCount,
        int maximumStockCount,
        float fillRatio
    )
    {
        if (stockCountText == null)
        {
            return;
        }

        stockCountText.text = showPercentage
            ? $"{stockCount}/{maximumStockCount} ({fillRatio * 100f:F0}%)"
            : $"{stockCount}/{maximumStockCount}";
    }

    private void CaptureMaximumLayout()
    {
        if (stockRectTransform == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();

        maximumHeight = stockRectTransform.rect.height;
        fixedBottomPositionY =
            stockRectTransform.anchoredPosition.y -
            maximumHeight * stockRectTransform.pivot.y;

        layoutCaptured = maximumHeight > 0f;

        if (!layoutCaptured)
        {
            Debug.LogWarning(
                $"{name}: Stock RawImageの最大Heightを1以上にしてください。",
                this
            );
        }
    }

    private void FindReferences()
    {
        if (stockImage == null)
        {
            stockImage = GetComponent<RawImage>();
        }

        stockRectTransform = stockImage != null
            ? stockImage.rectTransform
            : null;

        if (craneStockManager == null)
        {
            craneStockManager =
                FindObjectOfType<CraneStockManager>(true);
        }
    }

    private void Reset()
    {
        stockImage = GetComponent<RawImage>();
        craneStockManager =
            FindObjectOfType<CraneStockManager>(true);
    }

    private void OnValidate()
    {
        craneIndex = Mathf.Max(0, craneIndex);
        heightChangeSpeed = Mathf.Max(0.01f, heightChangeSpeed);
    }
}
