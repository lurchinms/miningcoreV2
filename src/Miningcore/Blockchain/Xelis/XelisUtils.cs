using System;
using System.Numerics;
using Miningcore.Extensions;

namespace Miningcore.Blockchain.Xelis;

public static class XelisUtils
{
    public static byte[] DifficultyToTarget(double difficulty)
    {
        Span<byte> paddedBytes = stackalloc byte[XelisConstants.TargetPaddingLength];

        if(difficulty == 0.0)
            return paddedBytes.ToArray();

        var targetBigInteger = BigInteger.Multiply(BigInteger.Divide(XelisConstants.Diff1Target, new BigInteger((ulong) (difficulty * 255d))), new BigInteger(255));
        var targetBytes = targetBigInteger.ToByteArray().AsSpan();

        var padLength = paddedBytes.Length - targetBytes.Length;

        if(padLength > 0)
            targetBytes.CopyTo(paddedBytes[padLength..]);
        else
            targetBytes.Slice(targetBytes.Length - paddedBytes.Length, paddedBytes.Length).CopyTo(paddedBytes);

        return paddedBytes.ToArray();
    }

    public static long UnixTimeStamp(DateTime date)
    {
        long unixTimeStamp = date.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0).Ticks;
        unixTimeStamp /= TimeSpan.TicksPerMillisecond;
        return unixTimeStamp;
    }

    public static bool CheckDiff(Span<byte> hashBytes, double difficulty)
    {
        byte[] targetBytes = DifficultyToTarget(difficulty);

        return CheckDiff(hashBytes, targetBytes);
    }

    public static bool CheckDiff(Span<byte> hashBytes, byte[] targetBytes)
    {
        for (int i = 0; i < 32; i++)
        {
            if (hashBytes[i] < targetBytes[i])
                return true;

            if (hashBytes[i] > targetBytes[i])
                return false;
        }

        return false;
    }
}
