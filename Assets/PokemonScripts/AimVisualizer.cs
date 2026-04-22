using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]

public class AimVisualizer : MonoBehaviour
{
    [Header("線の長さ")]
    public float length = 5f;

    private LineRenderer line;

    private float yaw;
    private float pitch;

    public void SetAim(float newYaw, float newPitch)
    {
        yaw = newYaw;
        pitch = newPitch;
    }

    private void Start()
    {
        line = GetComponent<LineRenderer>();
    }

    private void Update()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 direction = rotation * Vector3.forward;

        line.SetPosition(0, transform.position);
        line.SetPosition(1, transform.position + direction * length);
    }
}