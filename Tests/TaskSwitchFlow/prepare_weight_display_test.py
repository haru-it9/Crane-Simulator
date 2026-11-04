"""Exercise production weight UI methods without camera/input dispatch dependencies."""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
output = Path(sys.argv[1])

def method(path, signature):
    text = (root / path).read_text()
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]

stubs = (root / 'Tests/TaskSwitchFlow/UnityFlowStubs.cs').read_text()
stubs = stubs.replace('public class CraneOperationManager {', 'public partial class CraneOperationManager {')
stubs = stubs.replace('public class CraneUnit {', 'public class CraneUnit { public LifMagSystem LifMagSystem => magnet;')
stubs = stubs.replace('public class Text : UnityEngine.MonoBehaviour {', 'public class Text : UnityEngine.MonoBehaviour { public UnityEngine.Color color;')
stubs = stubs.replace('None=0,Minus=45', 'None=0,Tab=9,Minus=45')
stubs = stubs.replace('public static bool GetKey(KeyCode key)', 'public static bool GetKeyDown(KeyCode key) { return false; } public static bool GetKey(KeyCode key)')
output.joinpath('WeightUiStubs.cs').write_text(stubs)
op = 'Assets/Scripts/CraneOperationManager.cs'
info = 'Assets/Scripts/CraneInformationDisplay.cs'
output.joinpath('WeightUiProduction.cs').write_text('''
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public partial class CraneOperationManager {
    private TargetInformationDisplay targetInformationDisplay;
    private CraneWorkLoadPlanManager selectedLoadPlanManager;
    private CraneWorkPhaseTracker selectedWorkPhaseTracker;
    private int currentCraneIndex;
    public CraneUnit CurrentCrane;
''' + method(op, '    private void LateUpdate()') + '\n' + method(op, '    private void UpdateSelectedCraneWeightTargetInformation()') + '''
}
public class CraneInformationDisplay {
    private CraneWorkPhaseTracker workPhaseTracker;
    private CraneWorkLoadPlanManager loadPlanManager;
    private LifMagSystem lifMagSystem;
    private float weightToleranceKg = 1;
    public float CurrentDisplayWeightTon;
    public bool CurrentXHighlighted, CurrentZHighlighted, CurrentWeightHighlighted;
    public int AchievementFrame;
    private Color achievedValueColor = new Color(0,1,0);
    public class InformationTextSet { public Text xText,zText,weightText; }
    private List<InformationTextSet> texts = new List<InformationTextSet>();
    private Dictionary<Text,Color> defaultValueColors = new Dictionary<Text,Color>();
    private IEnumerable<InformationTextSet> GetTextSets() { return texts; }
    private void UpdateCoordinateAchievementState(out bool x,out bool z) { x=z=false; }
''' + '\n'.join(method(info, signature) for signature in [
    '    private void UpdateAchievementColors()',
    '    private bool TryGetCurrentWeightTargetKg(',
    '    private void SetAchievementColor('
]) + '\n}\n')
