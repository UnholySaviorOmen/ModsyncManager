// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Logging;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Message);
