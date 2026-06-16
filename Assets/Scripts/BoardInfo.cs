using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardInfo : MonoBehaviour
{
    [Header("Board Size [m]")]
    [SerializeField] private float sizeX;
    [SerializeField] private float sizeY;
    [SerializeField] private float sizeZ;

    [Header("Density [kg/m^3]")]
    [SerializeField] private float density = 7850f;

    public float SizeX => sizeX;
    public float SizeY => sizeY;
    public float SizeZ => sizeZ;
    public float Density => density;

    public float Volume => sizeX * sizeY * sizeZ;
    public float Weight => Volume * density;

    public void SetBoardInfo(float x, float y, float z, float density)
    {
        sizeX = x;
        sizeY = y;
        sizeZ = z;
        this.density = density;
    }
}
