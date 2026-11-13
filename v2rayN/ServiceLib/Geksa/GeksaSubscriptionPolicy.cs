namespace ServiceLib.Geksa;

public static class GeksaSubscriptionPolicy
{
    public const int DefaultIntervalMinutes = 5 * 60;

    // Migrate our old two-minute default without enabling disabled schedules
    // or overwriting other explicitly configured intervals.
    public static int MigrateInterval(int minutes) => minutes == 2 ? DefaultIntervalMinutes : minutes;
}
