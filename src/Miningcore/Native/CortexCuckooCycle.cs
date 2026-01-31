using System.Diagnostics;
using System.Runtime.InteropServices;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using CC = Miningcore.Blockchain.Ethereum.CortexConstants;

// ReSharper disable InconsistentNaming

namespace Miningcore.Native;

public unsafe class CortexCuckooCycle
{
    internal static IMessageBus messageBus;

    [DllImport("libcortexcuckoocycle", EntryPoint = "cortexcuckoocycle_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern int cortexcuckoocycle(byte* header, int inputLength, uint* solution);

    public int Verify(ReadOnlySpan<byte> data, ReadOnlySpan<uint> result)
    {
        Contract.Requires<ArgumentException>(result.Length == CC.CuckarooSolutionSize);

        var sw = Stopwatch.StartNew();

        fixed (byte* header = data)
        {
            fixed (uint* solution = result)
            {
                var res = cortexcuckoocycle(header, data.Length, solution);

                messageBus?.SendTelemetry("CortexCuckooCycle", TelemetryCategory.Hash, sw.Elapsed, true);

                return res;
            }
        }
    }
}