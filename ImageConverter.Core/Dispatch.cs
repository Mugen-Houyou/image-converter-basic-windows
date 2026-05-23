namespace ImageConverter.Core;

public static class UiDispatch
{
    public static Action<Action>? InvokeAsync { get; set; }
}
