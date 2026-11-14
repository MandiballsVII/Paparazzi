using System;

[Serializable]
public struct CelebrityActionDefinition
{
    public string Id;
    public bool RequiresLocation;

    public CelebrityActionDefinition(string id, bool requiresLocation)
    {
        Id = id;
        RequiresLocation = requiresLocation;
    }
}

public static class CelebrityActionCatalog
{
    public static readonly CelebrityActionDefinition[] PrototypeActions =
    {
        new("LC_Saltar", true),
        new("LC_Caer", true),
        new("LC_Sentarse", true),
        new("Empezar", false),
        new("Acabar", false),
        new("Soplar", false),
        new("Dormir", false)
    };

    public static CelebrityActionDefinition PickRandom()
    {
        int index = UnityEngine.Random.Range(0, PrototypeActions.Length);
        return PrototypeActions[index];
    }
}
