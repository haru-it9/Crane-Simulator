using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PokemonSpawner : MonoBehaviour
{
[System.Serializable]
    public class DistancePokemonSet
    {
        [Header("このグループで使う距離候補")]
        public float[] distanceOptions;

        [Header("この距離で出現するポケモンPrefab一覧")]
        public GameObject[] pokemonPrefabs;
    }

    [Header("基準となる射出位置")]
    public Transform shootPoint;

    [Header("生成キー")]
    public KeyCode spawnKey = KeyCode.T;

    [Header("水平角候補（度）")]
    public float[] yawOptions;

    [Header("垂直角候補（度）")]
    public float[] pitchOptions;

    [Header("距離ごとの出現設定")]
    public DistancePokemonSet[] distancePokemonSets;

    [Header("生成時のY回転")]
    public float spawnYRotation = 180f;

    [Header("前のポケモンを消してから生成する")]
    public bool destroyPreviousPokemon = true;

    private GameObject currentPokemon;

    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))
        {
            SpawnPokemon();
        }
    }

    public void SpawnPokemon()
    {
        if (shootPoint == null)
        {
            Debug.LogWarning("shootPoint が設定されていません");
            return;
        }

        if (yawOptions == null || yawOptions.Length == 0)
        {
            Debug.LogWarning("yawOptions が設定されていません");
            return;
        }

        if (pitchOptions == null || pitchOptions.Length == 0)
        {
            Debug.LogWarning("pitchOptions が設定されていません");
            return;
        }

        if (distancePokemonSets == null || distancePokemonSets.Length == 0)
        {
            Debug.LogWarning("distancePokemonSets が設定されていません");
            return;
        }

        // 距離グループをランダム選択
        DistancePokemonSet selectedSet = distancePokemonSets[Random.Range(0, distancePokemonSets.Length)];

        if (selectedSet.distanceOptions == null || selectedSet.distanceOptions.Length == 0)
        {
            Debug.LogWarning("distanceOptions が空のグループがあります");
            return;
        }

        if (selectedSet.pokemonPrefabs == null || selectedSet.pokemonPrefabs.Length == 0)
        {
            Debug.LogWarning("pokemonPrefabs が空のグループがあります");
            return;
        }

        // 各候補値をランダム選択
        float distance = selectedSet.distanceOptions[Random.Range(0, selectedSet.distanceOptions.Length)];
        float yaw = yawOptions[Random.Range(0, yawOptions.Length)];
        float pitch = pitchOptions[Random.Range(0, pitchOptions.Length)];

        // 射出位置からの極座標的な方向計算
        Quaternion offsetRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 direction = (shootPoint.rotation * offsetRotation) * Vector3.forward;

        Vector3 spawnPosition = shootPoint.position + direction * distance;

        // 距離に応じたポケモン候補からランダム選択
        GameObject pokemonPrefab = selectedSet.pokemonPrefabs[Random.Range(0, selectedSet.pokemonPrefabs.Length)];

        // 前回のポケモンを消す
        if (destroyPreviousPokemon && currentPokemon != null)
        {
            Destroy(currentPokemon);
        }

        // shootPoint を向く回転
        Quaternion lookRot = Quaternion.LookRotation(shootPoint.position - spawnPosition);

        // 0度時にY180°になるよう補正
        Quaternion spawnRotation = lookRot * Quaternion.Euler(0f, 0f, 0f);

        currentPokemon = Instantiate(pokemonPrefab, spawnPosition, spawnRotation);

        Debug.Log(
            $"生成ポケモン: {pokemonPrefab.name} / 距離: {distance} / yaw: {yaw} / pitch: {pitch} / 位置: {spawnPosition}"
        );
    }
}
