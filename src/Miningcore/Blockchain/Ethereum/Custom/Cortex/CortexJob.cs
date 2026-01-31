using System;
using System.Globalization;
using System.Numerics;
using System.Reactive.Threading.Tasks;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Stratum;
using Miningcore.Util;
using NBitcoin;
using NLog;

namespace Miningcore.Blockchain.Ethereum.Custom.Cortex;

public class CortexJob : EthereumJob
{
    protected CortexCuckooCycle cortexCuckooCycleHasher;
    protected Sha3_256 sha3Hasher;
    protected Blake2b blake2bHasher;

    public CortexJob(string id, EthereumBlockTemplate blockTemplate, ILogger logger, IEthashLight ethash) : base(id, blockTemplate, logger, ethash)
    {
        this.cortexCuckooCycleHasher = new CortexCuckooCycle();
        this.sha3Hasher = new Sha3_256();
        this.blake2bHasher = new Blake2b();
    }

    protected virtual byte[] SerializeHeader(ulong nonce)
    {
        using(var stream = new MemoryStream(CortexConstants.CuckarooHeaderNonceSize))
        {
            var bw = new BinaryWriter(stream);

            bw.Write(BlockTemplate.Header.HexToByteArray());
            bw.Write((BitConverter.IsLittleEndian ? BitConverter.GetBytes(nonce) : BitConverter.GetBytes(nonce).ReverseInPlace())); // cortex-cuckoo-cycles expects a little endian format.

            return stream.ToArray();
        }
    }

    protected virtual uint[] SerializeSolution(string solution)
    {
        // allocate a uint array of size 42
        var solutionUints = new uint[CortexConstants.CuckarooSolutionSize];
        var solutionBytes = (Span<byte>) solution.HexToByteArray();

        // fill the buffer with the big-endian representation of each uint in solution
        for (int i = 0; i < solutionUints.Length; i++)
        {
            var slice = solutionBytes.Slice(i * 4, 4);

            solutionUints[i] = BitConverter.ToUInt32(slice);
        }

        return solutionUints;
    }

    protected virtual byte[] SerializeCoinbase(uint[] solution)
    {
        // allocate a byte array of size 42 * 4
        var solutionBytes = new byte[solution.Length * 4];

        // fill the buffer with the big-endian representation of each uint in solution
        for (int i = 0; i < solution.Length; i++)
        {
            var uintBytes = (!BitConverter.IsLittleEndian) ? BitConverter.GetBytes(solution[i]).ReverseInPlace() : BitConverter.GetBytes(solution[i]); // sha3_256 expects a big endian format.

            uintBytes.CopyTo(solutionBytes, i * 4);
        }

        var coinbaseBytes = new byte[32];
        sha3Hasher.Digest(solutionBytes, coinbaseBytes);

        return coinbaseBytes;
    }

    public override async Task<SubmitResult> ProcessShareAsync(StratumConnection worker,
        string workerName, string fullNonceHex, string solution, CancellationToken ct)
    {
        // dupe check
        lock(workerNonces)
        {
            RegisterNonce(worker, fullNonceHex);
        }

        var context = worker.ContextAs<EthereumWorkerContext>();

        if(!ulong.TryParse(fullNonceHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fullNonce))
            throw new StratumException(StratumError.MinusOne, "bad nonce " + fullNonceHex);

        var solutionBytes = SerializeSolution(solution);

        await Task.Run( () =>
        {
            var headerBytes = SerializeHeader(fullNonce);

            if(cortexCuckooCycleHasher.Verify(headerBytes, solutionBytes) > 0)
                throw new StratumException(StratumError.MinusOne, "bad hash");
        }, ct);

        var resultBytes = SerializeCoinbase(solutionBytes);

        // test if share meets at least workers current difficulty
        resultBytes.ReverseInPlace();
        var resultValue = new uint256(resultBytes);
        var resultValueBig = resultBytes.AsSpan().ToBigInteger();
        var shareDiff = (double) BigInteger.Divide(EthereumConstants.BigMaxValue, resultValueBig) / EthereumConstants.Pow2x32;
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;
        var isBlockCandidate = resultValue <= blockTarget;

        if(!isBlockCandidate && ratio < 0.99)
        {
            // check if share matched the previous difficulty from before a vardiff retarget
            if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
            {
                ratio = shareDiff / context.PreviousDifficulty.Value;

                if(ratio < 0.99)
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                // use previous difficulty
                stratumDifficulty = context.PreviousDifficulty.Value;
            }

            else
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
        }

        var share = new Share
        {
            BlockHeight = (long) BlockTemplate.Height,
            IpAddress = worker.RemoteEndpoint?.Address?.ToString(),
            Miner = context.Miner,
            Worker = workerName,
            UserAgent = context.UserAgent,
            IsBlockCandidate = isBlockCandidate,
            Difficulty = stratumDifficulty * EthereumConstants.Pow2x32
        };

        if(share.IsBlockCandidate)
        {
            fullNonceHex = "0x" + fullNonceHex;
            var headerHash = BlockTemplate.Header;
            var mixHash = resultBytes.ToHexString(true);

            share.TransactionConfirmationData = "";

            return new SubmitResult(share, fullNonceHex, headerHash, mixHash);
        }

        return new SubmitResult(share);
    }
}
