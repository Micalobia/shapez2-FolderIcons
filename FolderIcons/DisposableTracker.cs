using System;
using System.Collections.Generic;
using UnityEngine;

namespace Micalobia.Shapez2.FolderIcons;

public abstract class DisposableTracker : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    protected T Track<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    public virtual void Dispose()
    {
        for (var i = _disposables.Count - 1; i >= 0; --i)
            try
            {
                _disposables[i].Dispose();
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to dispose tracked object '{_disposables[i].GetType().Name}': {ex}");
            }

        _disposables.Clear();
    }
}
