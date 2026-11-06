using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// ネットワークプレイヤーのスポーンを管理するクラス
/// NetworkManagerのPlayerPrefabを使用する代わりに、より細かい制御を提供
///
/// 【使い方】
/// 1. シーンに空のGameObjectを配置（名前例：SpawnPoint_1, SpawnPoint_2）
/// 2. このスクリプトのSpawn Pointsにドラッグ＆ドロップで登録
/// 3. プレイヤーが接続すると、登録したスポーンポイントの位置・回転でスポーン
/// </summary>
public class NetworkPlayerSpawner : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("プレイヤープレハブ（NetworkObjectが必要）")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("スポーンポイント（空のGameObjectを配置して登録）")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("スポーンポイントをランダムに選択")]
    [SerializeField] private bool randomSpawnPoint = false;

    [Tooltip("スポーンポイントがない場合のオフセット距離")]
    [SerializeField] private float spawnOffset = 2f;

    [Header("Gizmo Settings")]
    [Tooltip("シーンビューでスポーンポイントを表示")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color gizmoColor = Color.green;
    [SerializeField] private float gizmoSize = 0.5f;

    private int nextSpawnIndex = 0;
    private readonly Dictionary<ulong, int> assignedSpawns = new Dictionary<ulong, int>();
    private NetworkManager subscribedManager;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // クライアント接続時のコールバックを登録
            assignedSpawns.Clear();
            nextSpawnIndex = 0;
            subscribedManager = NetworkManager;
            subscribedManager.OnClientConnectedCallback += OnClientConnected;
            subscribedManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnClientConnectedCallback -= OnClientConnected;
            subscribedManager.OnClientDisconnectCallback -= OnClientDisconnected;
            subscribedManager = null;
        }
        assignedSpawns.Clear();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        assignedSpawns.Remove(clientId);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        if (assignedSpawns.ContainsKey(clientId)) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null) return;
        SpawnPlayerForClient(clientId);
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[NetworkPlayerSpawner] Player prefab is not assigned!");
            return;
        }

        // スポーン位置と回転を取得
        int spawnIndex = GetNextSpawnIndex();
        Vector3 spawnPosition = GetSpawnPosition(spawnIndex);
        Quaternion spawnRotation = GetSpawnRotation(spawnIndex);

        GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

        if (networkObject != null)
        {
            networkObject.SpawnAsPlayerObject(clientId);
            assignedSpawns[clientId] = spawnIndex;
            Debug.Log($"[NetworkPlayerSpawner] Spawned player for client {clientId} at {spawnPosition} (SpawnPoint index: {spawnIndex})");
        }
        else
        {
            Debug.LogError("[NetworkPlayerSpawner] Player prefab does not have a NetworkObject component!");
            Destroy(playerInstance);
        }
    }

    private int GetNextSpawnIndex()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return nextSpawnIndex++;
        }

        var available = new List<int>();
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            int candidate = (nextSpawnIndex + i) % spawnPoints.Length;
            if (!assignedSpawns.ContainsValue(candidate)) available.Add(candidate);
        }
        if (available.Count == 0)
        {
            Debug.LogWarning("[NetworkPlayerSpawner] No free spawn point. Add a point for each simultaneous player.");
            return nextSpawnIndex++ % spawnPoints.Length;
        }
        int index = randomSpawnPoint ? available[Random.Range(0, available.Count)] : available[0];
        nextSpawnIndex = (index + 1) % spawnPoints.Length;
        return index;
    }

    private Vector3 GetSpawnPosition(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            // スポーンポイントがない場合は、オフセットを使用
            Vector3 offset = new Vector3(index * spawnOffset, 0, 0);
            return transform.position + offset;
        }

        // 有効なスポーンポイントか確認
        if (spawnPoints[index] != null)
        {
            return spawnPoints[index].position;
        }

        Debug.LogWarning($"[NetworkPlayerSpawner] SpawnPoint[{index}] is null, using default position");
        return transform.position;
    }

    private Quaternion GetSpawnRotation(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return Quaternion.identity;
        }

        // 有効なスポーンポイントか確認
        if (spawnPoints[index] != null)
        {
            return spawnPoints[index].rotation;
        }

        return Quaternion.identity;
    }

    /// <summary>
    /// シーンビューでスポーンポイントを視覚化
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.color = gizmoColor;

        // スポーンポイントがある場合は表示
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] != null)
                {
                    // スポーンポイントの位置に球を描画
                    Gizmos.DrawWireSphere(spawnPoints[i].position, gizmoSize);

                    // 向きを示す矢印（前方向）
                    Vector3 forward = spawnPoints[i].forward * gizmoSize * 2f;
                    Gizmos.DrawRay(spawnPoints[i].position, forward);

                    // 番号を表示するためのラベル位置
                    #if UNITY_EDITOR
                    UnityEditor.Handles.Label(spawnPoints[i].position + Vector3.up * gizmoSize * 1.5f, $"Spawn {i + 1}");
                    #endif
                }
            }
        }
        else
        {
            // スポーンポイントがない場合は、デフォルトのスポーン位置を表示
            for (int i = 0; i < 4; i++)
            {
                Vector3 pos = transform.position + new Vector3(i * spawnOffset, 0, 0);
                Gizmos.DrawWireSphere(pos, gizmoSize * 0.5f);
            }
        }
    }
}
