using UnityEngine;

public struct TaskSwitchPedalSample
{
    public float AxisRaw, Value;
    public bool PositiveKey, NegativeKey;
    public bool ConflictingKeys => PositiveKey && NegativeKey;
    public string Source => ConflictingKeys ? "ConflictingKeys" : PositiveKey || NegativeKey ? "PlusMinusKeys" : "Axis";
}

public static class TaskSwitchPedalInput
{
    // Many USB foot switches emulate keys rather than joystick axes. Keep the configured axis too.
    // Unity 2022 physical-key mode reports the main '+' key as Equals, not KeyCode.Plus.
    public static TaskSwitchPedalSample Read(string axis, bool usePlusMinusKeys, KeyCode positiveKey, KeyCode negativeKey)
    {
        var sample = new TaskSwitchPedalSample { AxisRaw = Input.GetAxisRaw(axis) };
        if (usePlusMinusKeys)
        {
            sample.PositiveKey = (positiveKey != KeyCode.None && Input.GetKey(positiveKey)) || Input.GetKey(KeyCode.KeypadPlus);
            sample.NegativeKey = (negativeKey != KeyCode.None && Input.GetKey(negativeKey)) || Input.GetKey(KeyCode.KeypadMinus);
        }
        // Simultaneous presses must neither count as a response nor rearm the neutral-input gate.
        sample.Value = sample.ConflictingKeys ? float.NaN : sample.PositiveKey ? 1f : sample.NegativeKey ? -1f : sample.AxisRaw;
        return sample;
    }
}
