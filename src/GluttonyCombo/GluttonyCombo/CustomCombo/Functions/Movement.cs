using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using GluttonyCombo.Services;

namespace GluttonyCombo.CustomComboNS.Functions;

internal abstract partial class CustomComboFunctions
{
    private static DateTime? movementStarted;
    private static DateTime? movementStopped;

    /// <summary> Checks if the player is moving. </summary>
    public static unsafe bool IsMoving(bool ignoreConfig = false)
    {
        if (Player.Object is null)
            return false;
        var agentMap = AgentMap.Instance();
        if (agentMap is null)
            return false;

        bool isMoving = agentMap->IsPlayerMoving || Player.IsJumping;

        if (isMoving)
        {
            if (movementStarted is null)
                movementStarted = DateTime.Now;

            movementStopped = null;
        }
        else
        {
            if (movementStopped is null)
                movementStopped = DateTime.Now;

            movementStarted = null;
        }

        return isMoving && (ignoreConfig ? true : TimeMoving.TotalSeconds >= Service.Configuration.MovementLeeway);
    }

    public unsafe static bool IsDashing() => MovementHook.Instance != null && MovementHook.Instance->Dashing == 1;

    public static TimeSpan TimeMoving => movementStarted is null ? TimeSpan.Zero : (DateTime.Now - movementStarted.Value);

    public static TimeSpan TimeStoodStill => movementStopped is null ? TimeSpan.Zero : (DateTime.Now - movementStopped.Value);
}

/// <summary>
///     RMIWalk hook. v1 only read the wish direction; Smart Movement v2 also
///     WRITES it: when the user presses nothing and the mover has a steering
///     direction, the input sums are overwritten so the character walks there
///     (the same mechanism BossMod's MovementOverride uses). The user's own
///     input always wins the frame it is present; a held escape-hatch key wins
///     too; a running vnavmesh path is never fought.
/// </summary>
internal unsafe class MovementHook : IDisposable
{
    public static MoveControllerSubMemberForMine* Instance = null!;

    /// <summary> Raw user movement input was non-zero on the last RMIWalk call. </summary>
    public static bool UserMoving;

    /// <summary> The override wrote a direction on the last RMIWalk call. </summary>
    public static bool OverrideActive;

    /// <summary> Both input-enabled probes resolved; without them the mover never writes input. </summary>
    public static bool SteeringAvailable;

    private delegate bool RMIWalkIsInputEnabled(void* self);
    private RMIWalkIsInputEnabled? _rmiWalkIsInputEnabled1;
    private RMIWalkIsInputEnabled? _rmiWalkIsInputEnabled2;
    private bool[]? _vnavPathIsRunning;
    private static bool _sigLogged;

    private bool VnavFollowing()
    {
        try
        {
            if (_vnavPathIsRunning == null && Svc.PluginInterface.TryGetData<bool[]>("vnav.PathIsRunning", out var data))
                _vnavPathIsRunning = data;
            return _vnavPathIsRunning != null && _vnavPathIsRunning[0];
        }
        catch { return false; }
    }

    private static bool EscapeHatchHeld()
    {
        try
        {
            var mode = global::GluttonyCombo.AutoRotation.AutoRotationController.cfg?.DPSSettings.SmartMoverEscapeHatch ?? 0;
            var io = Dalamud.Bindings.ImGui.ImGui.GetIO();
            return mode switch { 1 => io.KeyCtrl, 2 => io.KeyAlt, 3 => io.KeyShift, _ => false };
        }
        catch { return false; }
    }

    /// <summary> Legacy movement type (character moves relative to the camera). </summary>
    public static bool ReadLegacyMode()
    {
        try { return Svc.GameConfig.UiControl.TryGetUInt("MoveMode", out var mode) && mode == 1; }
        catch { return false; }
    }

    /// <summary> Camera azimuth in game rotation convention (direction from the character to the camera), for legacy mode. </summary>
    private static float CameraAzimuth()
    {
        try
        {
            var cam = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance()->GetActiveCamera();
            if (cam == null) return Player.Rotation;
            var view = cam->CameraBase.SceneCamera.ViewMatrix;
            return MathF.Atan2(view.M13, view.M33);
        }
        catch { return Player.Rotation; }
    }

    /// <summary> Forward basis of the movement input: character rotation (standard) or camera azimuth + 180 degrees (legacy). </summary>
    private static float ForwardMovementDirection() =>
        global::GluttonyCombo.AutoRotation.SmartMover.LegacyMode ? CameraAzimuth() + MathF.PI : Player.Rotation;

    private delegate void RMIWalkDelegate(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour))]
    private readonly Hook<RMIWalkDelegate> _rmiWalkHook = null!;

    private void RMIWalkDetour(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        _rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);

        Instance = self;
        OverrideActive = false;
        if (bAdditiveUnk != 0)
            return;

        UserMoving = *sumLeft != 0 || *sumForward != 0;
        if (UserMoving || !SteeringAvailable)
            return;

        try
        {
            if (_rmiWalkIsInputEnabled1 is null || _rmiWalkIsInputEnabled2 is null ||
                !_rmiWalkIsInputEnabled1(self) || !_rmiWalkIsInputEnabled2(self) || VnavFollowing() || EscapeHatchHeld())
                return;

            var steer = global::GluttonyCombo.AutoRotation.SmartMover.CurrentSteer();
            if (steer is not { } dir || dir == default)
                return;

            // desired world direction (X, Z) in game rotation convention: rot = atan2(x, z), dir = (sin rot, cos rot)
            var desired = MathF.Atan2(dir.X, dir.Y);
            var rel = desired - ForwardMovementDirection();
            *sumLeft = MathF.Sin(rel);
            *sumForward = MathF.Cos(rel);
            OverrideActive = true;
        }
        catch
        {
            // never let a steering fault leak into the game's input path
        }
    }

    public void Dispose()
    {
        _rmiWalkHook?.Dispose();
    }

    internal MovementHook()
    {
        Svc.Hook.InitializeFromAttributes(this);
        _rmiWalkHook.Enable();
        try
        {
            var a1 = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C");
            var a2 = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F");
            _rmiWalkIsInputEnabled1 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(a1);
            _rmiWalkIsInputEnabled2 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(a2);
            SteeringAvailable = true;
        }
        catch (Exception e)
        {
            SteeringAvailable = false;
            if (!_sigLogged)
            {
                _sigLogged = true;
                Svc.Log.Warning($"[SmartMover] movement steering unavailable (input-enabled signatures not found): {e.Message}");
            }
        }
    }
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct MoveControllerSubMemberForMine
{
    [FieldOffset(0x10)] public Vector3 Direction;
    [FieldOffset(0x28)] public float Unk_0x28;
    [FieldOffset(0x38)] public float Unk_0x38;
    [FieldOffset(0x3C)] public byte Moved; // 1 when the character has moved
    [FieldOffset(0x3D)] public byte Rotated; // 1 when the character has rotated
    [FieldOffset(0x3E)] public byte MovementLock;
    [FieldOffset(0x3F)] public byte Unk_0x3F; // non-zero when moving with LMB+RMB
    [FieldOffset(0x40)] public byte Unk_0x40;
    [FieldOffset(0x44)] public float MoveSpeed;
    [FieldOffset(0x50)] public float* MoveSpeedMaximums;
    [FieldOffset(0x80)] public Vector3 ZoningPosition;
    [FieldOffset(0x90)] public float MoveDir;
    [FieldOffset(0x94)] public byte Unk_0x94;
    [FieldOffset(0xA0)] public Vector3 MoveForward; // direction output by MovementUpdate
    [FieldOffset(0xB0)] public float Unk_0xB0;
    [FieldOffset(0xB4)] public byte Unk_0xB4;
    [FieldOffset(0xF2)] public byte Dashing;
    [FieldOffset(0xF3)] public byte Unk_0xF3;
    [FieldOffset(0xF4)] public byte Unk_0xF4;
    [FieldOffset(0xF5)] public byte Unk_0xF5;
    [FieldOffset(0xF6)] public byte Unk_0xF6;
    [FieldOffset(0x104)] public byte Unk_0x104;
    [FieldOffset(0x110)] public Int32 WishdirChanged;
    [FieldOffset(0x114)] public float Wishdir_Horizontal;
    [FieldOffset(0x118)] public float Wishdir_Vertical;
    [FieldOffset(0x120)] public byte Unk_0x120;
    [FieldOffset(0x121)] public byte Rotated1;
    [FieldOffset(0x122)] public byte Unk_0x122;
    [FieldOffset(0x123)] public byte Unk_0x123;
    [FieldOffset(0x125)] public byte Unk_0x125;
    [FieldOffset(0x12A)] public byte Unk_0x12A;
}