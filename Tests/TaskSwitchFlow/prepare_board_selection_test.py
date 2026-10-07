"""Compile the production selection methods without Unity's mesh/spawning dependencies."""
from pathlib import Path
import sys
root = Path(__file__).resolve().parents[2]
output = Path(sys.argv[1])
stubs = (root / 'Tests/TaskSwitchFlow/UnityFlowStubs.cs').read_text()
output.joinpath('BoardUnityStubs.cs').write_text(stubs[:stubs.index('public class CraneStatusManager')].replace('public static Vector2 zero', 'public static float Distance(Vector2 a,Vector2 b) => (float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)); public static Vector2 zero') + '\npublic class BoardInfo { public float Weight; }\n')
source = (root / 'Assets/Scripts/BoardGenerator.cs').read_text()
start = source.index('    public bool TryGetPickupTargetWeightKg(')
end = source.index('    private float GetEffectiveBoardGapY()', start)
output.joinpath('BoardSelectionProduction.cs').write_text('using System; using System.Collections.Generic; using System.Linq; using UnityEngine;\npublic class BoardGenerator { public List<Vector3> spawnPositions=new List<Vector3>(); public Dictionary<int,List<GameObject>> generatedBoardsBySpawnIndex=new Dictionary<int,List<GameObject>>(); public float targetMatchTolerance=.75f; public int defaultPickupCount=1;\n' + source[start:end] + '\n}\n')
