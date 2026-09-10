using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// シーン内のクレーン1基分に属する参照をまとめます。
/// MainCraneの最上位GameObjectに追加してください。
/// </summary>
[DisallowMultipleComponent]
public class CraneInstance : MonoBehaviour
{
    [Header("識別情報")]
    [Tooltip("1から始まる重複しない番号を設定します。")]
    [SerializeField]
    [Min(1)]
    private int craneId = 1;

    [SerializeField]
    private string displayName = "Crane 1";

    [Header("クレーン本体")]
    [Tooltip("このクレーンのCraneUnitです。")]
    [SerializeField]
    private CraneUnit craneUnit;

    [Header("外部Camera")]
    [Tooltip("BirdCamerasやFrontCamerasなど、MainCrane配下にないCameraだけを登録します。")]
    [SerializeField]
    private Camera[] externalCameras = new Camera[0];

    [Header("クレーン固有オブジェクト")]
    [SerializeField]
    private BoardGenerator boardGenerator;

    [SerializeField]
    private GameObject trailerObject;

    [Header("操作情報表示")]
    [SerializeField]
    private Transform informationTarget;

    [SerializeField]
    private LifMagSystem lifMagSystem;

    [Header("介入シナリオ用位置補正")]
    [SerializeField]
    private float humanXOffset;

    [SerializeField]
    private float trailerXOffset;

    public int CraneId => craneId;
    public string DisplayName => displayName;
    public CraneUnit CraneUnit => craneUnit;
    public BoardGenerator BoardGenerator => boardGenerator;
    public GameObject TrailerObject => trailerObject;
    public Transform InformationTarget => informationTarget;
    public LifMagSystem LifMagSystem => lifMagSystem;
    public float HumanXOffset => humanXOffset;
    public float TrailerXOffset => trailerXOffset;

    /// <summary>
    /// MainCrane・MainLifMag配下のCameraと、外部Cameraをまとめて返します。
    /// 重複して登録されているCameraは1台にまとめます。
    /// </summary>
    public Camera[] GetAllCameras()
    {
        HashSet<Camera> cameras = new HashSet<Camera>();

        Camera[] internalCameras = GetComponentsInChildren<Camera>(true);

        foreach (Camera camera in internalCameras)
        {
            if (camera != null)
            {
                cameras.Add(camera);
            }
        }

        if (externalCameras != null)
        {
            foreach (Camera camera in externalCameras)
            {
                if (camera != null)
                {
                    cameras.Add(camera);
                }
            }
        }

        Camera[] result = new Camera[cameras.Count];
        cameras.CopyTo(result);
        return result;
    }

    /// <summary>
    /// MainCrane配下ではないCameraだけを返します。
    /// </summary>
    public Camera[] GetExternalCameras()
    {
        return externalCameras ?? new Camera[0];
    }

    /// <summary>
    /// Component追加時に、同一クレーン内から取得できる参照を自動設定します。
    /// </summary>
    private void Reset()
    {
        craneUnit = GetComponent<CraneUnit>();

        if (craneUnit == null)
        {
            craneUnit = GetComponentInChildren<CraneUnit>(true);
        }

        if (lifMagSystem == null)
        {
            lifMagSystem = GetComponentInChildren<LifMagSystem>(true);
        }

        displayName = $"Crane {craneId}";
    }

    private void OnValidate()
    {
        craneId = Mathf.Max(1, craneId);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = $"Crane {craneId}";
        }
    }
}
