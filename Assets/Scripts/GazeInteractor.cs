using UnityEngine;

public class GazeInteractor : MonoBehaviour
{
    [SerializeField] private CalibratedGazeProvider gazeProviderObject;
    [SerializeField] private ControllerConfirmProvider confirmProviderObject;

    private IGazeProvider gazeProvider;
    private IConfirmProvider confirmProvider;
    private Selectable currentTarget;

    private void Start()
    {
        gazeProvider = gazeProviderObject;
        confirmProvider = confirmProviderObject;

        if (gazeProvider == null)
            Debug.LogError("GazeInteractor: gazeProviderObject does not implement IGazeProvider");
        if (confirmProvider == null)
            Debug.LogError("confirmProvider: confirmProviderObject does not implement IConfirmProvider");

    }

    private void Update()
    {
        if (gazeProvider == null || confirmProvider == null) return;

        if (gazeProvider.Raycast(out RaycastHit hit))
        {
            Selectable selectable = hit.collider.GetComponent<Selectable>();

            if (selectable != null)
            {
                if (selectable != currentTarget)   // only on gaze entering
                {
                    currentTarget?.HidePrompt();
                    currentTarget = selectable;
                    currentTarget.ShowPrompt();
                }

                if (confirmProvider.IsConfirmed())
                {
                    Debug.Log($"[gaze] selected: {currentTarget.gameObject.name}");
                    currentTarget.OnSelect();
                }
            }
            else
            {
                // Gazing at a non-selectable surface (wall, bench): the old
                // target's prompt must not stay up.
                ClearTarget();
            }
        }
        else
        {
            ClearTarget();
        }
    }

    private void ClearTarget()
    {
        if (currentTarget != null)
        {
            currentTarget.HidePrompt();
            currentTarget = null;
        }
    }
}