using Dalamud;
using Dalamud.Common;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Logging;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;


//using ECommons;
//using ECommons.DalamudServices;
//using ECommons.Reflection;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace PunishLib.ImGuiMethods
{
    public static class AboutTab
    {
        private static string _inputKey = PunishLibMain.SharedConfig.APIKey;
        private static bool openApiSettings = false;
        private static bool showKeyError = false;
        private static bool showSuccess = false;
        private static bool apiTestSuccess = false;
        private static bool apiTestFail = false;
        private static bool disableTestButton = false;
        private static List<string> testedKeys = new();
        private static List<LocalPluginInfo> installedPluginInfo = new();
        private static Regex uuidPattern = new Regex("[a-fA-F\\d]{8}(?:\\-[a-fA-F\\d]{4}){3}\\-[a-fA-F\\d]{12}$");

        static string GetImageURL()
        {
            return Svc.PluginInterface.Manifest.IconUrl ?? "";
        }

        public static void Draw(string pluginName)
        {
            ImGuiEx.ImGuiLineCentered("About1", delegate
            {
                ImGuiEx.Text($"{pluginName} - {Svc.PluginInterface.Manifest.AssemblyVersion}");
            });

            PunishLibMain.About.WithLoveBy();

            ImGuiHelpers.ScaledDummy(10f);
            ImGuiEx.ImGuiLineCentered("About2", delegate
            {
                if (ThreadLoadImageHandler.TryGetTextureWrap(GetImageURL(), out var texture))
                {
                    ImGui.Image(texture.Handle, new(200f, 200f));
                }
            });
            ImGuiHelpers.ScaledDummy(10f);
            ImGuiEx.ImGuiLineCentered("About3", delegate
            {
                /*if (ImGuiEx.IconButton((FontAwesomeIcon)0xf392))
                {
                    GenericHelpers.ShellStart("https://discord.gg/Zzrcc8kmvy");
                }
                ImGui.SameLine();*/
                ImGui.TextWrapped("Join our Discord community for project announcements, updates, and support.");
            });
            ImGuiEx.ImGuiLineCentered("About4", delegate
            {
                if (ImGui.Button("Discord"))
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://discord.gg/Zzrcc8kmvy",
                        UseShellExecute = true
                    });
                }
                ImGui.SameLine();
                if (ImGui.Button("Repository"))
                {
                    ImGui.SetClipboardText("https://love.puni.sh/ment.json");
                    Notify.Success("Link copied to clipboard");
                }
                if (Svc.PluginInterface.Manifest.RepoUrl != null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Source Code"))
                    {
                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = Svc.PluginInterface.Manifest.RepoUrl,
                            UseShellExecute = true
                        });
                    }
                }
                if (PunishLibMain.About.Sponsor != null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Sponsor"))
                    {
                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = PunishLibMain.About.Sponsor,
                            UseShellExecute = true
                        });
                    }
                }

                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog) && !openApiSettings)
                {
                    openApiSettings = true;
                    _inputKey = PunishLibMain.SharedConfig.APIKey;

                    if (uuidPattern.IsMatch(_inputKey))
                    {
                        showKeyError = false;
                        showSuccess = false;
                        apiTestFail = false;
                        apiTestSuccess = false;

                        disableTestButton = false;
                    }
                }

                if (openApiSettings)
                {
                    ImGuiHelpers.ForceNextWindowMainViewport();
                    ImGui.SetNextWindowSize(new System.Numerics.Vector2(200, 300), ImGuiCond.Once);
                    ImGui.Begin($"Puni.sh API Key Settings", ref openApiSettings, ImGuiWindowFlags.AlwaysAutoResize);
                    ImGui.Text("API Key");
                    if (showKeyError)
                        ImGuiEx.Text(ImGuiColors.DalamudRed, "ERROR - Invalid API Key");

                    if (showSuccess)
                        ImGuiEx.Text(ImGuiColors.HealerGreen, "Success - Your key has been saved.");

                    if (apiTestSuccess)
                        ImGuiEx.Text(ImGuiColors.HealerGreen, "Success - You have a valid API key.");

                    if (apiTestFail)
                        ImGuiEx.Text(ImGuiColors.DalamudRed, "ERROR - Your API key is invalid.");

                    ImGui.PushItemWidth(300);
                    if (ImGui.InputText("", ref _inputKey, 100))
                    {
                        showKeyError = false;
                        showSuccess = false;
                        apiTestFail = false;
                        apiTestSuccess = false;

                        disableTestButton = true;
                    }

                    //if (ImGui.Button("Diagnostics Export"))
                    //{
                    //    if (DalamudReflector.TryGetDalamudStartInfo(out startInfo, Svc.PluginInterface))
                    //    PunishLibMain.SharedConfig.FFXIVGameVersion = startInfo.GameVersion?.ToString();

                    //    var pluginManager = DalamudReflector.GetPluginManager();
                    //    var installedPlugins = (System.Collections.IList)pluginManager.GetType().GetProperty("InstalledPlugins").GetValue(pluginManager);

                    //    installedPluginInfo.Clear();
                    //    foreach (var i in installedPlugins)
                    //    {
                    //        try
                    //        {
                    //            LocalPluginInfo plugin = new();
                    //            plugin.Name = (string)i.GetType().GetProperty("Name").GetValue(i);
                    //            plugin.Version = i.GetType().GetProperty("EffectiveVersion").GetValue(i).ToString();

                    //            installedPluginInfo.Add(plugin);
                    //        }
                    //        catch (Exception ex)
                    //        {
                    //            ex.Log();
                    //        }
                    //    }
                    //    PunishLibMain.SharedConfig.InstalledPlugins = JsonConvert.SerializeObject(installedPluginInfo);
                    //    PunishLibMain.SharedConfig.ClientLanguage = Svc.ClientState.ClientLanguage.ToString();

                    //}

                    //ImGui.SameLine();

                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Save))
                    {
                        if (uuidPattern.IsMatch(_inputKey))
                        {
                            showKeyError = false;
                            showSuccess = true;

                            disableTestButton = false;

                            PunishLibMain.SharedConfig.APIKey = _inputKey;
                        }
                        else
                        {
                            showSuccess = false;
                            showKeyError = true;
                        }

                    }

                    ImGui.SameLine();

                    if (disableTestButton)
                        ImGui.BeginDisabled();

                    if (IconButtons.IconTextButton(FontAwesomeIcon.Question, "Test Key"))
                    {
                        if (!string.IsNullOrEmpty(PunishLibMain.SharedConfig.APIKey))
                        {
                            if (!testedKeys.Any(x => x == PunishLibMain.SharedConfig.APIKey))
                            {
                                testedKeys.Add(PunishLibMain.SharedConfig.APIKey);
                                if (API.API.ValidateKey().Result)
                                {
                                    apiTestSuccess = true;
                                    apiTestFail = false;
                                }
                                else
                                {
                                    apiTestSuccess = false;
                                    apiTestFail = true;
                                }
                            }
                        }
                    }

                    if (disableTestButton)
                        ImGui.EndDisabled();

                    ImGui.End();
                }
            });
        }

        private class LocalPluginInfo
        {
            public string Name { get; set; } 

            public string Version { get; set; }
        }
    }
}
