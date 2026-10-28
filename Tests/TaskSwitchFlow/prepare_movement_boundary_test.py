"""Keep real movement dispatch and input mapping alongside the full production tracker."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
source = (root / 'Assets/Scripts/CraneOperationManager.cs').read_text()
methods = []
for name in ['ReadMovementCommand', 'HandleMovement', 'GetMainLifMagXInput',
             'GetMainLifMagYInput', 'GetMainCraneZInput', 'ApplyDeadZone']:
    start = re.search(r'^    (?:public|private).* ' + name + r'\(', source, re.M).start()
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    methods.append(source[start:end])
for name in ['JoyStick2HorizontalAxis', 'JoyStick2VerticalAxis', 'JoyStick3VerticalAxis']:
    start = source.index('    private string ' + name)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    methods.append(source[start:end])
Path(sys.argv[1], 'MovementDispatchProduction.cs').write_text(
    'using UnityEngine; public partial class CraneOperationManager {\n' + '\n'.join(methods) + '\n}\n')
