using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marca un objeto detectable por visión (jugadores). Registro estático sin Find.
/// </summary>
[DisallowMultipleComponent]
public class PerceptionTarget : MonoBehaviour
{
    static readonly List<PerceptionTarget> Active = new(8);

    [SerializeField]
    Transform aimPoint;

    [SerializeField]
    float aimHeight = 1.2f;

    public static IReadOnlyList<PerceptionTarget> ActiveTargets => Active;

    public Vector3 AimPosition
    {
        get
        {
            if (aimPoint != null)
                return aimPoint.position;
            return transform.position + Vector3.up * aimHeight;
        }
    }

    void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }
}
