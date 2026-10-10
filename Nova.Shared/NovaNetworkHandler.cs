using V12.Core;
using V12.Core.Networking;
using V12.Core.Systems;

namespace V12TwoDog
{
    /// <summary>
    /// Nova flavor of the shared V12 network stack: adds XR controller analog
    /// state to outgoing rig sync. Everything else lives in V12 core
    /// (<c>V12.Core.Systems.NetworkHandler</c>) so both hosts run one
    /// implementation.
    /// </summary>
    public sealed class NovaNetworkHandler : NetworkHandler
    {
        public NovaNetworkHandler(GameRoot root, WorldSyncHandler worldSyncHandler, RemotePlayerManager remotePlayerManager)
            : base(root, worldSyncHandler, remotePlayerManager)
        {
        }

        protected override void CaptureRigExtras(PlayerSyncDTO dto)
        {
            var xr = Root.Registry.Get<XRTrackingService>("XRTrackingService");
            if (xr == null) return;
            dto.LeftTrigger = xr.LeftTrigger;
            dto.LeftGrip = xr.LeftGrip;
            dto.RightTrigger = xr.RightTrigger;
            dto.RightGrip = xr.RightGrip;
        }
    }
}
