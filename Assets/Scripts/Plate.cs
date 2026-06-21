using System.Collections.Generic;
using UnityEngine;

public class Plate : MonoBehaviour
{
    [SerializeField] private float dropHeight = 0.05f; // vertical gap per layer
    private readonly List<GameObject> placed = new();
    private float nextY = 0f;

    public void Place(GameObject placedPrefab)
    {
        Vector3 pos = transform.position + new Vector3(0f, nextY, 0f);
        GameObject item = Instantiate(placedPrefab, pos, transform.rotation, transform);
        placed.Add(item);
        nextY += dropHeight;
    }

    public void Clear()
    {
        foreach (GameObject item in placed)
            Destroy(item);
        placed.Clear();
        nextY = 0f;
    }
}