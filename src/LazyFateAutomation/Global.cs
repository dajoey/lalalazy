global using ECommons.DalamudServices;
global using ECommons.GameHelpers;
global using ECommons.Logging;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Numerics;

global using Dalamud.Game.ClientState.Conditions;
global using Dalamud.Game.Addon.Lifecycle;
global using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
global using Dalamud.Interface.Utility;
global using static LazyFateAutomation.Plugin;

global using static ECommons.GenericHelpers;
global using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;
global using Callback = ECommons.Automation.Callback;

global using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
global using DGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
global using Sheets = Lumina.Excel.Sheets;
global using LazyFateAutomation.Helpers.IPC;
global using LazyFateAutomation.Helpers.TaskSystem;
global using LazyFateAutomation.Helpers.Services;
global using LazyFateAutomation.Helpers.Extensions;
global using clib.Extensions;
global using LazyFateAutomation.Helpers.Utils;
global using LazyFateAutomation;

global using static LazyFateAutomation.Helpers.Extensions.ImGuiExtensions;
