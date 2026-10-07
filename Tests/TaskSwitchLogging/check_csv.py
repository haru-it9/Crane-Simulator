import csv
import sys
from pathlib import Path

root = Path(sys.argv[1])
folders = list(root.glob('試行,1_*'))
assert len(folders) == 2, 'same label must create unique session directories'
required = {'session.csv', 'events.csv', 'crane_state.csv', 'input.csv', 'gaze.csv', 'switch_summary.csv', 'cycle_summary.csv', 'auditory_subtask.csv', 'auditory_trials.csv', 'pause_intervals.csv'}
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
assert not d['auditory_subtask'] and not d['auditory_trials'], 'disabled subtask must produce no trials'
assert all(r['schema_version'] == '4' and r['auditory_subtask_enabled'] == '0' for r in d['session'])
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
print('PASS: parsed all ten CSV schemas, disabled subtask, shared clocks/IDs, exact summary intervals, blank missing values, two cranes, pauses, valid/invalid gaze, unique folders')

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

# Real production auditory scheduler and logger are exercised by AuditorySubtaskTests.
auditory_folder, = root.glob('auditory-test_*')
auditory_data = {}
for path in auditory_folder.glob('*.csv'):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        rows = list(csv.reader(stream))
    assert len(set(rows[0])) == len(rows[0]) and all(len(r) == len(rows[0]) for r in rows[1:]), path
    auditory_data[path.stem] = [dict(zip(rows[0], r)) for r in rows[1:]]
    assert all(r['participant_id'] == 'P02' and r['block_id'] == 'auditory' for r in auditory_data[path.stem])
assert len({r['session_id'] for data in auditory_data.values() for r in data}) == 1
trials = auditory_data['auditory_trials']
history = auditory_data['auditory_subtask']
assert [r['trial_index'] for r in trials] == ['1', '2', '3', '4']
assert [r['outcome'] for r in trials] == ['Correct', 'Correct', 'Miss', 'CancelledBeforeOnset']
assert all(r['event_type'] == 'TrialFinished' and r['random_seed'] == '17' for r in trials)
assert trials == [r for r in history if r['event_type'] == 'TrialFinished'], 'summary differs from original event'
correct = trials[0]
assert correct['correct'] == '1' and correct['reaction_time_s'] == '0.150000' and correct['held_at_onset'] == '0'
assert correct['input_locked'] == '1', 'main operation lock must not gate auditory pedal input'
assert correct['onset_state'] == 'OperatingSource' and correct['state'] == 'OperatingTarget'
assert correct['onset_crane_index'] == '0' and correct['active_crane_index'] == '1'
assert correct['onset_switch_index'] == '0' and correct['switch_index'] == '1'
assert abs(float(correct['onset_observed_real_s']) - float(correct['onset_estimated_real_s']) - .02) < .00001
assert correct['onset_observation_lag_s'] == '0.020000'
assert abs(float(correct['response_observed_real_s']) - float(correct['onset_estimated_real_s']) - .15) < .00001
resumed = trials[1]
assert resumed['reaction_time_s'] == '0.520000' and resumed['wall_reaction_time_s'] == '20.520000'
assert resumed['pause_count'] == '1' and resumed['paused_duration_s'] == '20.000000'
assert all(r['reaction_time_s'] == '' and r['correct'] == '' and r['response_observed_real_s'] == '' for r in trials[2:])
assert trials[-1]['presented'] == '0' and trials[-1]['onset_observed_real_s'] == ''
assert trials[-1]['detail'] == 'LoggingStopped'
assert any(r['event_type'] == 'SubtaskPaused' for r in history) and any(r['event_type'] == 'SubtaskResumed' for r in history)
for event in (r for r in history if r['event_type'] == 'PedalPressed'):
    assert event['real_elapsed_s'] == event['response_observed_real_s'], 'pedal input has a different common clock'
disabled_folder, = root.glob('auditory-disabled_*')
for name in ['auditory_trials', 'auditory_subtask']:
    with (disabled_folder / (name + '.csv')).open(encoding='utf-8-sig', newline='') as stream:
        assert len(list(csv.reader(stream))) == 1, 'disabled feature recorded secondary-task events'
print('PASS: auditory trial/event CSV widths, matching summaries, missing values, precise shared clock, onset/response switch context, pause and recording-stop outcomes')

pause_folder, = root.glob('pause-test_*')
pause_data = {}
for path in pause_folder.glob('*.csv'):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        rows = list(csv.reader(stream))
    assert all(len(r) == len(rows[0]) for r in rows[1:]), path
    pause_data[path.stem] = [dict(zip(rows[0], r)) for r in rows[1:]]
intervals = pause_data['pause_intervals']
assert len(intervals) == 2
assert intervals[0]['start_s'] == '1.000000' and intervals[0]['end_s'] == '8.000000' and intervals[0]['duration_s'] == '7.000000'
assert intervals[0]['start_observed'] == intervals[0]['end_observed'] == '1' and intervals[0]['outcome'] == 'Resumed'
assert intervals[1]['start_s'] == '10.000000' and intervals[1]['end_s'] == '12.000000' and intervals[1]['duration_s'] == '2.000000'
assert intervals[1]['end_observed'] == '0' and intervals[1]['outcome'] == 'LoggingStoppedWhilePaused'
assert all(r['start_state'] == r['end_state'] == 'OperatingTarget' and r['start_crane_index'] == r['end_crane_index'] == '1' for r in intervals)
for table in ['crane_state', 'input', 'gaze']:
    paused = [r for r in pause_data[table] if r['global_paused'] == '1']
    assert paused and all(r['pause_interval_index'] in {'1', '2'} and r['state'] == 'OperatingTarget' for r in paused)
    assert len({r['simulation_elapsed_s'] for r in paused}) == 1, 'simulation clock changed during pause'
markers = [r for r in pause_data['events'] if r['event_type'].startswith('GlobalPause')]
assert [(r['event_type'], r['global_paused'], r['real_elapsed_s']) for r in markers] == [
    ('GlobalPauseStarted', '1', '1.000000'), ('GlobalPauseEnded', '0', '8.000000'), ('GlobalPauseStarted', '1', '10.000000')]
partial_folder, = root.glob('pause-partial-start_*')
with (partial_folder / 'pause_intervals.csv').open(encoding='utf-8-sig', newline='') as stream:
    partial, = csv.DictReader(stream)
assert partial['start_observed'] == '0' and partial['end_observed'] == '1' and partial['duration_s'] == '3.000000'
print('PASS: exact pause/resume event timestamps, paused samples with interval IDs, target state retention, seven-second interval, partial start and recording stop during pause')
