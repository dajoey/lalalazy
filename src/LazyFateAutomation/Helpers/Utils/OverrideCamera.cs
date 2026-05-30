using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace LazyFateAutomation.Helpers.Utils;

public unsafe class OverrideCamera : IDisposable {
    public bool Enabled {
        get => _rmiCameraHook.IsEnabled;
        set {
            if (value)
                _rmiCameraHook.Enable();
            else
                _rmiCameraHook.Disable();
        }
    }

    public bool IgnoreUserInput; // if true - override even if user tries to change camera orientation, otherwise override only if user does nothing
    internal Angle DesiredAzimuth = default;
    internal Angle DesiredAltitude = default;
    internal Angle SpeedH = 360.Degrees(); // per second
    internal Angle SpeedV = 360.Degrees(); // per second

    private delegate void RMICameraDelegate(Camera* self, int inputMode, float speedH, float speedV);
    [Signature("40 53 48 83 EC 70 44 0F 29 44 24 ?? 48 8B D9")]
    private readonly Hook<RMICameraDelegate> _rmiCameraHook = null!;

    public OverrideCamera() {
        Svc.Hook.InitializeFromAttributes(this);
        Svc.Log.Print($"RMICamera address: 0x{_rmiCameraHook.Address:X}");
    }

    public void Dispose() {
        _rmiCameraHook.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RMICameraDetour(Camera* self, int inputMode, float speedH, float speedV) {
        _rmiCameraHook.Original(self, inputMode, speedH, speedV);
        if (IgnoreUserInput || inputMode == 0) // let user override...
        {
            var dt = Framework.Instance()->FrameDeltaTime;
            var deltaH = (DesiredAzimuth - self->DirH.Radians()).Normalized();
            var deltaV = (DesiredAltitude - self->DirV.Radians()).Normalized();
            var maxH = SpeedH.Rad * dt;
            var maxV = SpeedV.Rad * dt;
            self->InputDeltaH = Math.Clamp(deltaH.Rad, -maxH, maxH);
            self->InputDeltaV = Math.Clamp(deltaV.Rad, -maxV, maxV);
        }
    }
}
