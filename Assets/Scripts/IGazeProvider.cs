using UnityEngine;

public interface IGazeProvider
{
    bool Raycast(out UnityEngine.RaycastHit hit);
}
