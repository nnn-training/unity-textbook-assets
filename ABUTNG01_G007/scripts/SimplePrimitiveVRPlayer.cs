using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 【教育資料用】シンプルなPrimitiveアバターのネットワーク同期スクリプト
/// Cube/Sphereで頭と両手を表現し、VRトラッキングの動きをネットワーク同期
///
/// 学習ポイント：
/// 1. NetworkBehaviour の継承
/// 2. NetworkVariable による位置・回転の同期
/// 3. IsOwner による ローカル/リモート の処理分岐
/// 4. OnNetworkSpawn での初期化処理
/// </summary>
public class SimplePrimitiveVRPlayer : NetworkBehaviour
{
    [Header("=== Primitive Avatar Parts ===")]
    [Tooltip("頭を表すPrimitive（Cube/Sphere）")]
    [SerializeField] private Transform headPrimitive;
    [Tooltip("左手を表すPrimitive（Cube/Sphere）")]
    [SerializeField] private Transform leftHandPrimitive;
    [Tooltip("右手を表すPrimitive（Cube/Sphere）")]
    [SerializeField] private Transform rightHandPrimitive;

    [Header("=== VR Tracking Sources (Local Only) ===")]
    [Tooltip("VRカメラ（頭のトラッキング元）")]
    [SerializeField] private Transform vrHead;
    [Tooltip("左コントローラー（左手のトラッキング元）")]
    [SerializeField] private Transform vrLeftHand;
    [Tooltip("右コントローラー（右手のトラッキング元）")]
    [SerializeField] private Transform vrRightHand;

    [Header("=== Sync Settings ===")]
    [Tooltip("位置の補間速度（リモートプレイヤー用）")]
    [SerializeField] private float lerpSpeed = 15f;

    [Header("=== Visual Settings ===")]
    [Tooltip("ローカルプレイヤーの色")]
    [SerializeField] private Color localPlayerColor = Color.blue;
    [Tooltip("リモートプレイヤーの色")]
    [SerializeField] private Color remotePlayerColor = Color.green;

    // =========================================
    // NetworkVariable - ネットワーク同期される変数
    // =========================================
    // 重要: NetworkVariableWritePermission.Owner により、
    // 所有者（ローカルプレイヤー）のみが値を書き込める

    // 頭の位置・回転
    private NetworkVariable<Vector3> netHeadPos = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> netHeadRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 左手の位置・回転
    private NetworkVariable<Vector3> netLeftHandPos = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> netLeftHandRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 右手の位置・回転
    private NetworkVariable<Vector3> netRightHandPos = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> netRightHandRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>
    /// ネットワークに参加した時に呼ばれる
    /// ローカルプレイヤーとリモートプレイヤーで初期化処理を分岐
    /// </summary>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        rigMovedToSpawn = false;

        if (IsOwner)
        {
            // ★ ローカルプレイヤー（自分自身）の場合
            Debug.Log($"[SimplePrimitiveVRPlayer] ローカルプレイヤーがスポーン ClientId: {OwnerClientId}");
            SetupAsLocalPlayer();
        }
        else
        {
            // ★ リモートプレイヤー（他のプレイヤー）の場合
            Debug.Log($"[SimplePrimitiveVRPlayer] リモートプレイヤーがスポーン ClientId: {OwnerClientId}");
            SetupAsRemotePlayer();
        }
    }

    /// <summary>
    /// ローカルプレイヤーとしてセットアップ
    /// </summary>
    private void SetupAsLocalPlayer()
    {
        // ローカルプレイヤーの色を設定
        SetAvatarColor(localPlayerColor);

        // VRトラッキング参照がない場合は、XR Originから自動取得を試みる
        TryFindVRReferences();
    }

    // VR参照が見つかったかどうか
    private bool vrReferencesFound = false;
    private bool rigMovedToSpawn = false;

    /// <summary>
    /// リモートプレイヤーとしてセットアップ
    /// </summary>
    private void SetupAsRemotePlayer()
    {
        // リモートプレイヤーの色を設定
        SetAvatarColor(remotePlayerColor);

        // リモートプレイヤーはVRトラッキング参照は不要（ネットワーク同期データを使用）
        // VR関連のコンポーネントがあれば無効化
        var cameras = GetComponentsInChildren<Camera>();
        foreach (var cam in cameras)
        {
            cam.enabled = false;
        }

        var audioListeners = GetComponentsInChildren<AudioListener>();
        foreach (var listener in audioListeners)
        {
            listener.enabled = false;
        }
    }

    /// <summary>
    /// XR Originからカメラとコントローラーの参照を自動取得
    /// </summary>
    private void TryFindVRReferences()
    {
        // シーン内のXR Originを探す
        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            // スポーン位置はアバターの親だけでなく、ローカルXR Originへ反映します。
            // 頭と手はワールド座標を送るため、この移動を最初の送信前に1回行います。
            if (!rigMovedToSpawn)
            {
                xrOrigin.transform.SetPositionAndRotation(transform.position, transform.rotation);
                rigMovedToSpawn = true;
            }
            vrHead = xrOrigin.Camera?.transform;

            // XR Interaction Toolkit 3.x対応: 名前でコントローラーを検索
            // "Left Controller" と "Right Controller" を探す
            var allTransforms = xrOrigin.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                // 完全一致または部分一致で検索
                if (t.name == "Left Controller" || t.name == "LeftHand Controller")
                {
                    vrLeftHand = t;
                }
                else if (t.name == "Right Controller" || t.name == "RightHand Controller")
                {
                    vrRightHand = t;
                }
            }

            // 全て見つかったかチェック
            vrReferencesFound = (vrHead != null && vrLeftHand != null && vrRightHand != null);

            Debug.Log($"[SimplePrimitiveVRPlayer] VR参照を自動取得: Head={vrHead != null}, LeftHand={vrLeftHand != null}, RightHand={vrRightHand != null}");

            // 見つからなかった場合の詳細ログ
            if (!vrReferencesFound)
            {
                Debug.LogWarning("[SimplePrimitiveVRPlayer] 一部のコントローラーが見つかりません。XR Origin内のオブジェクト一覧:");
                foreach (var t in allTransforms)
                {
                    if (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))
                    {
                        Debug.Log($"  - {t.name}");
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("[SimplePrimitiveVRPlayer] XR Originが見つかりません。Inspector でVR参照を手動設定してください。");
        }
    }

    /// <summary>
    /// アバターのPrimitive全体の色を変更
    /// </summary>
    private void SetAvatarColor(Color color)
    {
        SetPrimitiveColor(headPrimitive, color);
        SetPrimitiveColor(leftHandPrimitive, color);
        SetPrimitiveColor(rightHandPrimitive, color);
    }

    private void SetPrimitiveColor(Transform primitive, Color color)
    {
        if (primitive == null) return;

        var renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            // マテリアルのインスタンスを作成して色を変更
            renderer.material = new Material(renderer.material);
            renderer.material.color = color;
        }
    }

    /// <summary>
    /// 毎フレーム呼ばれる更新処理
    /// </summary>
    private void Update()
    {
        // ネットワークにスポーンされていない場合は何もしない
        if (!IsSpawned) return;

        if (IsOwner)
        {
            // VR参照がまだ見つかっていない場合は再試行
            if (!vrReferencesFound)
            {
                TryFindVRReferences();
            }

            // ★ ローカルプレイヤー：VRトラッキングデータをネットワーク変数に書き込む
            SendTrackingDataToNetwork();
        }
        else
        {
            // ★ リモートプレイヤー：ネットワーク変数からPrimitiveの位置を更新
            ApplyNetworkDataToPrimitives();
        }
    }

    /// <summary>
    /// 【ローカルプレイヤー用】VRトラッキングデータをネットワーク変数に送信
    /// </summary>
    private void SendTrackingDataToNetwork()
    {
        // 頭（VRカメラ）のトラッキング
        if (vrHead != null)
        {
            netHeadPos.Value = vrHead.position;
            netHeadRot.Value = vrHead.rotation;

            // ローカルではPrimitiveを直接VR位置に追従させる
            if (headPrimitive != null)
            {
                headPrimitive.position = vrHead.position;
                headPrimitive.rotation = vrHead.rotation;
            }
        }

        // 左手（左コントローラー）のトラッキング
        if (vrLeftHand != null)
        {
            netLeftHandPos.Value = vrLeftHand.position;
            netLeftHandRot.Value = vrLeftHand.rotation;

            if (leftHandPrimitive != null)
            {
                leftHandPrimitive.position = vrLeftHand.position;
                leftHandPrimitive.rotation = vrLeftHand.rotation;
            }
        }

        // 右手（右コントローラー）のトラッキング
        if (vrRightHand != null)
        {
            netRightHandPos.Value = vrRightHand.position;
            netRightHandRot.Value = vrRightHand.rotation;

            if (rightHandPrimitive != null)
            {
                rightHandPrimitive.position = vrRightHand.position;
                rightHandPrimitive.rotation = vrRightHand.rotation;
            }
        }
    }

    /// <summary>
    /// 【リモートプレイヤー用】ネットワーク変数からPrimitiveの位置を更新
    /// Lerpで滑らかに補間
    /// </summary>
    private void ApplyNetworkDataToPrimitives()
    {
        float t = lerpSpeed * Time.deltaTime;

        // 頭の位置・回転を補間適用
        if (headPrimitive != null)
        {
            headPrimitive.position = Vector3.Lerp(headPrimitive.position, netHeadPos.Value, t);
            headPrimitive.rotation = Quaternion.Slerp(headPrimitive.rotation, netHeadRot.Value, t);
        }

        // 左手の位置・回転を補間適用
        if (leftHandPrimitive != null)
        {
            leftHandPrimitive.position = Vector3.Lerp(leftHandPrimitive.position, netLeftHandPos.Value, t);
            leftHandPrimitive.rotation = Quaternion.Slerp(leftHandPrimitive.rotation, netLeftHandRot.Value, t);
        }

        // 右手の位置・回転を補間適用
        if (rightHandPrimitive != null)
        {
            rightHandPrimitive.position = Vector3.Lerp(rightHandPrimitive.position, netRightHandPos.Value, t);
            rightHandPrimitive.rotation = Quaternion.Slerp(rightHandPrimitive.rotation, netRightHandRot.Value, t);
        }
    }
}
