public sealed class ProbeState
{
    private int failures;
    private int successes;
    private bool confirmedDown;
    public bool HasBeenOnline { get; private set; }
    public string LastPublished { get; set; } = "unknown";
    public string Unreachable() { failures = successes = 0; return "unreachable"; }
    public string Apply(bool? success)
    {
        if (success is null) { failures = successes = 0; return "probe-error"; }
        if (success.Value)
        {
            failures = 0; successes = Math.Min(2, successes + 1);
            if (successes >= 2) { confirmedDown = false; HasBeenOnline = true; return "online"; }
            return "pending";
        }
        successes = 0; failures = Math.Min(3, failures + 1);
        if (failures >= 3) { confirmedDown = true; return "offline"; }
        return confirmedDown ? "offline" : "pending";
    }
}
