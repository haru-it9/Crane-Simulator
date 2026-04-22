using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PokemonSpawner : MonoBehaviour
{
    [Header("生成するポケモンPrefab一覧")]
    public GameObject[] pokemonPrefabs;

    [Header("生成キー")]
    public KeyCode spawnKey = KeyCode.Space;

    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))
        {
            SpawnPokemon();
        }
    }

    public void SpawnPokemon()
    {
        if (pokemonPrefabs == null || pokemonPrefabs.Length == 0)
        {
            Debug.LogWarning("pokemonPrefabs が設定されていません");
            return;
        }

        // ★ ランダムなポケモンを選びます
        GameObject pokemonPrefab = pokemonPrefabs[Random.Range(0, pokemonPrefabs.Length)];

        // ★ ランダム座標
        float x = Random.Range(-30f, 30f);
        float y = Random.Range(0.5f, 10f);
        float z = Random.Range(5f, 30f);

        Vector3 randomPos = new Vector3(x, y, z);
        Quaternion randomRot = Quaternion.Euler(0f, 180f, 0f);

        // ★ 生成
        GameObject pokemon = Instantiate(pokemonPrefab, randomPos, randomRot);

        Debug.Log("生成位置: " + randomPos);
    }
}
