// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Pipes;
using FluentAssertions;
using Modsync.Core.Nxm;
using ModsyncManager.NxmHandler;

namespace ModsyncManager.NxmHandler.Tests;

/// <summary>
/// Тесты PipeClient с реальным NamedPipeServerStream.
///
/// PipeClient.PipeName — прокси на NxmPipeName.Value. Тесты
/// создают сервер с этим именем, запускают клиента,
/// проверяют доставку.
///
/// Каждый тест — свой сервер. xUnit запускает тесты одного класса
/// последовательно по умолчанию, так что конфликта имён нет.
/// Между классами — параллелизм, но у нас один класс с pipe-тестами.
/// </summary>
public class PipeClientTests
{
    [Fact]
    public void PipeName_MatchesCoreConstant()
    {
        // Защита от рассинхрона между handler-ом и receiver-ом.
        PipeClient.PipeName.Should().Be(NxmPipeName.Value);
    }

    [Fact]
    public async Task TrySendAsync_ServerRunning_DeliversUrl()
    {
        var received = new TaskCompletionSource<string>();

        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                PipeClient.PipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync();

            using var reader = new StreamReader(server);
            var line = await reader.ReadLineAsync();
            received.TrySetResult(line ?? "");
        });

        // Даём серверу встать на слух.
        await Task.Delay(50);

        const string url = "nxm://skyrimspecialedition/mods/1/files/2";
        var ok = await PipeClient.TrySendAsync(
            url, PipeClient.DefaultTimeout);

        ok.Should().BeTrue();
        (await received.Task).Should().Be(url);

        await serverTask;
    }

    [Fact]
    public async Task TrySendAsync_NoServer_ReturnsFalseAfterTimeout()
    {
        var url = "nxm://skyrimspecialedition/mods/1/files/2";

        var ok = await PipeClient.TrySendAsync(
            url, TimeSpan.FromMilliseconds(200));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task TrySendAsync_EmptyUrl_ReturnsFalseImmediately()
    {
        var ok = await PipeClient.TrySendAsync(
            "", PipeClient.DefaultTimeout);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task TrySendAsync_WhitespaceUrl_ReturnsFalseImmediately()
    {
        var ok = await PipeClient.TrySendAsync(
            "   ", PipeClient.DefaultTimeout);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task TrySendAsync_ExternallyCancelled_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ok = await PipeClient.TrySendAsync(
            "nxm://game/mods/1/files/2",
            PipeClient.DefaultTimeout,
            cts.Token);

        ok.Should().BeFalse();
    }
}
