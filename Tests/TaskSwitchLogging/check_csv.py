import csv
import sys
from pathlib import Path

root = Path(sys.argv[1])
folders = list(root.glob('試行,1_*'))
assert len(folders) == 2, 'same label must create unique session directories'
required = {'session.csv', 'events.csv', 'crane_state.csv', 'input.csv', 'gaze.csv', 'switch_summary.csv', 'cycle_summary.csv'}
all_data = []
for folder in folders:
    assert {p.name for p in folder.iterdir()} == required
    data = {}
    ids = set()
    for path in folder.glob('*.csv'):
        with path.open(encoding='utf-8-sig', newline='') as stream:
            rows = list(csv.reader(stream))
        assert len(set(rows[0])) == len(rows[0]), (path, 'duplicate headers')
        assert all(len(row) == len(rows[0]) for row in rows[1:]), (path, 'row width mismatch')
        data[path.stem] = [dict(zip(rows[0], row)) for row in rows[1:]]
        for row in data[path.stem]:
            ids.add(row['session_id'])
            assert row['participant_id'] == 'P01' and row['block_id'] == 'B01'
            assert int(row['experiment_run_index']) >= 1
    assert len(ids) == 1
    all_data.append(data)
d = next(data for data in all_data if data['crane_state'])
assert len(d['switch_summary']) == 3
complete, incomplete, no_input = d['switch_summary']
assert complete['outcome'] == 'Completed'
assert complete['request_to_suspend_s'] == '1.000000'
assert complete['request_to_target_input_s'] == '4.000000'
assert complete['suspend_to_source_input_s'] == '7.000000'
assert complete['source_confirm_to_unlock_s'] == complete['source_unlock_to_input_s'] == '1.000000'
assert complete['target_completed_s'] == '6.000000', 'duplicate full-cycle completion changed timestamp'
assert complete['weight_invalidations'] == complete['current_drop_events'] == '1'
assert complete['target_input_held_at_unlock'] == '1'
assert incomplete['outcome'].startswith('Incomplete') and incomplete['first_source_input_s'] == ''
assert no_input['outcome'] == 'CompletedWithoutSourceInput' and no_input['experiment_run_index'] == '2'
assert no_input['source_unlock_to_input_s'] == '', 'absent input must never be a zero latency'
events = d['events']
for marker in ['FirstEffectiveTargetInput', 'FirstEffectiveSourceInput']:
    rows = [row for row in events if row['event_type'] == marker]
    assert len(rows) == 1 and rows[0]['input_locked'] == '0'
assert [r for r in events if r['event_type'] == 'FirstEffectiveTargetInput'][0]['real_elapsed_s'] == '5.000000'
for name in ['PickupWeightInvalidated', 'PickupWeightRollback', 'BoardDetachedInsufficientCurrent', 'CycleCompleted']:
    assert any(row['event_type'] == name for row in events), name
for marker, column in [('SwitchRequested', 'request_s'), ('SourceWorkSuspended', 'source_suspended_s'),
                       ('TargetInputUnlocked', 'target_unlock_s'), ('SourceInputUnlocked', 'source_unlock_s'),
                       ('FirstEffectiveSourceInput', 'first_source_input_s')]:
    row = next(r for r in events if r['event_type'] == marker and r['experiment_run_index'] == '1' and r['switch_index'] == '1')
    assert row['real_elapsed_s'] == complete[column], (marker, 'summary must match raw event clock')
for sample in {r['sample_index'] for r in d['crane_state']}:
    state = [r for r in d['crane_state'] if r['sample_index'] == sample]
    assert {r['crane_index'] for r in state} == {'0', '1'}
    inputs = next(r for r in d['input'] if r['sample_index'] == sample)
    gaze = next(r for r in d['gaze'] if r['sample_index'] == sample)
    assert {r['real_elapsed_s'] for r in state} == {inputs['real_elapsed_s'], gaze['real_elapsed_s']}
    assert {r['utc_timestamp'] for r in state} == {inputs['utc_timestamp'], gaze['utc_timestamp']}
    assert all(r['safe_hold_release_threshold_a'] == '70.000000' for r in state)
assert all(r['slider_axis'] == 'JoyStick1LeftSlider' for r in d['input'])
assert any(r['global_paused'] == '1' for r in d['crane_state']), 'pause must remain visible in wall-clock sampling'
assert any(r['is_valid'] == '0' and r['viewport_x'] == '' for r in d['gaze'])
valid = next(r for r in d['gaze'] if r['is_valid'] == '1')
assert float(valid['game_screen_x']) > 1920 and valid['clamped_game_screen_x'] == '1920.000000'
assert valid['aoi'] == 'Unknown'
assert any(r['outcome'] == 'Completed' and r['cycle_number'] == '1' for r in d['cycle_summary'])
assert any(r['outcome'].startswith('Incomplete') for r in d['cycle_summary'])
with (root / 'escaping.csv').open(encoding='utf-8-sig', newline='') as stream:
    row = list(csv.reader(stream))[1]
assert row == ['参加者,"名前"\n改行', '1.250000', '', '1']
print('PASS: parsed all seven CSV schemas, shared clocks/IDs, exact summary intervals, blank missing values, two cranes, pauses, valid/invalid gaze, unique folders')

# Rebuild derived summaries from raw records and compare every typed field.
import importlib.util
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('rebuild', Path(__file__).parents[2] / 'Tools/rebuild_task_switch_summaries.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
folder = next(folder for folder, data in zip(folders, all_data) if data['crane_state'])
rebuilt_switches, rebuilt_cycles = module.rebuild(folder)
for expected_rows, rebuilt_rows in [(d['switch_summary'], rebuilt_switches), (d['cycle_summary'], rebuilt_cycles)]:
    assert len(expected_rows) == len(rebuilt_rows)
    for expected, actual in zip(expected_rows, rebuilt_rows):
        for key, value in expected.items():
            assert key in actual, (key, 'missing rebuilt column')
            try:
                match = abs(float(value) - float(actual[key])) < 0.00001
            except (ValueError, TypeError):
                match = value == actual[key]
            assert match, (key, value, actual[key])
print('PASS: switch and cycle summaries rebuilt from raw events/state match every exported field')
