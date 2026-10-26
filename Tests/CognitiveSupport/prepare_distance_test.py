"""Compile the actual read-only distance probes and their collider filters outside Unity."""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
source = (root / 'Assets/Scripts/CraneUnit.cs').read_text()
names = ['TryGetSupportRaisingDistance', 'TryGetSupportLoweringDistance',
         'TryGetLowestAttachedBoardCollider', 'IsColliderPartOfAttachedBoard', 'HasTagOnSelfOrParent']
methods = []
for name in names:
    token = 'bool ' + name + '('
    start = source.rfind('\n', 0, source.index(token)) + 1
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    methods.append(source[start:end])
Path(sys.argv[1], 'DistanceProduction.cs').write_text(
    'using UnityEngine; public partial class CraneUnit {\n' + '\n'.join(methods) + '\n}\n')
