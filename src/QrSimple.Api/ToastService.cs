namespace QrSimple.Api;

public enum ToastKind
{
    Success,
    Error,
}

public sealed class ToastService
{
    public event Action<string, ToastKind>? OnShow;

    public void Success(string message) => OnShow?.Invoke(message, ToastKind.Success);
    public void Error(string message) => OnShow?.Invoke(message, ToastKind.Error);
}
