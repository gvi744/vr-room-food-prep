using UnityEngine;
using UnityEngine.InputSystem;

public class RayCastMouseTest : MonoBehaviour
{
    [SerializeField] private float maxDistance = 10f;
    private GameObject currentTarget;

    void Update()
    {
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            GameObject hitObject = hit.collider.gameObject;

            if (hitObject != currentTarget)
            {
                ClearTarget();
                currentTarget = hitObject;
                OnGazeEnter(currentTarget);
            }
        }
        else
        {
            ClearTarget();
        }

        // click-to-confirm on whatever is currently targeted
        if (currentTarget != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            var selectable = currentTarget.GetComponent<Selectable>();
            if (selectable != null) selectable.OnSelect();
        }
    }

    void OnGazeEnter(GameObject target)
    {
        // show popup, highlight, etc.
        var selectable = target.GetComponent<Selectable>();
        if (selectable != null) selectable.ShowPrompt();
    }

    void ClearTarget()
    {
        if (currentTarget != null)
        {
            var selectable = currentTarget.GetComponent<Selectable>();
            if (selectable != null) selectable.HidePrompt();
            currentTarget = null;
        }
    }
}