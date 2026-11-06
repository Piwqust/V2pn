namespace v2rayN.Desktop.Common;

public static class DisposableExtensions
{
    public static T AddTo<T>(this T disposable, ICollection<IDisposable> list) where T : IDisposable
    {
        list.Add(disposable);
        return disposable;
    }
}
