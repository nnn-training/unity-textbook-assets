using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// ローカルマルチプレイヤー用のネットワークメニューUI
/// 同じLAN内でIPアドレスを使って直接接続する方式
/// 
/// 【学習ポイント】
/// - UnityTransportを使ったIP直接接続
/// - Host/Client モデルの基本
/// - World Space Canvas のVR対応UI
///
/// 【Meta Quest / Android で LAN Host を使う前提】
/// - Player Settings > Android > Internet Access を Require にする。
/// - OpenXR > Meta Quest Support > Force Remove Internet Permission をオフにして再ビルドする。
///   最終 APK の AndroidManifest.xml に android.permission.INTERNET が必要（LAN 通信でも必要）。
/// - 同じ LAN に接続し、Wi-Fi の AP isolation / ゲストネットワーク等による端末間通信の遮断がないこと。
/// - Host は全 IPv4 インターフェースの 0.0.0.0:port（既定 7777）で待ち受ける。
///   Client は Host の実際の LAN IPv4 アドレスを入力する。0.0.0.0 は接続先ではない。
/// - 権限のビルド設定や Wi-Fi の設定は、このスクリプトから自動変更しない。
///   Quest 実機では、Host 起動後に別端末から参加して Clients = 2 になることまで確認する。
/// </summary>
public class LocalNetworkMenuSpawner : MonoBehaviour
{
    [Header("=== UI設定 ===")]
    [Tooltip("UIを生成する位置（このオブジェクトからの相対位置）")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0, 1.5f, 2f);

    [Tooltip("UIのスケール")]
    [SerializeField] private float uiScale = 0.005f;

    [Tooltip("カメラの方を向くか")]
    [SerializeField] private bool faceCamera = true;

    [Header("=== ネットワーク設定 ===")]
    [Tooltip("デフォルトのIPアドレス")]
    [SerializeField] private string defaultIPAddress = "192.168.1.1";

    [Tooltip("ポート番号")]
    [SerializeField] private ushort port = 7777;

    [Header("=== UIカラー設定 ===")]
    [SerializeField] private Color hostButtonColor = new Color(0.2f, 0.6f, 0.2f);      // 緑：Hostボタン
    [SerializeField] private Color clientButtonColor = new Color(0.2f, 0.4f, 0.8f);    // 青：Clientボタン
    [SerializeField] private Color disconnectButtonColor = new Color(0.8f, 0.2f, 0.2f); // 赤：切断ボタン
    [SerializeField] private Color panelColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);     // パネル背景
    [SerializeField] private Color inputFieldColor = new Color(0.2f, 0.2f, 0.2f, 1f);  // 入力欄背景

    // ===== 生成されたUI参照 =====
    private Canvas menuCanvas;
    private GameObject connectionPanel;  // 接続前パネル
    private GameObject connectedPanel;   // 接続後パネル
    private TextMeshProUGUI statusText;
    private TMP_InputField ipInputField;
    private Button hostButton;
    private Button clientButton;
    private Button disconnectButton;
    private GameObject keypadPanel;

    // 接続状態の変化を検知するための変数
    private NetworkManager lastManager;
    private bool lastIsServer, lastIsClient, lastIsConnected;
    private ulong lastClientId;
    private int lastClientCount = -1;

    /// <summary>
    /// 初期化：UIを自動生成
    /// </summary>
    private void Start()
    {
        CreateNetworkMenuUI();
    }

    /// <summary>
    /// 毎フレーム更新：カメラ追従と接続状態の監視
    /// </summary>
    private void Update()
    {
        // カメラの方を向く処理
        if (faceCamera && menuCanvas != null && Camera.main != null)
        {
            menuCanvas.transform.LookAt(Camera.main.transform);
            menuCanvas.transform.Rotate(0, 180, 0);
        }

        // 接続状態が変化した時のみUIを更新（パフォーマンス最適化）
        var manager = NetworkManager.Singleton;
        bool isServer = manager != null && manager.IsServer;
        bool isClient = manager != null && manager.IsClient;
        bool isConnected = manager != null && manager.IsConnectedClient;
        ulong clientId = manager != null ? manager.LocalClientId : 0;
        int clientCount = manager != null ? manager.ConnectedClientsIds.Count : 0;
        if (manager != lastManager || isServer != lastIsServer || isClient != lastIsClient ||
            isConnected != lastIsConnected || clientId != lastClientId || clientCount != lastClientCount)
        {
            lastManager = manager;
            lastIsServer = isServer;
            lastIsClient = isClient;
            lastIsConnected = isConnected;
            lastClientId = clientId;
            lastClientCount = clientCount;
            UpdateUI();
        }
    }

    #region ===== UI生成 =====

    /// <summary>
    /// ネットワークメニューUIを生成
    /// </summary>
    private void CreateNetworkMenuUI()
    {
        // ----- Canvas作成 -----
        GameObject canvasObj = new GameObject("LocalNetworkMenuCanvas");
        canvasObj.transform.SetParent(transform);
        canvasObj.transform.localPosition = spawnOffset;
        canvasObj.transform.localRotation = Quaternion.identity;
        canvasObj.transform.localScale = Vector3.one * uiScale;
        canvasObj.layer = LayerMask.NameToLayer("UI");

        menuCanvas = canvasObj.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.WorldSpace;
        menuCanvas.worldCamera = Camera.main;

        // Canvas Scaler
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        // VR用のRaycaster（XR Interaction Toolkit）
        canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();
        // エディタでのマウス操作用
        canvasObj.AddComponent<GraphicRaycaster>();

        // RectTransform設定
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(500, 450);

        // メインパネル
        GameObject mainPanel = CreatePanel(canvasObj.transform, "MainPanel", new Vector2(500, 450));

        // ----- 接続前パネル -----
        connectionPanel = CreatePanel(mainPanel.transform, "ConnectionPanel", new Vector2(480, 430));
        CreateConnectionUI(connectionPanel.transform);

        // ----- 接続後パネル -----
        connectedPanel = CreatePanel(mainPanel.transform, "ConnectedPanel", new Vector2(480, 430));
        CreateConnectedUI(connectedPanel.transform);
        connectedPanel.SetActive(false);

        Debug.Log("[LocalNetworkMenuSpawner] Local Network Menu UI created");
    }

    /// <summary>
    /// パネル（背景付きの領域）を作成
    /// </summary>
    private GameObject CreatePanel(Transform parent, string name, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        panel.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        Image image = panel.AddComponent<Image>();
        image.color = panelColor;
        image.raycastTarget = false;

        return panel;
    }

    /// <summary>
    /// 接続前のUI（IPアドレス入力、Host/Clientボタン）を作成
    /// </summary>
    private void CreateConnectionUI(Transform parent)
    {
        // タイトル
        CreateText(parent, "Title", "Local Network", 28, new Vector2(0, 190), new Vector2(460, 40));

        // ステータス表示
        statusText = CreateText(parent, "Status", "Not Connected", 16, new Vector2(0, 160), new Vector2(460, 25));

        // IPアドレスラベル
        CreateText(parent, "IPLabel", "Server IP Address:", 16, new Vector2(0, 125), new Vector2(460, 25));

        // IPアドレス入力フィールド
        ipInputField = CreateInputField(parent, "IPInput", defaultIPAddress, new Vector2(0, 90), new Vector2(350, 45));

        // 数字キーパッド（VRでの入力を容易にするため）
        CreateKeypad(parent);
        if (keypadPanel != null)
        {
            var keypadRect = keypadPanel.GetComponent<RectTransform>();
            keypadRect.anchoredPosition = new Vector2(0, -25);
        }

        // Hostボタン
        hostButton = CreateButton(parent, "HostButton", "Start as Host", hostButtonColor,
            new Vector2(0, -140), new Vector2(350, 45));
        hostButton.onClick.AddListener(OnHostClicked);

        // Clientボタン
        clientButton = CreateButton(parent, "ClientButton", "Connect as Client", clientButtonColor,
            new Vector2(0, -190), new Vector2(350, 45));
        clientButton.onClick.AddListener(OnClientClicked);
    }

    /// <summary>
    /// 接続後のUI（ステータス表示、切断ボタン）を作成
    /// </summary>
    private void CreateConnectedUI(Transform parent)
    {
        // タイトル
        CreateText(parent, "Title", "Connected", 32, new Vector2(0, 150), new Vector2(460, 50));

        // ステータス（接続情報）
        CreateText(parent, "ConnectedStatus", "", 22, new Vector2(0, 50), new Vector2(460, 120));

        // Disconnectボタン
        disconnectButton = CreateButton(parent, "DisconnectButton", "Disconnect", disconnectButtonColor,
            new Vector2(0, -100), new Vector2(350, 60));
        disconnectButton.onClick.AddListener(OnDisconnectClicked);
    }

    /// <summary>
    /// 数字キーパッドを作成（IPアドレス入力用）
    /// </summary>
    private void CreateKeypad(Transform parent)
    {
        keypadPanel = new GameObject("KeypadPanel");
        keypadPanel.transform.SetParent(parent, false);
        keypadPanel.layer = LayerMask.NameToLayer("UI");

        RectTransform keypadRect = keypadPanel.AddComponent<RectTransform>();
        keypadRect.sizeDelta = new Vector2(350, 180);
        keypadRect.anchoredPosition = Vector2.zero;

        // キー配置（4行3列）: 1-9, ., 0, Del
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "Del" };
        int cols = 3;
        float buttonWidth = 100;
        float buttonHeight = 40;
        float spacing = 5;

        float totalHeight = buttonHeight * 4 + spacing * 3;
        float startY = totalHeight / 2 - buttonHeight / 2;

        for (int i = 0; i < keys.Length; i++)
        {
            int row = i / cols;
            int col = i % cols;

            float x = (col - 1) * (buttonWidth + spacing);
            float y = startY - row * (buttonHeight + spacing);

            string key = keys[i];
            Color buttonColor;
            if (key == "Del")
                buttonColor = disconnectButtonColor;
            else if (key == ".")
                buttonColor = new Color(0.4f, 0.4f, 0.2f);
            else
                buttonColor = new Color(0.3f, 0.3f, 0.3f);

            Button keyButton = CreateButton(keypadPanel.transform, $"Key_{key}", key, buttonColor,
                new Vector2(x, y), new Vector2(buttonWidth, buttonHeight));

            string capturedKey = key;
            keyButton.onClick.AddListener(() => OnKeypadPressed(capturedKey));
        }
    }

    #endregion

    #region ===== UI部品作成ヘルパー =====

    private TextMeshProUGUI CreateText(Transform parent, string name, string text, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);
        textObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = textObj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return tmp;
    }

    private TMP_InputField CreateInputField(Transform parent, string name, string defaultText, Vector2 position, Vector2 size)
    {
        GameObject inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);
        inputObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = inputObj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        Image bgImage = inputObj.AddComponent<Image>();
        bgImage.color = inputFieldColor;
        bgImage.raycastTarget = true;

        // Text Area
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        textArea.layer = LayerMask.NameToLayer("UI");

        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10, 5);
        textAreaRect.offsetMax = new Vector2(-10, -5);
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        GameObject placeholder = new GameObject("Placeholder");
        placeholder.transform.SetParent(textArea.transform, false);
        placeholder.layer = LayerMask.NameToLayer("UI");

        RectTransform placeholderRect = placeholder.AddComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        TextMeshProUGUI placeholderText = placeholder.AddComponent<TextMeshProUGUI>();
        placeholderText.text = "Enter IP Address...";
        placeholderText.fontSize = 24;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        // Input Text
        GameObject inputText = new GameObject("Text");
        inputText.transform.SetParent(textArea.transform, false);
        inputText.layer = LayerMask.NameToLayer("UI");

        RectTransform inputTextRect = inputText.AddComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = Vector2.zero;
        inputTextRect.offsetMax = Vector2.zero;

        TextMeshProUGUI inputTextComponent = inputText.AddComponent<TextMeshProUGUI>();
        inputTextComponent.fontSize = 24;
        inputTextComponent.alignment = TextAlignmentOptions.MidlineLeft;
        inputTextComponent.color = Color.white;

        // InputField component
        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputTextComponent;
        inputField.placeholder = placeholderText;
        inputField.text = defaultText;
        inputField.contentType = TMP_InputField.ContentType.Standard;
        inputField.lineType = TMP_InputField.LineType.SingleLine;

        // VR/モバイル向け設定
        inputField.shouldHideMobileInput = false;
        inputField.keyboardType = TouchScreenKeyboardType.Default;

        // VRキーボードヘルパーを追加（Quest等でシステムキーボードを表示）
        inputObj.AddComponent<VRKeyboardHelper>();

        return inputField;
    }

    private Button CreateButton(Transform parent, string name, string text, Color color, Vector2 position, Vector2 size)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);
        buttonObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = buttonObj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        Image image = buttonObj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.2f;
        colors.pressedColor = color * 0.8f;
        colors.selectedColor = color;
        button.colors = colors;

        // ボタンテキスト
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        textObj.layer = LayerMask.NameToLayer("UI");

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return button;
    }

    #endregion

    #region ===== イベントハンドラ =====

    /// <summary>
    /// キーパッドボタンが押された時の処理
    /// </summary>
    private void OnKeypadPressed(string key)
    {
        if (ipInputField == null) return;

        if (key == "Del")
        {
            // 1文字削除
            if (ipInputField.text.Length > 0)
            {
                ipInputField.text = ipInputField.text.Substring(0, ipInputField.text.Length - 1);
            }
        }
        else
        {
            // 数字またはドットを追加
            ipInputField.text += key;
        }
    }

    /// <summary>
    /// Hostボタンが押された時：ホストとして開始
    /// </summary>
    private void OnHostClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LocalNetworkMenuSpawner] NetworkManager.Singleton is null!");
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            try
            {
                // 接続先アドレスと待受アドレスを分ける。
                // 第3引数を明示し、Inspector の設定に依存せず LAN からの接続を受け付ける。
                // Host の接続先にはループバック、待受には全インターフェースを指定する。
                if (!SetServerAddress("127.0.0.1", "0.0.0.0")) return;

                Debug.Log("[LocalNetworkMenuSpawner] Starting Host...");
                bool success = NetworkManager.Singleton.StartHost();
                if (success)
                {
                    string localIP = GetLocalIPAddress();
                    Debug.Log($"[LocalNetworkMenuSpawner] Started as Host. Clients can connect to: {localIP}:{port}");
                }
                else
                {
                    Debug.LogError("[LocalNetworkMenuSpawner] Failed to start Host");
                }
                UpdateUI();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LocalNetworkMenuSpawner] Exception starting Host: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Clientボタンが押された時：クライアントとして接続
    /// </summary>
    private void OnClientClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LocalNetworkMenuSpawner] NetworkManager.Singleton is null!");
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            try
            {
                string serverIP = (ipInputField != null ? ipInputField.text : defaultIPAddress).Trim();

                if (string.IsNullOrEmpty(serverIP))
                {
                    Debug.LogError("[LocalNetworkMenuSpawner] IP address is empty!");
                    return;
                }

                if (!System.Net.IPAddress.TryParse(serverIP, out var parsedIP) ||
                    parsedIP.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    statusText.text = "Enter a valid IPv4 address";
                    return;
                }
                if (parsedIP.Equals(System.Net.IPAddress.Any))
                {
                    if (statusText != null) statusText.text = "Enter the Host's LAN IPv4 address";
                    Debug.LogWarning("[LocalNetworkMenuSpawner] 0.0.0.0 is a listen address. Enter the Host's LAN IPv4 address.");
                    return;
                }
                if (!SetServerAddress(serverIP)) return;

                Debug.Log($"[LocalNetworkMenuSpawner] Connecting to {serverIP}:{port}...");
                bool success = NetworkManager.Singleton.StartClient();
                if (success)
                {
                    Debug.Log("[LocalNetworkMenuSpawner] Started as Client");
                }
                else
                {
                    Debug.LogError("[LocalNetworkMenuSpawner] Failed to start Client");
                }
                UpdateUI();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LocalNetworkMenuSpawner] Exception starting Client: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Disconnectボタンが押された時：接続を切断
    /// </summary>
    private void OnDisconnectClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("[LocalNetworkMenuSpawner] Disconnected");
        }
        UpdateUI();
    }

    #endregion

    #region ===== ネットワーク処理 =====

    /// <summary>
    /// UnityTransport の接続先と、必要に応じてサーバーの待受アドレスを設定する。
    /// </summary>
    /// <param name="ipAddress">接続先。Client は Host の実際の LAN IPv4 を指定する。</param>
    /// <param name="listenAddress">Host は 0.0.0.0 を指定して全 IPv4 インターフェースで待ち受ける。</param>
    private bool SetServerAddress(string ipAddress, string listenAddress = null)
    {
        if (NetworkManager.Singleton == null) return false;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[LocalNetworkMenuSpawner] UnityTransport not found on NetworkManager!");
            if (statusText != null) statusText.text = "Unity Transport is missing";
            return false;
        }

        transport.SetConnectionData(ipAddress, port, listenAddress);
        Debug.Log($"[LocalNetworkMenuSpawner] Server address: {ipAddress}:{port}, listen address: {transport.ConnectionData.ServerListenAddress}:{port}");
        return true;
    }

    /// <summary>
    /// ローカルIPアドレスを取得
    /// </summary>
    private string GetLocalIPAddress()
    {
        string localIP = "127.0.0.1";
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    // 192.168.x.x を優先（家庭内LANで一般的）
                    if (localIP.StartsWith("192.168"))
                    {
                        break;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LocalNetworkMenuSpawner] Failed to get local IP: {e.Message}");
        }
        return localIP;
    }

    #endregion

    #region ===== UI更新 =====

    /// <summary>
    /// 接続状態に応じてUIを更新
    /// </summary>
    private void UpdateUI()
    {
        if (NetworkManager.Singleton == null) return;

        bool isConnected = NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsServer;
        bool isConnecting = NetworkManager.Singleton.IsClient && !isConnected;
        if (hostButton != null) hostButton.interactable = !isConnecting;
        if (clientButton != null) clientButton.interactable = !isConnecting;

        // パネルの表示切り替え
        if (connectionPanel != null)
            connectionPanel.SetActive(!isConnected);

        if (connectedPanel != null)
            connectedPanel.SetActive(isConnected);

        // ステータステキストの更新
        if (statusText != null)
        {
            if (!isConnected)
            {
                statusText.text = isConnecting ? "Connecting..." : "Not Connected";
            }
            else if (NetworkManager.Singleton.IsHost)
            {
                string localIP = GetLocalIPAddress();
                statusText.text = $"Host | IP: {localIP}:{port}";
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                statusText.text = "Connecting...";
            }
        }

        // 接続後パネル内のステータス更新
        if (connectedPanel != null && connectedPanel.activeInHierarchy)
        {
            var connectedStatus = connectedPanel.transform.Find("ConnectedStatus");
            if (connectedStatus != null)
            {
                var tmp = connectedStatus.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    if (NetworkManager.Singleton.IsHost)
                    {
                        string localIP = GetLocalIPAddress();
                        tmp.text = $"Mode: Host\nIP: {localIP}:{port}\nClients: {NetworkManager.Singleton.ConnectedClientsIds.Count}";
                    }
                    else if (NetworkManager.Singleton.IsClient)
                    {
                        tmp.text = $"Mode: Client\nYour ID: {NetworkManager.Singleton.LocalClientId}";
                    }
                }
            }
        }
    }

    #endregion
}
