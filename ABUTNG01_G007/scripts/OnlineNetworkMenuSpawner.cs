using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// オンラインマルチプレイヤー用のネットワークメニューUI
/// Unity Relay を使ってインターネット経由で接続する方式
/// 
/// 【学習ポイント】
/// - Unity Relay を使ったオンライン接続
/// - Join Code（ルームコード）による接続
/// - RelayNetworkManager との連携
/// 
/// 【前提条件】
/// - Unity Gaming Services の設定が完了していること
/// - RelayNetworkManager スクリプトがプロジェクトに存在すること
/// </summary>
public class OnlineNetworkMenuSpawner : MonoBehaviour
{
    [Header("=== UI設定 ===")]
    [Tooltip("UIを生成する位置（このオブジェクトからの相対位置）")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0, 1.5f, 2f);

    [Tooltip("UIのスケール")]
    [SerializeField] private float uiScale = 0.005f;

    [Tooltip("カメラの方を向くか")]
    [SerializeField] private bool faceCamera = true;

    [Header("=== UIカラー設定 ===")]
    [SerializeField] private Color createRoomButtonColor = new Color(0.5f, 0.3f, 0.7f);  // 紫：部屋作成
    [SerializeField] private Color joinRoomButtonColor = new Color(0.2f, 0.4f, 0.8f);    // 青：参加
    [SerializeField] private Color disconnectButtonColor = new Color(0.8f, 0.2f, 0.2f);  // 赤：切断
    [SerializeField] private Color panelColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);       // パネル背景
    [SerializeField] private Color inputFieldColor = new Color(0.2f, 0.2f, 0.2f, 1f);    // 入力欄背景

    // ===== 生成されたUI参照 =====
    private Canvas menuCanvas;
    private GameObject connectionPanel;  // 接続前パネル
    private GameObject connectedPanel;   // 接続後パネル
    private TextMeshProUGUI statusText;
    private TMP_InputField joinCodeInputField;
    private Button createRoomButton;
    private Button joinRoomButton;
    private Button disconnectButton;

    // キーパッド関連
    private GameObject numberKeypadPanel;
    private GameObject alphabetKeypadPanel;
    private bool showAlphabetKeypad = false;

    // 接続状態の変化を検知するための変数
    private string lastState = "";
    private string message = "Not Connected";
    private bool busy;

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

        var nm = NetworkManager.Singleton;
        var relay = RelayNetworkManager.Instance;
        string state = $"{nm?.IsConnectedClient}|{nm?.IsServer}|{nm?.ShutdownInProgress}|{nm?.ConnectedClientsIds.Count}|{nm?.LocalClientId}|{relay?.JoinCode}|{relay?.IsRelayHost}|{relay?.IsRelayClient}|{busy}";
        if (state != lastState) { lastState = state; UpdateUI(); }

    }

    #region ===== UI生成 =====

    /// <summary>
    /// ネットワークメニューUIを生成
    /// </summary>
    private void CreateNetworkMenuUI()
    {
        // ----- Canvas作成 -----
        GameObject canvasObj = new GameObject("OnlineNetworkMenuCanvas");
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

        Debug.Log("[OnlineNetworkMenuSpawner] Online Network Menu UI created");
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
    /// 接続前のUI（JoinCode入力、CreateRoom/JoinRoomボタン）を作成
    /// </summary>
    private void CreateConnectionUI(Transform parent)
    {
        // タイトル
        CreateText(parent, "Title", "Online Network", 28, new Vector2(0, 190), new Vector2(460, 40));

        // ステータス表示
        statusText = CreateText(parent, "Status", "Not Connected", 16, new Vector2(0, 160), new Vector2(460, 25));

        // Join Codeラベル
        CreateText(parent, "JoinCodeLabel", "Join Code:", 16, new Vector2(0, 125), new Vector2(460, 25));

        // Join Code入力フィールド
        joinCodeInputField = CreateInputField(parent, "JoinCodeInput", "", new Vector2(0, 90), new Vector2(350, 45));
        var placeholder = joinCodeInputField.placeholder as TextMeshProUGUI;
        if (placeholder != null)
        {
            placeholder.text = "Enter Join Code...";
        }

        // 数字/アルファベット切り替えキーパッド
        CreateNumberKeypad(parent);
        CreateAlphabetKeypad(parent);

        if (numberKeypadPanel != null)
        {
            var keypadRect = numberKeypadPanel.GetComponent<RectTransform>();
            keypadRect.anchoredPosition = new Vector2(0, -25);
        }
        if (alphabetKeypadPanel != null)
        {
            alphabetKeypadPanel.SetActive(false);
            var keypadRect = alphabetKeypadPanel.GetComponent<RectTransform>();
            keypadRect.anchoredPosition = new Vector2(0, -25);
        }

        // Create Roomボタン
        createRoomButton = CreateButton(parent, "CreateRoomButton", "Create Room", createRoomButtonColor,
            new Vector2(0, -140), new Vector2(350, 45));
        createRoomButton.onClick.AddListener(OnCreateRoomClicked);

        // Join Roomボタン
        joinRoomButton = CreateButton(parent, "JoinRoomButton", "Join Room", joinRoomButtonColor,
            new Vector2(0, -190), new Vector2(350, 45));
        joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
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
    /// 数字キーパッドを作成（Join Code入力用）
    /// </summary>
    private void CreateNumberKeypad(Transform parent)
    {
        numberKeypadPanel = new GameObject("NumberKeypadPanel");
        numberKeypadPanel.transform.SetParent(parent, false);
        numberKeypadPanel.layer = LayerMask.NameToLayer("UI");

        RectTransform keypadRect = numberKeypadPanel.AddComponent<RectTransform>();
        keypadRect.sizeDelta = new Vector2(350, 180);
        keypadRect.anchoredPosition = Vector2.zero;

        // キー配置（4行3列）: 1-9, ABC, 0, Del
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "ABC", "0", "Del" };
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
            else if (key == "ABC")
                buttonColor = new Color(0.3f, 0.5f, 0.7f);
            else
                buttonColor = new Color(0.3f, 0.3f, 0.3f);

            Button keyButton = CreateButton(numberKeypadPanel.transform, $"NumKey_{key}", key, buttonColor,
                new Vector2(x, y), new Vector2(buttonWidth, buttonHeight));

            string capturedKey = key;
            keyButton.onClick.AddListener(() => OnNumberKeypadPressed(capturedKey));
        }
    }

    /// <summary>
    /// アルファベットキーパッドを作成（Join Code入力用）
    /// </summary>
    private void CreateAlphabetKeypad(Transform parent)
    {
        alphabetKeypadPanel = new GameObject("AlphabetKeypadPanel");
        alphabetKeypadPanel.transform.SetParent(parent, false);
        alphabetKeypadPanel.layer = LayerMask.NameToLayer("UI");

        RectTransform keypadRect = alphabetKeypadPanel.AddComponent<RectTransform>();
        keypadRect.sizeDelta = new Vector2(350, 180);
        keypadRect.anchoredPosition = Vector2.zero;

        // キー配置（4行7列）: A-Z, 123, Del
        string[] keys = {
            "A", "B", "C", "D", "E", "F", "G",
            "H", "I", "J", "K", "L", "M", "N",
            "O", "P", "Q", "R", "S", "T", "U",
            "V", "W", "X", "Y", "Z", "123", "Del"
        };
        int cols = 7;
        float buttonWidth = 45;
        float buttonHeight = 40;
        float spacing = 3;

        float totalHeight = buttonHeight * 4 + spacing * 3;
        float startY = totalHeight / 2 - buttonHeight / 2;
        float totalWidth = buttonWidth * cols + spacing * (cols - 1);
        float startX = -totalWidth / 2 + buttonWidth / 2;

        for (int i = 0; i < keys.Length; i++)
        {
            int row = i / cols;
            int col = i % cols;

            float x = startX + col * (buttonWidth + spacing);
            float y = startY - row * (buttonHeight + spacing);

            string key = keys[i];
            Color buttonColor;
            if (key == "Del")
                buttonColor = disconnectButtonColor;
            else if (key == "123")
                buttonColor = new Color(0.3f, 0.5f, 0.7f);
            else
                buttonColor = new Color(0.3f, 0.3f, 0.3f);

            Button keyButton = CreateButton(alphabetKeypadPanel.transform, $"AlphaKey_{key}", key, buttonColor,
                new Vector2(x, y), new Vector2(buttonWidth, buttonHeight));

            string capturedKey = key;
            keyButton.onClick.AddListener(() => OnAlphabetKeypadPressed(capturedKey));
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
        placeholderText.text = "Enter Join Code...";
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
        inputField.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;

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

    #region ===== キーパッドイベント =====

    /// <summary>
    /// 数字キーパッドが押された時の処理
    /// </summary>
    private void OnNumberKeypadPressed(string key)
    {
        if (joinCodeInputField == null) return;

        if (key == "Del")
        {
            if (joinCodeInputField.text.Length > 0)
            {
                joinCodeInputField.text = joinCodeInputField.text.Substring(0, joinCodeInputField.text.Length - 1);
            }
        }
        else if (key == "ABC")
        {
            // アルファベットキーパッドに切り替え
            ToggleKeypad();
        }
        else
        {
            joinCodeInputField.text += key.ToUpper();
        }
    }

    /// <summary>
    /// アルファベットキーパッドが押された時の処理
    /// </summary>
    private void OnAlphabetKeypadPressed(string key)
    {
        if (joinCodeInputField == null) return;

        if (key == "Del")
        {
            if (joinCodeInputField.text.Length > 0)
            {
                joinCodeInputField.text = joinCodeInputField.text.Substring(0, joinCodeInputField.text.Length - 1);
            }
        }
        else if (key == "123")
        {
            // 数字キーパッドに切り替え
            ToggleKeypad();
        }
        else
        {
            joinCodeInputField.text += key.ToUpper();
        }
    }

    /// <summary>
    /// キーパッドの表示を切り替え（数字⇔アルファベット）
    /// </summary>
    private void ToggleKeypad()
    {
        showAlphabetKeypad = !showAlphabetKeypad;

        if (numberKeypadPanel != null)
            numberKeypadPanel.SetActive(!showAlphabetKeypad);

        if (alphabetKeypadPanel != null)
            alphabetKeypadPanel.SetActive(showAlphabetKeypad);
    }

    #endregion

    #region ===== ネットワークイベント =====

    /// <summary>
    /// Create Roomボタンが押された時：Relay Hostとして部屋を作成
    /// </summary>
    private void OnCreateRoomClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[OnlineNetworkMenuSpawner] NetworkManager.Singleton is null!");
            return;
        }

        if (!busy && !NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress)
        {
            // RelayNetworkManagerがない場合は作成
            EnsureRelayNetworkManager();

            busy = true;
            UpdateUI();
            // ステータスを更新
            if (statusText != null)
            {
                statusText.text = message = "Creating Room...";
            }

            // Relay Hostとして開始
            RelayNetworkManager.Instance.StartRelayHost((joinCode) =>
            {
                if (this == null) return;
                busy = false;
                if (!string.IsNullOrEmpty(joinCode))
                {
                    Debug.Log($"[OnlineNetworkMenuSpawner] Room created! Join Code: {joinCode}");
                    // 作成されたJoin Codeを入力欄に表示
                    if (joinCodeInputField != null)
                    {
                        joinCodeInputField.text = joinCode;
                    }
                }
                else
                {
                    Debug.LogWarning("[OnlineNetworkMenuSpawner] Failed to create room");
                    if (statusText != null)
                    {
                        statusText.text = message = "Failed to create room. See Console.";
                    }
                }
                UpdateUI();
            });
        }
    }

    /// <summary>
    /// Join Roomボタンが押された時：Relay Clientとして部屋に参加
    /// </summary>
    private void OnJoinRoomClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[OnlineNetworkMenuSpawner] NetworkManager.Singleton is null!");
            return;
        }

        if (!busy && !NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress)
        {
            string joinCode = joinCodeInputField != null ? joinCodeInputField.text.Trim().ToUpper() : "";

            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogError("[OnlineNetworkMenuSpawner] Join Code is empty!");
                if (statusText != null)
                {
                    statusText.text = message = "Enter a Join Code";
                }
                return;
            }

            // RelayNetworkManagerがない場合は作成
            EnsureRelayNetworkManager();

            busy = true;
            UpdateUI();
            // ステータスを更新
            if (statusText != null)
            {
                statusText.text = message = $"Joining {joinCode}...";
            }

            // Relay Clientとして参加
            RelayNetworkManager.Instance.JoinRelay(joinCode, (success) =>
            {
                if (this == null) return;
                busy = false;
                if (success)
                {
                    Debug.Log($"[OnlineNetworkMenuSpawner] Joined room with code: {joinCode}");
                }
                else
                {
                    Debug.LogWarning($"[OnlineNetworkMenuSpawner] Failed to join room: {joinCode}");
                    if (statusText != null)
                    {
                        statusText.text = message = "Failed to join. Check code and Host.";
                    }
                }
                UpdateUI();
            });
        }
    }

    /// <summary>
    /// Disconnectボタンが押された時：接続を切断
    /// </summary>
    private void OnDisconnectClicked()
    {
        message = "Not Connected";
        if (RelayNetworkManager.Instance != null &&
            (RelayNetworkManager.Instance.IsRelayHost || RelayNetworkManager.Instance.IsRelayClient))
        {
            RelayNetworkManager.Instance.Disconnect();
            Debug.Log("[OnlineNetworkMenuSpawner] Disconnected from Relay");
        }
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("[OnlineNetworkMenuSpawner] Disconnected");
        }
        UpdateUI();
    }

    /// <summary>
    /// RelayNetworkManagerが存在することを保証
    /// </summary>
    private void EnsureRelayNetworkManager()
    {
        if (RelayNetworkManager.Instance == null)
        {
            GameObject relayManagerObj = new GameObject("RelayNetworkManager");
            relayManagerObj.AddComponent<RelayNetworkManager>();
        }
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
        bool canStart = !busy && !NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress;
        if (createRoomButton != null) createRoomButton.interactable = canStart;
        if (joinRoomButton != null) joinRoomButton.interactable = canStart;
        bool isRelayMode = RelayNetworkManager.Instance != null &&
                          (RelayNetworkManager.Instance.IsRelayHost || RelayNetworkManager.Instance.IsRelayClient);

        if (isConnected) message = "Not Connected";

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
                statusText.text = message;
            }
            else if (isRelayMode && RelayNetworkManager.Instance.IsRelayHost)
            {
                string joinCode = RelayNetworkManager.Instance.JoinCode;
                statusText.text = $"Host | Code: {joinCode}";
            }
            else if (isRelayMode && RelayNetworkManager.Instance.IsRelayClient)
            {
                statusText.text = "Client | Connected";
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
                    if (isRelayMode && RelayNetworkManager.Instance.IsRelayHost)
                    {
                        string joinCode = RelayNetworkManager.Instance.JoinCode;
                        tmp.text = $"Mode: Host\nJoin Code: {joinCode}\nClients: {NetworkManager.Singleton.ConnectedClientsIds.Count}";
                    }
                    else if (isRelayMode && RelayNetworkManager.Instance.IsRelayClient)
                    {
                        tmp.text = $"Mode: Client\nYour ID: {NetworkManager.Singleton.LocalClientId}";
                    }
                }
            }
        }
    }

    #endregion
}
