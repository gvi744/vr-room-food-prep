using UnityEngine;

public enum SelectableKind { Ingredient, Trash }

public class Selectable : MonoBehaviour
{
    [SerializeField] private SelectableKind kind = SelectableKind.Ingredient;
    [SerializeField] private GameObject prompt; // world-space UI, e.g. "Click to Plate"
    [SerializeField] private GameObject placedPrefab; // assign BottomBun_Placed etc.
    [SerializeField] private Plate plate;

    public void ShowPrompt()
    {
        if (prompt != null) prompt.SetActive(true);
    }

    public void HidePrompt()
    {
        if (prompt != null) prompt.SetActive(false);
    }

    public void OnSelect()
    {
        if (kind == SelectableKind.Ingredient)
            plate.Place(placedPrefab);
        else
            plate.Clear();
    }
}