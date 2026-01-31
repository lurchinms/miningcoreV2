using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Miningcore.Blockchain.Xelis.DaemonResponses;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;

namespace Miningcore.Blockchain.Xelis;

public class XelisJob
{
    protected IMasterClock clock;
    protected double shareMultiplier;
    protected readonly IHashAlgorithm blake3Hasher = new Blake3();
    protected readonly IHashAlgorithm xelisHash = new XelisHash();
    protected readonly IHashAlgorithm xelisHashV2 = new XelisHashV2();

    protected readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
    public byte[] blockTargetBytes { get; protected set; }
    public uint256 blockTargetValue { get; protected set; }
    protected byte[] prevHashBytes;
    protected byte[] versionBytes;
    protected byte[] heightBytes;
    protected byte[] nTimeBytes;
    protected byte[] extraNonceBytes;
    protected byte[] tipsCountBytes;
    protected byte[] tipsBytes;
    protected byte[] transactionsCountBytes;
    protected byte[] transactionsBytes;
    protected byte[] minerPublicKeyBytes;

    protected object[] jobParams;

    #region API-Surface

    public GetBlockHeaderResponse BlockHeader { get; protected set; }
    public GetBlockTemplateResponse BlockTemplate { get; protected set; }
    public string PrevHash { get; protected set; }
    public double Difficulty { get; protected set; }
    public string network { get; protected set; }

    public string JobId { get; protected set; }

    protected virtual byte[] SerializeCoinbase(byte[] extranonceBytes, byte[] nonceBytes)
    {
        using(var stream = new MemoryStream(XelisConstants.BlockWorkSize))
        {
            var bw = new BinaryWriter(stream);

            bw.Write(prevHashBytes);
            bw.Write(nTimeBytes);
            bw.Write(nonceBytes);
            bw.Write(extranonceBytes);
            bw.Write(minerPublicKeyBytes);
            
            return stream.ToArray();
        }
    }

    protected virtual byte[] SerializeHeader(byte[] extranonceBytes, byte[] nonceBytes)
    {
        using(var stream = new MemoryStream())
        {
            var bw = new BinaryWriter(stream);

            bw.Write(versionBytes);
            bw.Write(heightBytes);
            bw.Write(nTimeBytes);
            bw.Write(nonceBytes);
            bw.Write(extranonceBytes);
            bw.Write(tipsCountBytes);

            if(tipsBytes != null)
                bw.Write(tipsBytes);

            bw.Write(transactionsCountBytes);

            if(transactionsBytes != null)
                bw.Write(transactionsBytes);

            bw.Write(minerPublicKeyBytes);

            return stream.ToArray();
        }
    }

    protected virtual Share ProcessShareInternal(
        StratumConnection worker, string nonce)
    {
        var context = worker.ContextAs<XelisWorkerContext>();
        var extranonceBytes = context.ExtraNonce1.HexToByteArray();
        var nonceBytes = nonce.HexToByteArray();

        var coinbaseBytes = SerializeCoinbase(extranonceBytes, nonceBytes);
        Span<byte> hashCoinbaseBytes = stackalloc byte[XelisConstants.HashSize];

        if(BlockTemplate.Algorithm == XelisConstants.AlgorithmXelisHashV2)
            xelisHashV2.Digest(coinbaseBytes, hashCoinbaseBytes);
        else
            xelisHash.Digest(coinbaseBytes, hashCoinbaseBytes);

        var targetHashCoinbaseBytes = new Target(new BigInteger(hashCoinbaseBytes, true, true));
        var hashCoinbaseBytesValue = targetHashCoinbaseBytes.ToUInt256();
        //throw new StratumException(StratumError.LowDifficultyShare, $"nonce: {nonce} ||| hashCoinbaseBytes: {hashCoinbaseBytes.ToHexString()} [Reverse: {hashCoinbaseBytes.ToNewReverseArray().ToHexString()}] ||| BigInteger: {targetHashCoinbaseBytes.ToBigInteger()} ||| Target: {hashCoinbaseBytesValue} [isBlockCandidate: {hashCoinbaseBytesValue <= blockTargetValue} - stratum: {new Target(new BigInteger(XelisUtils.DifficultyToTarget(context.Difficulty), true, true)).ToUInt256()} - blockTemplate: {blockTargetValue}] ||| shareDiff: {(double) new BigRational(XelisConstants.Diff1Target, targetHashCoinbaseBytes.ToBigInteger()) * shareMultiplier} [isStratumCandidate: {hashCoinbaseBytesValue <= new Target(new BigInteger(XelisUtils.DifficultyToTarget(context.Difficulty), true, true)).ToUInt256()} - stratum: {context.Difficulty} - blockTemplate: {Difficulty}]");

        // calc share-diff
        var shareDiff = (double) new BigRational(XelisConstants.Diff1Target, targetHashCoinbaseBytes.ToBigInteger()) * shareMultiplier;

        // diff check
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;

        // check if the share meets the much harder block difficulty (block candidate)
        var isBlockCandidate = hashCoinbaseBytesValue <= blockTargetValue;
        //var isBlockCandidate = XelisUtils.CheckDiff(hashCoinbaseBytes, blockTargetBytes);

        // test if share meets at least workers current difficulty
        if(!isBlockCandidate && ratio < 0.99)
        {
            // check if share matched the previous difficulty from before a vardiff retarget
            if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
            {
                ratio = shareDiff / context.PreviousDifficulty.Value;

                if(ratio < 0.99)
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share [{shareDiff}]");

                // use previous difficulty
                stratumDifficulty = context.PreviousDifficulty.Value;
            }

            else
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share [{shareDiff}]");
        }

        var result = new Share
        {
            BlockHeight = (long) BlockHeader.TopoHeight,
            NetworkDifficulty = Difficulty,
            Difficulty = stratumDifficulty,
        };

        if(isBlockCandidate)
        {
            result.IsBlockCandidate = true;

            Span<byte> blockHashBytes = stackalloc byte[XelisConstants.HashSize];
            blake3Hasher.Digest(coinbaseBytes, blockHashBytes);

            result.BlockHash = blockHashBytes.ToHexString();

            var blockHeaderBytes = SerializeHeader(extranonceBytes, nonceBytes);
            BlockHeader.Template = blockHeaderBytes.ToHexString();

            return result;
        }

        return result;
    }

    public virtual void Init(GetBlockHeaderResponse blockHeader, GetBlockTemplateResponse blockTemplate, string jobId, IMasterClock clock, string network, double shareMultiplier, string prevHash)
    {
        Contract.RequiresNonNull(blockHeader);
        Contract.RequiresNonNull(blockTemplate);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(network);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));
        Contract.RequiresNonNull(shareMultiplier);

        this.clock = clock;
        BlockHeader = blockHeader;
        BlockTemplate = blockTemplate;
        JobId = jobId;
        this.shareMultiplier = shareMultiplier;

        this.network = network;

        Difficulty = BlockTemplate.Difficulty;
        blockTargetBytes = XelisUtils.DifficultyToTarget(BlockTemplate.Difficulty);
        blockTargetValue = new Target(new BigInteger(blockTargetBytes, true, true)).ToUInt256();

        PrevHash = prevHash;
        prevHashBytes = PrevHash.HexToByteArray();

        var blockHeaderBytes = BlockHeader.Template.HexToByteArray();
        var blockTemplateBytes = BlockTemplate.Template.HexToByteArray();

        versionBytes = blockHeaderBytes.Take(XelisConstants.BlockHeaderOffsetHeight - XelisConstants.BlockHeaderOffsetVersion).ToArray();
        heightBytes = blockHeaderBytes.Skip(XelisConstants.BlockHeaderOffsetHeight).Take(XelisConstants.BlockHeaderOffsetTimestamp - XelisConstants.BlockHeaderOffsetHeight).ToArray();

        //nTimeBytes = (BitConverter.IsLittleEndian ? BitConverter.GetBytes(XelisUtils.UnixTimeStamp(clock.Now)).ReverseInPlace() : BitConverter.GetBytes(XelisUtils.UnixTimeStamp(clock.Now))); // xelis_daemon expects a big endian format.
        nTimeBytes = blockTemplateBytes.Skip(XelisConstants.BlockTemplateOffsetTimestamp).Take(XelisConstants.BlockTemplateOffsetNonce - XelisConstants.BlockTemplateOffsetTimestamp).ToArray();

        extraNonceBytes = blockTemplateBytes.Skip(XelisConstants.BlockTemplateOffsetExtraNonce).Take(XelisConstants.BlockTemplateOffsetMinerPublicKey - XelisConstants.BlockTemplateOffsetExtraNonce).ToArray();

        tipsCountBytes = blockHeaderBytes.Skip(XelisConstants.BlockHeaderOffsetTipsCount).Take(XelisConstants.BlockHeaderOffsetTips - XelisConstants.BlockHeaderOffsetTipsCount).ToArray();
        var tipsCount = uint.Parse(tipsCountBytes.ToHexString(), NumberStyles.HexNumber);
        if(tipsCount > 0)
            tipsBytes = blockHeaderBytes.Skip(XelisConstants.BlockHeaderOffsetTips).Take(XelisConstants.HashSize * (int) tipsCount).ToArray();

        transactionsCountBytes = blockHeaderBytes.Skip(XelisConstants.BlockHeaderOffsetTips + (XelisConstants.HashSize * (int) tipsCount)).Take(XelisConstants.BlockHeaderSizeTransactionsCount).ToArray();
        var transactionsCount = uint.Parse(transactionsCountBytes.ToHexString(), NumberStyles.HexNumber);
        if(transactionsCount > 0)
            transactionsBytes = blockHeaderBytes.Skip(XelisConstants.BlockHeaderOffsetTips + (XelisConstants.HashSize * (int) tipsCount) + XelisConstants.BlockHeaderSizeTransactionsCount).Take(XelisConstants.HashSize * (int) transactionsCount).ToArray();

        minerPublicKeyBytes = blockTemplateBytes.Skip(XelisConstants.BlockTemplateOffsetMinerPublicKey).Take(blockTemplateBytes.Length - XelisConstants.BlockTemplateOffsetMinerPublicKey).ToArray();

        jobParams = new object[]
        {
            JobId,
            nTimeBytes.ToHexString(),
            PrevHash,
            BlockTemplate.Algorithm,
            false
        };
    }

    public virtual object GetJobParams(bool isNew)
    {
        jobParams[^1] = isNew;
        return jobParams;
    }

    protected virtual bool RegisterSubmit(string extraNonce1, string nonce)
    {
        var key = new StringBuilder()
            .Append(extraNonce1)
            .Append(nonce)
            .ToString();

        return submissions.TryAdd(key, true);
    }

    public virtual Share ProcessShare(StratumConnection worker,
        string nonce)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));

        var context = worker.ContextAs<XelisWorkerContext>();

        // validate nonce
        if(nonce.Length != XelisConstants.NonceLength)
            throw new StratumException(StratumError.Other, "incorrect size of nonce");

        // dupe check
        if(!RegisterSubmit(context.ExtraNonce1, nonce))
            throw new StratumException(StratumError.DuplicateShare, "duplicate");

        return ProcessShareInternal(worker, nonce);
    }

    #endregion // API-Surface
}