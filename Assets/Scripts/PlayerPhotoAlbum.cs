using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerPhotoAlbum : MonoBehaviour
{
    readonly List<PhotoShot> shots = new();

    public event Action PhotosChanged;

    public IReadOnlyList<PhotoShot> Shots => shots;
    public int Count => shots.Count;

    /// <summary>Compat UI: solo texturas.</summary>
    public IReadOnlyList<Texture2D> Photos
    {
        get
        {
            var list = new List<Texture2D>(shots.Count);
            for (int i = 0; i < shots.Count; i++)
            {
                if (shots[i] != null && shots[i].Texture != null)
                    list.Add(shots[i].Texture);
            }

            return list;
        }
    }

    public bool HasControversialActionShot
    {
        get
        {
            for (int i = 0; i < shots.Count; i++)
            {
                if (shots[i] != null && shots[i].IsControversialActionShot)
                    return true;
            }

            return false;
        }
    }

    public void AddPhoto(Texture2D photo)
    {
        if (photo == null)
            return;

        AddShot(new PhotoShot(photo, new PhotoEvaluation(), transform));
    }

    public void AddShot(PhotoShot shot)
    {
        if (shot == null || shot.Texture == null)
            return;

        shots.Add(shot);
        PhotosChanged?.Invoke();
    }

    public void Clear()
    {
        for (int i = 0; i < shots.Count; i++)
        {
            if (shots[i] != null && shots[i].Texture != null)
                Destroy(shots[i].Texture);
        }

        shots.Clear();
        PhotosChanged?.Invoke();
    }

    void OnDestroy()
    {
        Clear();
    }
}
