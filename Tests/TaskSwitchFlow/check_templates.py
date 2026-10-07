import csv
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
work_path = root / 'Assets/TaskSwitchWorkConditionsTemplate.csv'
work = list(csv.DictReader(work_path.open()))
assert [int(r['index']) for r in work if r['role'] == 'Source'] == [1, 2, 3, 4, 5]
targets = {int(r['index']) for r in work if r['role'] == 'Target'}
for path in [root / 'Assets/TaskSwitchScheduleTemplate.csv', *root.glob('Assets/Input CSV/TaskSwitchSchedulePattern*.csv')]:
    rows = list(csv.DictReader(path.open()))
    assert [int(r['switchIndex']) for r in rows] == list(range(1, len(rows) + 1))
    for row in rows:
        assert row['targetTaskPattern'] in {'Move1ToLiftUp', 'Move2ToPlace'}
        assert int(row['switchIndex']) in targets
        assert 1 <= int(row['sourceCycle']) <= 5
scene = (root / 'Assets/Scenes/RemoteManagementScene(union).unity').read_text()
guid = re.search(r'^guid: (\w+)', Path(str(work_path) + '.meta').read_text(), re.M)[1]
assert f'workConditionsCsv: {{fileID: 4900000, guid: {guid}, type: 3}}' in scene
assert 'sourceTotalCycleCount: 5' in scene and 'targetTaskPattern: 0' in scene
assert 'targetRunsFullCycle:' not in scene
print('PASS: schedule templates, all eight Target and five Source condition rows, and scene assignment')
