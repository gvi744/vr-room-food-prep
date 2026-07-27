// KeyboardConfirmProvider.cs — testing only
using UnityEngine;
using UnityEngine.InputSystem;
public class KeyboardConfirmProvider : MonoBehaviour, IConfirmProvider
{
    public bool IsConfirmed() =>
        Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
}