// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;

namespace Modsync.Gui.Shared.ViewModels;

public abstract class ViewModel : ObservableObject
{
    public virtual void Dispose() { }
}
