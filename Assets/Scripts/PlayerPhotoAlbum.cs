using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerPhotoAlbum : MonoBehaviour
{
    readonly List<Texture2D> photos = new List<Texture2D>();

    public event Action PhotosChanged;

    public IReadOnlyList<Texture2D> Photos => photos;
    public int Count => photos.Count;

    public void AddPhoto(Texture2D photo)
    {
        if (photo == null)
            return;

        photos.Add(photo);
        PhotosChanged?.Invoke();
    }

    public void Clear()
    {
        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i] != null)
                Destroy(photos[i]);
        }

        photos.Clear();
        PhotosChanged?.Invoke();
    }

    void OnDestroy()
    {
        Clear();
    }
}
