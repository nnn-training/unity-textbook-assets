using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

/// <summary>Relay の割り当てと NGO の接続を管理する教材用コンポーネント。</summary>
public class RelayNetworkManager : MonoBehaviour
{
    public static RelayNetworkManager Instance { get; private set; }
    [Header("Relay Settings")]
    [Tooltip("ホストを除く参加者数。2人で遊ぶ場合は1")]
    [SerializeField, Min(1)] private int maxConnections = 1;
    [Header("Status")]
    [SerializeField] private bool isInitialized;
    [SerializeField] private string currentJoinCode = "";
    public bool IsInitialized => isInitialized;
    public string JoinCode => currentJoinCode;
    public bool IsRelayHost { get; private set; }
    public bool IsRelayClient { get; private set; }
    public bool IsBusy { get; private set; }
    public string LastError { get; private set; } = "";
    public event Action OnServicesInitialized;
    public event Action<string> OnJoinCodeGenerated;
    public event Action<string> OnError;
    public event Action OnRelayConnected;
    public event Action OnRelayDisconnected;
    private NetworkManager manager;
    private int operation;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    private void OnDestroy()
    {
        operation++;
        if (manager != null) manager.OnClientStopped -= OnClientStopped;
        if (Instance == this) Instance = null;
    }
    private void OnClientStopped(bool wasHost) { ResetConnection(); }
    private void ResetConnection()
    {
        currentJoinCode = "";
        IsRelayHost = IsRelayClient = false;
        OnRelayDisconnected?.Invoke();
    }
    private void ReportError(Exception e)
    {
        LastError = e.Message;
        Debug.LogWarning($"[RelayNetworkManager] {LastError}");
        OnError?.Invoke(LastError);
    }
    private bool BeginOperation()
    {
        var next = NetworkManager.Singleton;
        if (IsBusy || next == null || next.IsListening || next.ShutdownInProgress) return false;
        if (manager != next)
        {
            if (manager != null) manager.OnClientStopped -= OnClientStopped;
            manager = next;
            manager.OnClientStopped += OnClientStopped;
        }
        LastError = "";
        IsBusy = true;
        return true;
    }
    public async Task<bool> InitializeServicesAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            isInitialized = true;
            OnServicesInitialized?.Invoke();
            return true;
        }
        catch (Exception e) { ReportError(e); return false; }
    }
    private void CheckOperation(int expected)
    {
        if (this == null || expected != operation) throw new OperationCanceledException();
    }
    public async Task<string> StartRelayHostAsync()
    {
        if (!BeginOperation()) return null;
        int expected = ++operation;
        try
        {
            if (!await InitializeServicesAsync()) return null;
            CheckOperation(expected);
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            CheckOperation(expected);
            var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            CheckOperation(expected);
            manager.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            if (!manager.StartHost()) throw new InvalidOperationException("Failed to start Host");
            currentJoinCode = code;
            IsRelayHost = true;
            IsRelayClient = false;
            OnJoinCodeGenerated?.Invoke(code);
            OnRelayConnected?.Invoke();
            return code;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e) { ReportError(e); Disconnect(); return null; }
        finally { IsBusy = false; }
    }
    public async Task<bool> JoinRelayAsync(string joinCode)
    {
        if (!BeginOperation()) return false;
        int expected = ++operation;
        try
        {
            if (string.IsNullOrWhiteSpace(joinCode)) throw new ArgumentException("Enter a Join Code");
            if (!await InitializeServicesAsync()) return false;
            CheckOperation(expected);
            string code = joinCode.Trim().ToUpperInvariant();
            var allocation = await RelayService.Instance.JoinAllocationAsync(code);
            CheckOperation(expected);
            manager.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            if (!manager.StartClient()) throw new InvalidOperationException("Failed to start Client");
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            // StartClient は接続開始。NGO の接続成立まで待つ。
            while (!manager.IsConnectedClient)
            {
                await Task.Yield();
                CheckOperation(expected);
                if (!manager.IsListening || Time.realtimeSinceStartupAsDouble > deadline)
                    throw new TimeoutException("Connection failed or timed out");
            }
            currentJoinCode = code;
            IsRelayClient = true;
            IsRelayHost = false;
            OnRelayConnected?.Invoke();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception e) { ReportError(e); Disconnect(); return false; }
        finally { IsBusy = false; }
    }
    public void Disconnect()
    {
        operation++;
        if (manager != null && manager.IsListening) manager.Shutdown();
        ResetConnection();
    }
    public async void StartRelayHost(Action<string> callback)
    {
        var code = await StartRelayHostAsync();
        if (this != null) callback?.Invoke(code);
    }
    public async void JoinRelay(string code, Action<bool> callback)
    {
        var success = await JoinRelayAsync(code);
        if (this != null) callback?.Invoke(success);
    }
    public async void InitializeServices(Action<bool> callback)
    {
        var success = await InitializeServicesAsync();
        if (this != null) callback?.Invoke(success);
    }
}
