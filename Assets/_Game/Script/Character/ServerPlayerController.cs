using UnityEngine;

// Deprecated in favor of ClientNetworkTransform (Direction 1)
public class ServerPlayerController : MonoBehaviour
{
    public void Initialize(PlayerController playerController) { }
    public void OnInputReceived(PlayerController.InputPayload input) { }
    public void ResetState(Vector2 position) { }
}
