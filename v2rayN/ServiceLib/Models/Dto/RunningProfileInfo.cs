namespace ServiceLib.Models.Dto;

/// <summary>Identity of the configuration loaded into the managed core, independent of subscription edits.</summary>
public sealed record RunningProfileInfo(
    string IndexId, string Name, EConfigType Protocol, string Address, int Port, ECoreType CoreType)
{
    public static RunningProfileInfo From(CoreConfigContext context)
    {
        var profile = context.RootNode ?? context.Node;
        return new(profile.IndexId,
            string.IsNullOrWhiteSpace(profile.Remarks) ? profile.Address : profile.Remarks,
            profile.ConfigType, profile.Address, profile.Port, context.RunCoreType);
    }
}
