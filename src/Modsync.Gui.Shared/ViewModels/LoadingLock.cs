// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;

namespace Modsync.Gui.Shared.ViewModels;

public sealed partial class LoadingLock : ObservableObject
{
    private int _counter;

    [ObservableProperty]
    private bool _isLoading;

    public LoadingLock() { }

    public IDisposable Lock()
    {
        Interlocked.Increment(ref _counter);
        IsLoading = true;
        return new Releaser(this);
    }

    private void Release()
    {
        Interlocked.Decrement(ref _counter);
        IsLoading = _counter > 0;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly LoadingLock _parent;
        private int _disposed;

        public Releaser(LoadingLock parent) => _parent = parent;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _parent.Release();
        }
    }
}
