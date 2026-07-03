namespace Micalobia.Shapez2.FolderIcons.Services;

public abstract class HookAdapterBase(FolderIcons mod) : DisposableTracker
{
    protected FolderIcons Mod { get; } = mod;

    public void InstallHooks()
    {
        try
        {
            Install();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected abstract void Install();
}
