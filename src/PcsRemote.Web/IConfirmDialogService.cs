namespace PcsRemote.Web;

/// <summary>
/// Wraps Radzen's <see cref="DialogService.Confirm"/> to allow injection of a
/// testable interface at the layer boundary.
/// </summary>
public interface IConfirmDialogService
{
    /// <summary>
    /// Shows a confirmation dialog and returns the result.
    /// Returns <c>true</c> if confirmed, <c>false</c> if cancelled, <c>null</c> if dismissed.
    /// </summary>
    Task<bool?> ConfirmAsync(string message, string title);
}
