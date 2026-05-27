using System;
using System.Collections.Generic;
using ILogger = Core.Logging.ILogger;

namespace FolderIcons;

public abstract class ModHandler(FolderIcons mod) : IDisposable
{
    protected FolderIcons Mod { get; } = mod;
    protected ILogger Logger => Mod.Logger;
    private readonly List<IDisposable> _hooks = [];

    protected T Track<T>(T hook) where T : IDisposable
    {
        _hooks.Add(hook);
        return hook;
    }

    public void Dispose()
    {
        for (var i = _hooks.Count - 1; i >= 0; --i) _hooks[i].Dispose();
        _hooks.Clear();
    }
}