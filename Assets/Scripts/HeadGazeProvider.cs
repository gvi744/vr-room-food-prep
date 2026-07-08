using UnityEngine;

public class HeadGazeProvider : MonoBehaviour, IGazeProvider
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private LayerMask layerMask = ~0;

    private void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;
    }

    public bool Raycast(out RaycastHit hit)
    {
        Debug.DrawRay(cameraTransform.position, cameraTransform.forward * maxDistance, Color.green);
        return Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, maxDistance, layerMask);
    }
}
