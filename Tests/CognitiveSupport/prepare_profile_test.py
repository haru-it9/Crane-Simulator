"""Use production raising-step lookup with the support controller test surface."""
from pathlib import Path
import sys
import re

root = Path(__file__).resolve().parents[2]
source = (root / 'Assets/Scripts/CraneWorkPhaseTracker.cs').read_text()
methods = []
for name in ['GetRaisingStepDefinition', 'HasRaisingHeightCondition', 'IsSupportWeightWithinTarget']:
    start = re.search(r'^    (?:public|private).* ' + name + r'\(', source, re.M).start()
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    methods.append(source[start:end])
Path(sys.argv[1], 'ProfileLookupProduction.cs').write_text(
    'using UnityEngine; public partial class CraneWorkPhaseTracker {\n'
    'private CraneWorkPhaseProfile phaseProfile = null;\n'
    'private CraneWorkLoadPlanManager loadPlanManager = null; private CraneInstance craneInstance = null;\n'
    + '\n'.join(methods) + '\n}\n')
