using Radzen;

namespace PcsRemote.Web;

internal sealed class RadzenConfirmDialogService : IConfirmDialogService
{
    private readonly DialogService _dialogService;

    public RadzenConfirmDialogService(DialogService dialogService)
        => _dialogService = dialogService;

    public Task<bool?> ConfirmAsync(string message, string title)
        => _dialogService.Confirm(message, title);
}
