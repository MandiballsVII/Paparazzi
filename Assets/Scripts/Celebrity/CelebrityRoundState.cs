using UnityEngine;

/// <summary>
/// Estado de ronda compartido: una sola acción polémica por match.
/// </summary>
public class CelebrityRoundState : MonoBehaviour
{
    public static CelebrityRoundState Instance { get; private set; }

    [SerializeField]
    bool logSelection = true;

    public CelebrityActionDefinition SelectedAction { get; private set; }
    public bool HasSelectedAction { get; private set; }
    public string LocationRoomName { get; private set; } = string.Empty;
    public Vector3 LocationPoint { get; private set; }
    public bool HasLocationPoint { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureSelection();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void EnsureSelection()
    {
        if (HasSelectedAction)
            return;

        SelectedAction = CelebrityActionCatalog.PickRandom();
        HasSelectedAction = true;
        HasLocationPoint = false;
        LocationRoomName = string.Empty;

        if (SelectedAction.RequiresLocation)
        {
            if (CelebrityRoomUtility.TryPickRandomRoomPoint(out Collider room, out Vector3 point))
            {
                HasLocationPoint = true;
                LocationPoint = point;
                LocationRoomName = room != null ? room.name : "UnknownRoom";
            }
            else if (logSelection)
            {
                Debug.LogWarning(
                    "[CelebrityRound] Acción LC_ elegida pero no hay Room_* con trigger. Se ejecutará in-place.",
                    this);
            }
        }

        if (logSelection)
        {
            if (SelectedAction.RequiresLocation && HasLocationPoint)
            {
                Debug.Log(
                    $"[CelebrityRound] Acción: {SelectedAction.Id} | Habitación: {LocationRoomName} | Punto: {LocationPoint}",
                    this);
            }
            else
            {
                Debug.Log($"[CelebrityRound] Acción: {SelectedAction.Id} (cualquier lugar)", this);
            }
        }
    }

    public static CelebrityRoundState GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        var go = new GameObject("CelebrityRoundState");
        return go.AddComponent<CelebrityRoundState>();
    }
}
