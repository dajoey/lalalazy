using System.Reflection;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using KamiToolKit.Nodes;

namespace LazyHub;

/// <summary>
/// Loads the embedded 64x64 pixel-art icons into native image nodes. The icons are embedded
/// (logical name <c>Resources.&lt;file&gt;</c>), so the zip needs no loose files for them.
/// </summary>
internal sealed class IconLoader(ITextureProvider textures, IFramework framework, IPluginLog log)
{
    private static readonly Assembly Asm = typeof(IconLoader).Assembly;

    /// <summary>
    /// Decodes the icon off the main thread, then hands it to the node on the framework thread.
    /// <paramref name="stillValid"/> is checked on the framework thread right before the node is
    /// touched: the window rebuilds its nodes every time it opens, so a node from a closed window
    /// must never be written to.
    /// </summary>
    public void LoadInto(ImGuiImageNode node, string file, Func<bool> stillValid)
    {
        _ = Task.Run(async () =>
        {
            IDalamudTextureWrap? wrap = null;
            try
            {
                var bytes = ReadResource(file);
                if (bytes == null)
                {
                    log.Warning("Icon resource missing: {File}", file);
                    return;
                }

                wrap = await textures.CreateFromImageAsync(bytes).ConfigureAwait(false);

                await framework.RunOnFrameworkThread(() =>
                {
                    if (!stillValid()) return;

                    node.LoadTexture(wrap);   // the node now owns the texture and disposes it with itself
                    node.TextureSize = wrap.Size;
                    node.MarkDirty();
                    wrap = null;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Could not load icon {File}", file);
            }
            finally
            {
                wrap?.Dispose();   // still ours when the node was gone or the load failed
            }
        });
    }

    private static byte[]? ReadResource(string file)
    {
        using var stream = Asm.GetManifestResourceStream("Resources." + file);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
