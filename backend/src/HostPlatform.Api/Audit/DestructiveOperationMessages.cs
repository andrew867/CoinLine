namespace HostPlatform.Api.Audit;

public static class DestructiveOperationMessages
{
    /// <summary>Returned as JSON <c>error</c> when <c>confirm=true</c> is required.</summary>
    public const string RepeatWithConfirmTrue = "destructive operation: repeat with query parameter confirm=true";
}
