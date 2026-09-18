using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 大フェーズ内の詳細ステップ構成を共有する設定です。
/// 複数クレーン・複数実験モードで同じAssetを利用できます。
/// </summary>
[CreateAssetMenu(
    fileName = "CraneWorkPhaseProfile",
    menuName = "Crane Simulator/Crane Work Phase Profile"
)]
public class CraneWorkPhaseProfile : ScriptableObject
{
    [SerializeField]
    private List<CraneWorkStepDefinition> stepDefinitions =
        new List<CraneWorkStepDefinition>();

    public IReadOnlyList<CraneWorkStepDefinition> StepDefinitions =>
        stepDefinitions;

    public void GetStepsForPhase(
        CraneStatusManager.WorkPhase phase,
        List<CraneWorkStepDefinition> destination
    )
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();

        if (stepDefinitions == null)
        {
            return;
        }

        foreach (CraneWorkStepDefinition definition in stepDefinitions)
        {
            if (definition != null && definition.majorPhase == phase)
            {
                destination.Add(definition);
            }
        }
    }

    [ContextMenu("Reset To Built-In Defaults")]
    private void ResetToBuiltInDefaults()
    {
        stepDefinitions = CreateBuiltInDefaults();
    }

    /// <summary>
    /// Profile未設定時にも利用できる初期ステップ構成を生成します。
    /// </summary>
    public static List<CraneWorkStepDefinition> CreateBuiltInDefaults()
    {
        return new List<CraneWorkStepDefinition>
        {
            CreateStep(
                "Move1.PickupCoarseMove",
                "吸着位置へ大まかに移動",
                CraneStatusManager.WorkPhase.Move1,
                0.20f,
                Condition(
                    CraneWorkConditionType.PositionWithinTarget,
                    1.00f,
                    1.00f
                ),
                Condition(CraneWorkConditionType.BoardNotAttached)
            ),
            CreateStep(
                "Move1.PickupFineAlign",
                "吸着位置を微調整",
                CraneStatusManager.WorkPhase.Move1,
                0.30f,
                Condition(
                    CraneWorkConditionType.PositionWithinTarget,
                    0.25f,
                    0.25f
                ),
                Condition(CraneWorkConditionType.BoardNotAttached),
                Condition(
                    CraneWorkConditionType.HorizontalSpeedBelow,
                    0.05f
                )
            ),
            CreateStep(
                "LiftUp.PickupLowering",
                "リフマグを厚板まで下降",
                CraneStatusManager.WorkPhase.LiftUp,
                0.10f,
                Condition(CraneWorkConditionType.TouchdownObserved),
                Condition(
                    CraneWorkConditionType.VerticalSpeedBelow,
                    0.05f
                )
            ),
            CreateStep(
                "LiftUp.LoadAcquisition",
                "目標重量を吸着",
                CraneStatusManager.WorkPhase.LiftUp,
                0.50f,
                Condition(
                    CraneWorkConditionType.AttachedWeightWithinTarget,
                    100f,
                    100f
                )
            ),
            CreateStep(
                "LiftUp.LoadedRaising",
                "吊荷を安全高さまで上昇",
                CraneStatusManager.WorkPhase.LiftUp,
                0.20f,
                Condition(
                    CraneWorkConditionType.AttachedWeightWithinTarget,
                    100f,
                    100f
                ),
                Condition(
                    CraneWorkConditionType.LiftMagClearanceFromTouchdown,
                    0.50f
                )
            ),
            CreateStep(
                "Move2.DestinationCoarseMove",
                "配置位置へ大まかに移動",
                CraneStatusManager.WorkPhase.Move2,
                0.20f,
                Condition(CraneWorkConditionType.BoardAttached),
                Condition(
                    CraneWorkConditionType.PositionWithinTarget,
                    1.00f,
                    1.00f
                )
            ),
            CreateStep(
                "Move2.DestinationFineAlign",
                "配置位置を微調整",
                CraneStatusManager.WorkPhase.Move2,
                0.30f,
                Condition(CraneWorkConditionType.BoardAttached),
                Condition(
                    CraneWorkConditionType.PositionWithinTarget,
                    0.25f,
                    0.25f
                ),
                Condition(
                    CraneWorkConditionType.HorizontalSpeedBelow,
                    0.05f
                )
            ),
            CreateStep(
                "Place.PlacementLowering",
                "吊荷を配置面まで下降",
                CraneStatusManager.WorkPhase.Place,
                0.10f,
                Condition(CraneWorkConditionType.BoardAttached),
                Condition(
                    CraneWorkConditionType.PositionWithinTarget,
                    0.25f,
                    0.25f
                ),
                Condition(CraneWorkConditionType.TouchdownObserved),
                Condition(
                    CraneWorkConditionType.VerticalSpeedBelow,
                    0.05f
                )
            ),
            CreateStep(
                "Place.LoadRelease",
                "所定重量を配置",
                CraneStatusManager.WorkPhase.Place,
                0.50f,
                Condition(
                    CraneWorkConditionType.PlacementRemainingWeightWithinTarget,
                    100f,
                    100f
                )
            ),
            CreateStep(
                "Place.PostPlacementRaising",
                "配置後にリフマグを上昇",
                CraneStatusManager.WorkPhase.Place,
                0.20f,
                Condition(
                    CraneWorkConditionType.PlacementRemainingWeightWithinTarget,
                    100f,
                    100f
                ),
                Condition(
                    CraneWorkConditionType.LiftMagClearanceFromTouchdown,
                    0.50f
                )
            ),
            CreateStep(
                "PlaceToTrack.PlacementLowering",
                "吊荷をトレーラまで下降",
                CraneStatusManager.WorkPhase.PlaceToTrack,
                0.10f,
                Condition(CraneWorkConditionType.BoardAttached),
                Condition(
                    CraneWorkConditionType.PositionWithinTarget,
                    0.25f,
                    0.25f
                ),
                Condition(CraneWorkConditionType.TouchdownObserved),
                Condition(
                    CraneWorkConditionType.VerticalSpeedBelow,
                    0.05f
                )
            ),
            CreateStep(
                "PlaceToTrack.LoadRelease",
                "所定重量をトレーラへ配置",
                CraneStatusManager.WorkPhase.PlaceToTrack,
                0.50f,
                Condition(
                    CraneWorkConditionType.PlacementRemainingWeightWithinTarget,
                    100f,
                    100f
                )
            ),
            CreateStep(
                "PlaceToTrack.PostPlacementRaising",
                "トレーラ配置後にリフマグを上昇",
                CraneStatusManager.WorkPhase.PlaceToTrack,
                0.20f,
                Condition(
                    CraneWorkConditionType.PlacementRemainingWeightWithinTarget,
                    100f,
                    100f
                ),
                Condition(
                    CraneWorkConditionType.LiftMagClearanceFromTouchdown,
                    0.50f
                )
            )
        };
    }

    private static CraneWorkStepDefinition CreateStep(
        string stepId,
        string displayName,
        CraneStatusManager.WorkPhase majorPhase,
        float stableSeconds,
        params CraneWorkConditionDefinition[] conditions
    )
    {
        CraneWorkStepDefinition definition =
            new CraneWorkStepDefinition
            {
                stepId = stepId,
                displayName = displayName,
                majorPhase = majorPhase,
                requiredStableSeconds = Mathf.Max(0f, stableSeconds),
                completionConditions =
                    new List<CraneWorkConditionDefinition>()
            };

        if (conditions != null)
        {
            definition.completionConditions.AddRange(conditions);
        }

        return definition;
    }

    private static CraneWorkConditionDefinition Condition(
        CraneWorkConditionType type,
        float firstThreshold = 0f,
        float secondThreshold = 0f
    )
    {
        return new CraneWorkConditionDefinition(
            type,
            firstThreshold,
            secondThreshold
        );
    }

    private void OnValidate()
    {
        if (stepDefinitions == null)
        {
            stepDefinitions = new List<CraneWorkStepDefinition>();
            return;
        }

        HashSet<string> usedIds = new HashSet<string>();

        for (int i = 0; i < stepDefinitions.Count; i++)
        {
            CraneWorkStepDefinition step = stepDefinitions[i];

            if (step == null)
            {
                continue;
            }

            step.requiredStableSeconds =
                Mathf.Max(0f, step.requiredStableSeconds);

            if (step.completionConditions == null)
            {
                step.completionConditions =
                    new List<CraneWorkConditionDefinition>();
            }

            string normalizedId = string.IsNullOrWhiteSpace(step.stepId)
                ? $"{step.majorPhase}.Step{i + 1}"
                : step.stepId.Trim();

            if (!usedIds.Add(normalizedId))
            {
                Debug.LogWarning(
                    $"CraneWorkPhaseProfile内でStep IDが重複しています: " +
                    normalizedId,
                    this
                );
            }

            step.stepId = normalizedId;
        }
    }
}
