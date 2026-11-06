using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// VR環境でTMP_InputFieldをタップした時にシステムキーボードを表示するヘルパー
/// Quest3などのVRデバイスで使用
/// </summary>
public class VRKeyboardHelper : MonoBehaviour, IPointerClickHandler, ISelectHandler
{
    private TMP_InputField inputField;
    private TouchScreenKeyboard keyboard;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OpenKeyboard();
    }

    public void OnSelect(BaseEventData eventData)
    {
        OpenKeyboard();
    }

    private void OpenKeyboard()
    {
        if (inputField == null) return;

        // 既存のキーボードがあれば閉じる
        if (keyboard != null && keyboard.active)
        {
            return;
        }

        // システムキーボードを開く
        keyboard = TouchScreenKeyboard.Open(
            inputField.text,
            TouchScreenKeyboardType.Default,
            false,  // autocorrection
            false,  // multiline
            false,  // secure
            false,  // alert
            "",     // placeholder
            0       // characterLimit (0 = no limit)
        );

        Debug.Log("[VRKeyboardHelper] Opening system keyboard");
    }

    private void Update()
    {
        // キーボードが開いている場合、入力を同期
        if (keyboard != null && keyboard.active && inputField != null)
        {
            inputField.text = keyboard.text;
        }

        // キーボードが閉じられた場合
        if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Done)
        {
            inputField.text = keyboard.text;
            keyboard = null;
            Debug.Log("[VRKeyboardHelper] Keyboard closed");
        }
        else if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Canceled)
        {
            keyboard = null;
            Debug.Log("[VRKeyboardHelper] Keyboard canceled");
        }
    }

    private void OnDisable()
    {
        // コンポーネントが無効になったらキーボードを閉じる
        if (keyboard != null && keyboard.active)
        {
            keyboard.active = false;
            keyboard = null;
        }
    }
}
