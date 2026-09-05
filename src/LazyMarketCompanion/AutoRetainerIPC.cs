using ECommons.DalamudServices;
using System;
using System.Linq;

namespace LazyMarketCompanion;

/// <summary>
/// Thin client for AutoRetainer's IPC surface. Wire names are the ApiConsts of
/// github.com/PunishXIV/AutoRetainerAPI (MIT); verified against AutoRetainer 4.6.x source.
///
/// Postprocess contract (AutoRetainer/Scheduler/Tasks/TaskPostprocessRetainerIPC.cs):
///  1. AR fires OnRetainerAdditionalTask(retainer) while the retainer's SelectString menu is open,
///     after its own venture / entrust / vendor work.
///  2. A plugin that wants the retainer calls RequestPostprocess(ownInternalName).
///  3. AR then fires OnRetainerReadyForPostprocess(pluginName, retainer) and BLOCKS (no timeout)
///     until that plugin calls FinishPostprocessRequest().
/// </summary>
public sealed class AutoRetainerIPC : IDisposable
{
  public const string Name = "AutoRetainer";

  private const string OnRetainerAdditionalTask = "AutoRetainer.OnRetainerAdditionalTask";
  private const string OnRetainerReadyForPostprocess = "AutoRetainer.OnRetainerReadyForPostprocess";
  private const string RequestRetainerPostProcess = "AutoRetainer.RequestPostprocess";
  private const string FinishRetainerPostprocessRequest = "AutoRetainer.FinishPostprocessRequest";
  private const string GetSuppressedName = "AutoRetainer.GetSuppressed";
  private const string SetSuppressedName = "AutoRetainer.SetSuppressed";

  public static bool Installed => Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == Name && x.IsLoaded);

  private static AutoRetainerIPC? _instance;
  public static AutoRetainerIPC? Instance => _instance;

  /// <summary>AR asks "anyone want this retainer?" — SelectString menu is open.</summary>
  public event Action<string>? OnRetainerPostprocessStep;

  /// <summary>AR has handed the retainer to us and is waiting for <see cref="FinishRetainerPostProcess"/>.</summary>
  public event Action<string>? OnRetainerReadyToPostprocess;

  private readonly string _pluginName;
  private bool _finished = true;

  private AutoRetainerIPC()
  {
    _pluginName = Svc.PluginInterface.InternalName;
    Svc.PluginInterface.GetIpcSubscriber<string, object>(OnRetainerAdditionalTask).Subscribe(OnAdditionalTask);
    Svc.PluginInterface.GetIpcSubscriber<string, string, object>(OnRetainerReadyForPostprocess).Subscribe(OnReadyForPostprocess);
  }

  public static void Initialize()
  {
    if (Installed && _instance == null)
      _instance = new AutoRetainerIPC();
  }

  public static void DisposeInstance()
  {
    _instance?.Dispose();
    _instance = null;
  }

  public void Dispose()
  {
    try
    {
      Svc.PluginInterface.GetIpcSubscriber<string, object>(OnRetainerAdditionalTask).Unsubscribe(OnAdditionalTask);
      Svc.PluginInterface.GetIpcSubscriber<string, string, object>(OnRetainerReadyForPostprocess).Unsubscribe(OnReadyForPostprocess);
      // Never leave AR blocked on us if we unload mid-postprocess.
      if (!_finished)
        FinishRetainerPostProcess();
    }
    catch (Exception ex)
    {
      Svc.Log.Warning(ex, "[LMC] AutoRetainer IPC dispose");
    }
  }

  private void OnAdditionalTask(string retainer)
  {
    try { OnRetainerPostprocessStep?.Invoke(retainer); }
    catch (Exception ex) { Svc.Log.Error(ex, "[LMC] OnRetainerPostprocessStep handler"); }
  }

  private void OnReadyForPostprocess(string plugin, string retainer)
  {
    if (plugin != _pluginName)
      return;

    _finished = false;
    try { OnRetainerReadyToPostprocess?.Invoke(retainer); }
    catch (Exception ex)
    {
      Svc.Log.Error(ex, "[LMC] OnRetainerReadyToPostprocess handler; releasing AutoRetainer");
      FinishRetainerPostProcess();
    }
  }

  /// <summary>Call only from inside <see cref="OnRetainerPostprocessStep"/>.</summary>
  public void RequestRetainerPostprocess()
  {
    Svc.PluginInterface.GetIpcSubscriber<string, object>(RequestRetainerPostProcess).InvokeAction(_pluginName);
  }

  /// <summary>Release AutoRetainer. Idempotent.</summary>
  public void FinishRetainerPostProcess()
  {
    if (_finished) return;
    _finished = true;
    Svc.PluginInterface.GetIpcSubscriber<object>(FinishRetainerPostprocessRequest).InvokeAction();
    Svc.Log.Information("[LMC] AutoRetainer postprocess released");
  }

  public bool PostprocessPending => !_finished;

  // ----- Suppress (inherited from Dagobert; keeps AR idle while a manual sweep drives the retainer UI) -----

  public static bool Suppressed()
  {
    if (_instance == null) return false;
    try { return Svc.PluginInterface.GetIpcSubscriber<bool>(GetSuppressedName).InvokeFunc(); }
    catch { return false; }
  }

  public static bool Suppressed(bool value)
  {
    if (_instance == null) return true;
    try
    {
      Svc.Log.Debug($"[LMC] AR Suppressed={value}");
      Svc.PluginInterface.GetIpcSubscriber<bool, object>(SetSuppressedName).InvokeAction(value);
    }
    catch (Exception ex) { Svc.Log.Warning(ex, "[LMC] AR SetSuppressed"); }
    return true;
  }
}
