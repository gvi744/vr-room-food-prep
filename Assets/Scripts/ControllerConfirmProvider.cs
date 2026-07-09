using UnityEngine;
using UnityEngine.InputSystem;

public class ControllerConfirmProvider : MonoBehaviour, IConfirmProvider
{
	[SerializeField] private InputAction confirmAction;

	private void OnEnable()
	{
		if (confirmAction != null)
			confirmAction.Enable();
	}

	private void OnDisable()
	{
		if (confirmAction != null)
			confirmAction.Disable();
	}

	public bool IsConfirmed()
	{
		if (confirmAction == null) return false;
		return confirmAction.WasPressedThisFrame();
	}
}