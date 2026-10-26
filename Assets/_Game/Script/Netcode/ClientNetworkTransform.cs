using Unity.Netcode.Components;
using UnityEngine;

namespace Unity.Multiplayer.Samples.Utilities.ClientAuthority
{
    /// <summary>
    /// Used for syncing a transform with client side changes. This includes host. Pure server as owner isn't supported.
    /// For transforms that'll always be owned by the server, use the standard NetworkTransform.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        /// <summary>
        /// Used to determine who can write to this transform. Owner client only.
        /// This imposes state to the server. This is putting trust on your clients. Make sure no security-sensitive 
        /// logic depends on this transform.
        /// </summary>
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            SyncPositionX = true;
            SyncPositionY = true;
            SyncPositionZ = false;
            SyncRotAngleX = false;
            SyncRotAngleY = false;
            SyncRotAngleZ = false;
            SyncScaleX = false;
            SyncScaleY = false;
            SyncScaleZ = false;
            Interpolate = true;
        }
    }
}
