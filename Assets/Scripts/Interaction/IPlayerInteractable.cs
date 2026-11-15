using UnityEngine;

public interface IPlayerInteractable
{
    string Prompt { get; }
    float InteractDistance { get; }
    bool CanInteract(GameObject interactor);
    void Interact(GameObject interactor);
}
