using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedTools.Web.Modules;

/// <summary>
/// The on-disk layout of a managed module root (<c>Modules:Root</c>, default <c>/data/modules</c>).
/// Shared by the loader and by whatever uploads drops, so both agree on the contract:
/// <code>
/// {id}/              live drop: dotnet publish output (one *.deps.json + its dll, wwwroot/)
/// {id}/.drop         one line: the drop id, written by the uploader
/// {id}.prev/         the previous drop, for rollback and automatic fallback
/// {id}.disabled/     a crashed drop with no previous drop to fall back to
/// _status/{id}.json  { drop, status: pending|loaded|failed, phase, error, serving, updated }
/// _incoming/         uploader scratch space (same volume, so renames are atomic)
/// _keys/             host data-protection keys
/// .booting           present while the host is starting; left behind if startup crashed
/// </code>
/// An uploader writes <c>{ drop, status: "pending" }</c> (no phase) after moving a new drop live. The
/// loader writes <c>pending</c> with a phase only when it has rolled back on its own.
/// Directories starting with <c>_</c> or <c>.</c>, or ending in <c>.prev</c>/<c>.disabled</c>, are not modules.
/// </summary>
public sealed class ModuleStore(string root)
{
    public const string StatusDirectoryName = "_status";
    public const string IncomingDirectoryName = "_incoming";
    public const string KeysDirectoryName = "_keys";
    public const string BootMarkerName = ".booting";
    public const string DropFileName = ".drop";
    public const string PreviousSuffix = ".prev";
    public const string DisabledSuffix = ".disabled";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string Root { get; } = Path.GetFullPath(root);

    public string ModuleDirectory(string id) => Path.Combine(Root, id);
    public string PreviousDirectory(string id) => Path.Combine(Root, id + PreviousSuffix);
    public string DisabledDirectory(string id) => Path.Combine(Root, id + DisabledSuffix);
    public string StatusPath(string id) => Path.Combine(Root, StatusDirectoryName, id + ".json");
    public string KeysDirectory => Path.Combine(Root, KeysDirectoryName);
    private string BootMarkerPath => Path.Combine(Root, BootMarkerName);

    /// <summary>Ids of the live module drops in the store, in a stable order.</summary>
    public IEnumerable<string> ModuleIds()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name)
                && name[0] != '_' && name[0] != '.'
                && !name.EndsWith(PreviousSuffix, StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    /// <summary>Reads a drop directory's <c>.drop</c> id, or null if it has none.</summary>
    public static string? ReadDropId(string dropDirectory)
    {
        var path = Path.Combine(dropDirectory, DropFileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() is { Length: > 0 } id ? id : null : null;
    }

    /// <summary>Writes a drop directory's <c>.drop</c> id.</summary>
    public static void WriteDropId(string dropDirectory, string dropId) =>
        File.WriteAllText(Path.Combine(dropDirectory, DropFileName), dropId);

    public ModuleDropStatus? ReadStatus(string id)
    {
        var path = StatusPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ModuleDropStatus>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes <c>_status/{id}.json</c> atomically.</summary>
    public void WriteStatus(string id, ModuleDropStatus status)
    {
        var path = StatusPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public bool HasPrevious(string id) => Directory.Exists(PreviousDirectory(id));

    /// <summary>Swaps <c>{id}</c> and <c>{id}.prev</c>.</summary>
    public void SwapWithPrevious(string id)
    {
        var live = ModuleDirectory(id);
        var prev = PreviousDirectory(id);
        var swap = Path.Combine(Root, $"{IncomingDirectoryName}-swap-{id}-{Guid.NewGuid():N}");

        Directory.Move(live, swap);
        Directory.Move(prev, live);
        Directory.Move(swap, prev);
    }

    /// <summary>Moves <c>{id}</c> to <c>{id}.disabled</c>, replacing any older disabled drop.</summary>
    public void Disable(string id)
    {
        var disabled = DisabledDirectory(id);
        if (Directory.Exists(disabled))
        {
            Directory.Delete(disabled, recursive: true);
        }
        Directory.Move(ModuleDirectory(id), disabled);
    }

    public bool BootMarkerExists => File.Exists(BootMarkerPath);

    public void WriteBootMarker()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(BootMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
    }

    public void ClearBootMarker()
    {
        if (File.Exists(BootMarkerPath))
        {
            File.Delete(BootMarkerPath);
        }
    }
}

/// <summary>The status of a managed module, as stored in <c>_status/{id}.json</c>.</summary>
/// <param name="Drop">The most recently attempted drop id.</param>
/// <param name="Status"><c>pending</c> (uploaded, not yet loaded), <c>loaded</c>, or <c>failed</c>.</param>
/// <param name="Phase">The failing phase when <paramref name="Status"/> is <c>failed</c>.</param>
/// <param name="Error">The failure message when <paramref name="Status"/> is <c>failed</c>.</param>
/// <param name="Serving">The drop id actually serving traffic, or null if none.</param>
/// <param name="Updated">When this status was written.</param>
public sealed record ModuleDropStatus(
    string? Drop,
    string Status,
    string? Phase,
    string? Error,
    string? Serving,
    DateTimeOffset Updated)
{
    public const string Pending = "pending";
    public const string Loaded = "loaded";
    public const string Failed = "failed";
}
