"""Validate serialized support references and preserve the existing scene hierarchy."""
from pathlib import Path
import re
R=Path(__file__).resolve().parents[2]
s=(R/'Assets/Scenes/RemoteManagementScene(union).unity').read_text()
docs={m[2]:(int(m[1]),m[3]) for m in re.finditer(r'^--- !u!(\d+) &(\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\Z)',s,re.S|re.M)}
assert len(docs)==len(re.findall(r'^--- !u!',s,re.M))
component=docs['9000010090'][1]
for key,id in {'experimentManager':'333508956','operationManager':'1612894383','supportRoot':'9000010000','phaseText':'9000010013','leftXGroup':'9000010021','leftXArrow':'9000010025','rightYGroup':'9000010041','rightYArrow':'9000010045','rightZGroup':'9000010061','rightZArrow':'9000010065'}.items():
    assert f'{key}: {{fileID: {id}}}' in component and id in docs,key
assert 'supportContent: 2' in component
assert float(re.search(r'^  displaySeconds: (.+)$',component,re.M)[1])>0
assert int(re.search(r'^  directionPresentation: (.+)$',component,re.M)[1]) in [0,1]
assert 'fadeOutSeconds: 0.75' in component
assert 'currentHighlightDisplaySeconds: 5' in component
assert 'rightYLabel: {fileID: 9000010049}' in component
for id in ['1470163867','263746697','1055664397','1973951998','1132206959']:
    assert f'  - {{fileID: {id}}}' in component and id in docs
for id in ['9000010026','9000010066']:
    assert 'm_IsActive: 0' in docs[id][1]
for field,value in [('xDistancePerChevron','0.5'),('yDistancePerChevron','0.5'),('zDistancePerChevron','2')]:
    assert f'{field}: {value}' in component
# OnEnable owns initial visibility; preserve the scene author's editor-preview state.
assert re.search(r'^  m_IsActive: [01]$',docs['9000010000'][1],re.M)
assert '  - {fileID: 9000010001}' in docs['1796973733'][1]
for base in [9000010020,9000010040,9000010060]:
    assert 'm_RaycastTarget: 0' in docs[str(base+5)][1]
    assert 'm_Father: {fileID: 9000010001}' in docs[str(base+1)][1]
for name,id in [('TaskSwitchCognitiveSupport','9000010090'),('TaskSwitchSupportArrow','9000010025')]:
    guid=re.search(r'^guid: (\w+)',(R/f'Assets/Scripts/{name}.cs.meta').read_text(),re.M)[1]
    assert f'guid: {guid}' in docs[id][1],name
# Support remains in the same TaskSwitch UI Canvas, not a separately blocking overlay.
assert 'm_Father: {fileID: 1796973733}' in docs['9000010001'][1]
assert 'm_Father: {fileID: 798470288}' in docs['1796973733'][1]
print('Cognitive support scene references passed')
