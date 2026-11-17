namespace Kosync.Services;

// Which pushes in a Document's history were a Progress Regression (issue
// #38). A regression is a push that *was blocked* when it arrived, and
// nothing stored on the SyncEvent says that: IsCurrent is the Document's
// single current-progress pointer, so every push a later one superseded
// reads as not-current too - including the ordinary forward pushes that
// were promoted at the time.
//
// So the flag is derived rather than read, by replaying the promotion rule
// in Controllers/SyncController.cs over the history oldest-first: a push was
// blocked iff it was lower than the Document's current progress at the
// moment it arrived. No second stored field, which would be a second thing
// that can disagree with the promotion rule.
//
// Lives here beside the other rules rather than in the view, so the one
// statement of what a regression is holds wherever the flag is shown.
public static class ProgressRegressionRule
{
    // Takes and returns the merged timeline BookDashboardService builds -
    // newest first, reset markers included - because the replay has to see
    // every entry that moved the ceiling in the order it arrived.
    public static List<SyncEventSummary> Annotate(IEnumerable<SyncEventSummary> historyNewestFirst)
    {
        var history = historyNewestFirst.ToList();

        var ceiling = 0m;
        var hasCurrent = false;

        // Oldest first: promotion ran forwards, so the replay has to.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            // A reset clears the ceiling exactly as
            // BookDashboardService.ResetProgressAsync clears the pointer, so
            // the next push is promoted whatever its percentage - it has
            // nothing left to regress against. The marker itself is not a
            // push and so is not a regression either.
            //
            // Every reset is in the timeline, not only the latest: resets are
            // recorded one row each (ProgressReset, issue #40), so clearing
            // the ceiling per marker is enough to get a Document reset twice
            // right - the push after the earlier reset is measured against
            // nothing, same as the push after the newer one.
            if (history[i].IsReset)
            {
                ceiling = 0m;
                hasCurrent = false;
                continue;
            }

            var promoted = !hasCurrent || history[i].Percentage >= ceiling;

            history[i] = history[i] with { BlockedRegression = !promoted };

            if (promoted)
            {
                ceiling = history[i].Percentage;
                hasCurrent = true;
            }
        }

        return history;
    }
}
