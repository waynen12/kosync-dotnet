namespace Kosync;

public static class Constants
{
    public const string DevicesTab = "devices";

    public const string BooksTab = "books";

    public const string NoDevicesSyncedText = "No devices have synced yet.";

    public const string NoBooksSyncedText = "No books have synced yet.";

    // A Device's card and detail view list that Device's Documents, so the
    // empty state has to talk about Documents - not Books.
    public const string NoDeviceDocumentsSyncedText = "No documents synced yet";

    public const string NoBookProgressText = "No progress yet";

    public const string ProgressRegressionFlag = "Progress Regression";

    public const string SplitBookFlag = "Split Book";

    public const string ProgressResetFlag = "Progress Reset";

    public const string ProgressResetLabel = "Progress reset";

    public const string ResetProgressButton = "Reset progress";

    public const string ResetProgressFailedError = "Reset failed - document not found.";

    public const string MergeBookIdRequiredError = "Enter a Book # to merge.";

    public const string MergeSameBookError = "Can't merge a Book into itself.";

    public const string MergeBookNotFoundErrorFormat = "Book #{0} was not found.";

    public const string MergeFailedError = "Merge failed.";

    // Deliberately says neither which of the two was wrong nor whether the
    // User exists, is inactive or isn't an Administrator.
    public const string LoginErrorText = "Invalid username or password";
}
