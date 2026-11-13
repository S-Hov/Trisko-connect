namespace ServiceLib.Trisko;

public static class TriskoStartupDefaults
{
    /// <summary>Replace the inherited early-build VirtualHere default with a one-time manual-start default.</summary>
    public static void Apply(UIItem settings)
    {
        if (settings.TriskoVirtualHereStartupPreferenceInitialized) return;
        settings.TriskoAutoStartVirtualHereOnLaunch = false;
        settings.TriskoVirtualHereStartupPreferenceInitialized = true;
    }
}
