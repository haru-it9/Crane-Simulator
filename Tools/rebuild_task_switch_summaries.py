#!/usr/bin/env python3
"""Rebuild schema-v2 derived summaries from events.csv and crane_state.csv.
Writes *_rebuilt.csv; the original experiment records are never overwritten.
Usage: python3 Tools/rebuild_task_switch_summaries.py /path/to/session
"""
import argparse
import csv
from pathlib import Path

TIMING_MARKERS = [
    ('request_s', 'SwitchRequested'), ('source_suspended_s', 'SourceWorkSuspended'),
    ('target_display_s', 'TargetDisplaySwitched'), ('target_confirm_s', 'ConfirmationPressed'),
    ('target_unlock_s', 'TargetInputUnlocked'), ('first_target_input_s', 'FirstEffectiveTargetInput'),
    ('target_completed_s', 'TargetCompleted'), ('source_display_s', 'SourceDisplayRestored'),
    ('source_confirm_s', 'SourceReturnConfirmationPressed'), ('source_unlock_s', 'SourceInputUnlocked'),
    ('first_source_input_s', 'FirstEffectiveSourceInput'), ('logical_completion_s', 'SwitchCompleted')]
INTERVALS = [
    ('request_to_suspend_s', 'SwitchRequested', 'SourceWorkSuspended'),
    ('request_to_target_input_s', 'SwitchRequested', 'FirstEffectiveTargetInput'),
    ('suspend_to_source_input_s', 'SourceWorkSuspended', 'FirstEffectiveSourceInput'),
    ('source_display_to_confirm_s', 'SourceDisplayRestored', 'SourceReturnConfirmationPressed'),
    ('source_confirm_to_unlock_s', 'SourceReturnConfirmationPressed', 'SourceInputUnlocked'),
    ('source_unlock_to_input_s', 'SourceInputUnlocked', 'FirstEffectiveSourceInput'),
    ('target_confirm_to_unlock_s', 'ConfirmationPressed', 'TargetInputUnlocked'),
    ('target_unlock_to_input_s', 'TargetInputUnlocked', 'FirstEffectiveTargetInput')]
TARGET_COMPLETION = {'TargetMajorPhaseCompleted', 'TargetWorkCycleCompleted',
                     'TargetWorkCycleCompletedByPhase', 'TargetWorkCycleCompletionFallback'}
IDENTITY = ['session_id', 'participant_id', 'block_id', 'experiment_run_index']


def read(path):
    with path.open(encoding='utf-8-sig', newline='') as f:
        reader = csv.DictReader(f)
        rows = list(reader)
        if any(None in row for row in rows):
            raise ValueError(f'Invalid CSV row width: {path}')
        return reader.fieldnames, rows


def save(path, header, rows):
    with path.open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=header, extrasaction='ignore')
        writer.writeheader()
        writer.writerows(rows)


def number(value):
    return f'{value:.6f}'


def rebuild(folder):
    switch_header, _ = read(folder / 'switch_summary.csv')
    cycle_header, _ = read(folder / 'cycle_summary.csv')
    _, events = read(folder / 'events.csv')
    _, states = read(folder / 'crane_state.csv')
    switches, cycle_rows, active_cycles = [], [], {}
    pending = None

    def finish_switch(reason):
        nonlocal pending
        if pending is None:
            return
        row, markers = pending
        logical = 'SwitchCompleted' in markers
        source = 'FirstEffectiveSourceInput' in markers
        row['logical_completed'], row['source_input_observed'] = str(int(logical)), str(int(source))
        row['outcome'] = ('Completed' if source else 'CompletedWithoutSourceInput') if logical else f'Incomplete:{reason}'
        for column, marker in TIMING_MARKERS:
            row[column] = number(markers[marker]) if marker in markers else ''
        for column, start, end in INTERVALS:
            row[column] = number(markers[end] - markers[start]) if start in markers and end in markers and markers[end] >= markers[start] else ''
        switches.append(row)
        pending = None

    for event in events:
        name, run = event['event_type'], event['experiment_run_index']
        if pending and run != pending[0]['experiment_run_index']:
            finish_switch('ExperimentRestarted')
        if name == 'SwitchRequested':
            finish_switch('NextSwitchRequested')
            row = {key: event[key] for key in IDENTITY}
            row.update(switch_index=event['switch_index'], switch_method=event['switch_method'],
                       source_phase_at_suspend='', source_step_at_suspend='', source_cycle_at_suspend='0',
                       step_elapsed_at_suspend_s='', position_latched_at_suspend='0', board_count_at_suspend='0',
                       target_input_held_at_unlock='0', source_input_held_at_unlock='0', weight_invalidations='0', current_drop_events='0')
            pending = row, {}
        if pending:
            row, markers = pending
            markers.setdefault(name, float(event['real_elapsed_s']))
            if name in TARGET_COMPLETION:
                markers.setdefault('TargetCompleted', float(event['real_elapsed_s']))
            if name == 'SourceWorkSuspended':
                row.update(source_phase_at_suspend=event['event_phase'], source_step_at_suspend=event['event_step'],
                           source_cycle_at_suspend=event['source_cycle_number'], step_elapsed_at_suspend_s=event['step_elapsed_s'],
                           position_latched_at_suspend=event['position_condition_latched'], board_count_at_suspend=event['attached_board_count'] or '0')
            for marker, column in [('TargetInputUnlocked', 'target_input_held_at_unlock'), ('SourceInputUnlocked', 'source_input_held_at_unlock')]:
                if name == marker:
                    row[column] = event['operation_input_held']
            for marker, column in [('PickupWeightInvalidated', 'weight_invalidations'), ('BoardDetachedInsufficientCurrent', 'current_drop_events')]:
                if name == marker:
                    row[column] = str(int(row[column]) + 1)
            if 'SwitchCompleted' in markers and 'FirstEffectiveSourceInput' in markers:
                finish_switch('SourceInput')
            elif name == 'LoggingStopped':
                finish_switch('LoggingStopped')
        key = run, event['cycle_instance_id']
        if name == 'CycleStarted' and key[1]:
            row = {column: event[column] for column in IDENTITY}
            row.update(cycle_instance_id=key[1], crane_index=event['event_crane_index'], cycle_number=event['event_cycle_number'],
                       switch_index_at_start=event['switch_index'], first_phase=event['event_phase'], start_s=event['real_elapsed_s'],
                       steps_completed='0', weight_invalidations='0', current_drop_events='0', rollbacks='0')
            active_cycles[key] = row
        if key in active_cycles:
            row = active_cycles[key]
            for marker, column in [('StepCompleted', 'steps_completed'), ('PickupWeightInvalidated', 'weight_invalidations'),
                                   ('BoardDetachedInsufficientCurrent', 'current_drop_events'), ('PickupWeightRollback', 'rollbacks')]:
                if name == marker:
                    row[column] = str(int(row[column]) + 1)
            if name == 'CycleSummaryClosed':
                row.update(outcome=event['detail'], end_s=event['real_elapsed_s'], elapsed_s=number(float(event['real_elapsed_s']) - float(row['start_s'])))
                cycle_rows.append(active_cycles.pop(key))
    finish_switch('LoggingStopped')
    by_cycle = {(r['experiment_run_index'], r['cycle_instance_id']): r for r in cycle_rows}
    sums = {key: [0., 0., 0., float(row['start_s'])] for key, row in by_cycle.items()}
    previous = {}
    for state in states:
        time = float(state['real_elapsed_s'])
        crane = state['crane_index']
        prior = previous.get(crane, 0.)
        previous[crane] = time
        key = state['experiment_run_index'], state['cycle_instance_id']
        if key not in by_cycle:
            continue
        row = by_cycle[key]
        dt = max(0., time - max(prior, float(row['start_s'])))
        values = sums[key]
        if state['global_paused'] == '1':
            values[2] += dt
        elif all(state[c] == '1' for c in ['monitoring', 'cycle_running']) and all(state[c] == '0' for c in ['cycle_paused', 'boundary_waiting']):
            values[0] += dt
            if state['input_locked'] == '0' and state['operation_enabled'] == '1' and crane == state['active_crane_index']:
                values[1] += dt
        values[3] = time
    for key, row in by_cycle.items():
        row.update(zip(['monitoring_s', 'control_available_s', 'global_pause_s', 'duration_sampled_until_s'], map(number, sums[key])))
    save(folder / 'switch_summary_rebuilt.csv', switch_header, switches)
    save(folder / 'cycle_summary_rebuilt.csv', cycle_header, cycle_rows)
    return switches, cycle_rows


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('session_directory', type=Path)
    args = parser.parse_args()
    switches, cycles = rebuild(args.session_directory)
    print(f'Rebuilt {len(switches)} switches and {len(cycles)} cycles in {args.session_directory}')
