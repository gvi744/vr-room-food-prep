using UnityEngine;

public class GazeInteractor : MonoBehaviour
{
    [SerializeField] private MonoBehaviour gazeProviderObject;
    [SerializeField] private MonoBehaviour confirmProviderObject;

    private IGazeProvider gazeProvider;
    private IConfirmProvider confirmProvider;
    private Selectable currentTarget;

    private void Start()
    {
        gazeProvider = gazeProviderObject as IGazeProvider;
        confirmProvider = confirmProviderObject as IConfirmProvider;

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
            Debug.Log($"Ray hit: {hit.collider.gameObject.name}");

            Selectable selectable = hit.collider.GetComponent<Selectable>();

            if (selectable != null)
            {
                currentTarget?.HidePrompt();
                currentTarget = selectable;
                currentTarget.ShowPrompt();
                Debug.Log($"Confirmed: {currentTarget.gameObject.name}");

                if (confirmProvider.IsConfirmed())
                {
                    currentTarget.OnSelect();
                }
                
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
