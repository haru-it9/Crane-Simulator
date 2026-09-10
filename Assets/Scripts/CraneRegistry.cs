using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// シーン内のCraneInstanceを自動検出し、Crane ID順に管理します。
/// </summary>
[DisallowMultipleComponent]
public class CraneRegistry : MonoBehaviour
{
    [Header("自動検出設定")]
    [SerializeField]
    private bool refreshOnAwake = true;

    [SerializeField]
    private bool logDetectedCranes = true;

    private readonly List<CraneInstance> allCranes =
        new List<CraneInstance>();

    public IReadOnlyList<CraneInstance> AllCranes => allCranes;
    public int TotalCraneCount => allCranes.Count;
    public int ActiveCraneCount { get; private set; }

    public event Action<int> ActiveCraneCountChanged;

    private void Awake()
    {
        if (refreshOnAwake)
        {
            RefreshRegistry();
        }
    }

    /// <summary>
    /// 非アクティブなGameObjectを含め、シーン内の全クレーンを再検出します。
    /// </summary>
    public void RefreshRegistry()
    {
        int previousActiveCount = ActiveCraneCount;

        allCranes.Clear();

        CraneInstance[] detectedCranes =
            FindObjectsOfType<CraneInstance>(true);

        Array.Sort(
            detectedCranes,
            (left, right) => left.CraneId.CompareTo(right.CraneId)
        );

        HashSet<int> usedIds = new HashSet<int>();

        foreach (CraneInstance crane in detectedCranes)
        {
            if (crane == null)
                continue;

            if (!usedIds.Add(crane.CraneId))
            {
                Debug.LogError(
                    $"Crane ID {crane.CraneId} が重複しています: {crane.name}",
                    crane
                );
            }

            if (crane.CraneUnit == null)
            {
                Debug.LogWarning(
                    $"{crane.name} のCraneInstanceにCraneUnitが設定されていません。",
                    crane
                );
            }

            allCranes.Add(crane);
        }

        if (allCranes.Count == 0)
        {
            ActiveCraneCount = 0;
            Debug.LogWarning("CraneInstanceが1基も見つかりませんでした。");
            return;
        }

        ActiveCraneCount = previousActiveCount <= 0
            ? allCranes.Count
            : Mathf.Clamp(previousActiveCount, 1, allCranes.Count);

        if (logDetectedCranes)
        {
            LogDetectedCranes();
        }
    }

    /// <summary>
    /// 今回の試験で使用する基数を設定します。
    /// 現段階ではCrane IDが小さい順に指定基数分を有効対象とします。
    /// </summary>
    public void SetActiveCraneCount(int requestedCount)
    {
        if (allCranes.Count == 0)
        {
            RefreshRegistry();
        }

        if (allCranes.Count == 0)
            return;

        int clampedCount = Mathf.Clamp(requestedCount, 1, allCranes.Count);

        if (clampedCount != requestedCount)
        {
            Debug.LogWarning(
                $"指定基数{requestedCount}を、使用可能範囲の{clampedCount}に補正しました。"
            );
        }

        if (ActiveCraneCount == clampedCount)
            return;

        ActiveCraneCount = clampedCount;
        ActiveCraneCountChanged?.Invoke(ActiveCraneCount);

        Debug.Log(
            $"有効クレーン基数を{ActiveCraneCount}/{TotalCraneCount}基に設定しました。"
        );
    }

    public CraneInstance GetCraneByRuntimeIndex(int runtimeIndex)
    {
        if (runtimeIndex < 0 || runtimeIndex >= allCranes.Count)
            return null;

        return allCranes[runtimeIndex];
    }

    public CraneInstance GetCraneById(int craneId)
    {
        foreach (CraneInstance crane in allCranes)
        {
            if (crane != null && crane.CraneId == craneId)
            {
                return crane;
            }
        }

        return null;
    }

    public bool IsRuntimeIndexActive(int runtimeIndex)
    {
        return runtimeIndex >= 0 && runtimeIndex < ActiveCraneCount;
    }

    public bool IsCraneActive(CraneInstance crane)
    {
        if (crane == null)
            return false;

        int runtimeIndex = allCranes.IndexOf(crane);
        return IsRuntimeIndexActive(runtimeIndex);
    }

    public void LogDetectedCranes()
    {
        Debug.Log($"{allCranes.Count}基のクレーンを検出しました。");

        for (int i = 0; i < allCranes.Count; i++)
        {
            CraneInstance crane = allCranes[i];

            Debug.Log(
                $"RuntimeIndex={i}, Crane ID={crane.CraneId}, " +
                $"Name={crane.name}, DisplayName={crane.DisplayName}"
            );
        }
    }
}
