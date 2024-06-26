using Penumbra.Api.Enums;
using Penumbra.GameData.Files;
using Penumbra.Meta.Manipulations;
using Penumbra.String.Classes;
using Penumbra.Meta;
using static Penumbra.Mods.ItemSwap.ItemSwap;

namespace Penumbra.Mods.ItemSwap;

public class Swap
{
    /// <summary> Any further swaps belonging specifically to this tree of changes. </summary>
    public readonly List<Swap> ChildSwaps = [];

    public IEnumerable<Swap> WithChildren()
        => ChildSwaps.SelectMany(c => c.WithChildren()).Prepend(this);
}

public interface IMetaSwap
{
    public IMetaIdentifier SwapFromIdentifier { get; }
    public IMetaIdentifier SwapToIdentifier   { get; }

    public object SwapFromDefaultEntry { get; }
    public object SwapToDefaultEntry   { get; }
    public object SwapToModdedEntry    { get; }

    public bool SwapToIsDefault      { get; }
    public bool SwapAppliedIsDefault { get; }
}

public sealed class MetaSwap<TIdentifier, TEntry> : Swap, IMetaSwap
    where TIdentifier : unmanaged, IMetaIdentifier
    where TEntry : unmanaged, IEquatable<TEntry>
{
    public TIdentifier SwapFromIdentifier;
    public TIdentifier SwapToIdentifier;

    /// <summary> The default value of a specific meta manipulation that needs to be redirected. </summary>
    public TEntry SwapFromDefaultEntry;

    /// <summary> The default value of the same Meta entry of the redirected item. </summary>
    public TEntry SwapToDefaultEntry;

    /// <summary> The modded value of the same Meta entry of the redirected item, or the same as SwapToDefault if unmodded. </summary>
    public TEntry SwapToModdedEntry;

    /// <summary> Whether SwapToModdedEntry equals SwapToDefaultEntry. </summary>
    public bool SwapToIsDefault { get; }

    /// <summary> Whether the applied meta manipulation does not change anything against the default. </summary>
    public bool SwapAppliedIsDefault { get; }

    /// <summary>
    /// Create a new MetaSwap from the original meta identifier and the target meta identifier.
    /// </summary>
    /// <param name="manipulations">A function that obtains a modded meta entry if it exists. </param>
    /// <param name="manipFromIdentifier"> The original meta identifier. </param>
    /// <param name="manipFromEntry"> The default value for the original meta identifier. </param>
    /// <param name="manipToIdentifier"> The target meta identifier. </param>
    /// <param name="manipToEntry"> The default value for the target meta identifier. </param>
    public MetaSwap(Func<TIdentifier, TEntry?> manipulations, TIdentifier manipFromIdentifier, TEntry manipFromEntry,
        TIdentifier manipToIdentifier, TEntry manipToEntry)
    {
        SwapFromIdentifier   = manipFromIdentifier;
        SwapToIdentifier     = manipToIdentifier;
        SwapFromDefaultEntry = manipFromEntry;
        SwapToDefaultEntry   = manipToEntry;

        SwapToModdedEntry    = manipulations(SwapToIdentifier) ?? SwapToDefaultEntry;
        SwapToIsDefault      = SwapToModdedEntry.Equals(SwapToDefaultEntry);
        SwapAppliedIsDefault = SwapToModdedEntry.Equals(SwapFromDefaultEntry);
    }

    IMetaIdentifier IMetaSwap.SwapFromIdentifier
        => SwapFromIdentifier;

    IMetaIdentifier IMetaSwap.SwapToIdentifier
        => SwapToIdentifier;

    object IMetaSwap.SwapFromDefaultEntry
        => SwapFromDefaultEntry;

    object IMetaSwap.SwapToDefaultEntry
        => SwapToDefaultEntry;

    object IMetaSwap.SwapToModdedEntry
        => SwapToModdedEntry;
}

public sealed class FileSwap : Swap
{
    /// <summary> The file type, used for bookkeeping. </summary>
    public ResourceType Type;

    /// <summary> The binary or parsed data of the file at SwapToModded. </summary>
    public IWritable FileData = GenericFile.Invalid;

    /// <summary> The path that would be requested without manipulated parent files. </summary>
    public string SwapFromPreChangePath = string.Empty;

    /// <summary> The Path that needs to be redirected. </summary>
    public Utf8GamePath SwapFromRequestPath;

    /// <summary> The path that the game should request instead, if no mods are involved. </summary>
    public Utf8GamePath SwapToRequestPath;

    /// <summary> The path to the actual file that should be loaded. This can be the same as SwapToRequestPath or a file on the drive. </summary>
    public FullPath SwapToModded;

    /// <summary> Whether the target file is an actual game file. </summary>
    public bool SwapToModdedExistsInGame;

    /// <summary> Whether the target file could be read either from the game or the drive. </summary>
    public bool SwapToModdedExists
        => FileData.Valid;

    /// <summary> Whether SwapToModded is a path to a game file that equals SwapFromGamePath. </summary>
    public bool SwapToModdedEqualsOriginal;

    /// <summary> Whether the data in FileData was manipulated from the original file. </summary>
    public bool DataWasChanged;

    /// <summary> Whether SwapFromPreChangePath equals SwapFromRequest. </summary>
    public bool SwapFromChanged;

    public string GetNewPath(string newMod)
        => Path.Combine(newMod, new Utf8RelPath(SwapFromRequestPath).ToString());

    public MdlFile? AsMdl()
        => FileData as MdlFile;

    public MtrlFile? AsMtrl()
        => FileData as MtrlFile;

    public AvfxFile? AsAvfx()
        => FileData as AvfxFile;

    /// <summary>
    /// Create a full swap container for a specific file type using a modded redirection set, the actually requested path and the game file it should load instead after the swap.
    /// </summary>
    /// <param name="type">The file type. Mdl and Mtrl have special file loading treatment.</param>
    /// <param name="redirections">A function either returning the path after mod application.</param>
    /// <param name="swapFromRequest">The path the game is going to request when loading the file.</param>
    /// <param name="swapToRequest">The unmodded path to the file the game is supposed to load instead.</param>
    /// <param name="swap">A full swap container with the actual file in memory.</param>
    /// <returns>True if everything could be read correctly, false otherwise.</returns>
    public static FileSwap CreateSwap(MetaFileManager manager, ResourceType type, Func<Utf8GamePath, FullPath> redirections,
        string swapFromRequest, string swapToRequest, string? swapFromPreChange = null)
    {
        var swap = new FileSwap
        {
            Type                  = type,
            FileData              = GenericFile.Invalid,
            DataWasChanged        = false,
            SwapFromPreChangePath = swapFromPreChange ?? swapFromRequest,
            SwapFromChanged       = swapFromPreChange != swapFromRequest,
            SwapFromRequestPath   = Utf8GamePath.Empty,
            SwapToRequestPath     = Utf8GamePath.Empty,
            SwapToModded          = FullPath.Empty,
        };

        if (swapFromRequest.Length == 0
         || swapToRequest.Length == 0
         || !Utf8GamePath.FromString(swapToRequest,   out swap.SwapToRequestPath)
         || !Utf8GamePath.FromString(swapFromRequest, out swap.SwapFromRequestPath))
            throw new Exception($"Could not create UTF8 String for \"{swapFromRequest}\" or \"{swapToRequest}\".");

        swap.SwapToModded = redirections(swap.SwapToRequestPath);
        swap.SwapToModdedExistsInGame =
            !swap.SwapToModded.IsRooted && manager.GameData.FileExists(swap.SwapToModded.InternalName.ToString());
        swap.SwapToModdedEqualsOriginal = !swap.SwapToModded.IsRooted && swap.SwapToModded.InternalName.Equals(swap.SwapFromRequestPath.Path);

        swap.FileData = type switch
        {
            ResourceType.Mdl  => LoadMdl(manager, swap.SwapToModded, out var f) ? f : throw new MissingFileException(type,  swap.SwapToModded),
            ResourceType.Mtrl => LoadMtrl(manager, swap.SwapToModded, out var f) ? f : throw new MissingFileException(type, swap.SwapToModded),
            ResourceType.Avfx => LoadAvfx(manager, swap.SwapToModded, out var f) ? f : throw new MissingFileException(type, swap.SwapToModded),
            _                 => LoadFile(manager, swap.SwapToModded, out var f) ? f : throw new MissingFileException(type, swap.SwapToModded),
        };

        return swap;
    }
}
