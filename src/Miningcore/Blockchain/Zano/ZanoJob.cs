using System.Globalization;
using Miningcore.Blockchain.Zano.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Stratum;
using Miningcore.Util;
using Org.BouncyCastle.Math;
using Miningcore.Crypto.Hashing.Progpow;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Zano;

public class ZanoJob
{
    public ZanoJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId,
        ZanoCoinTemplate coin, PoolConfig poolConfig, ClusterConfig clusterConfig, string prevHash, IProgpowCache progpowHasher)
    {
        Contract.RequiresNonNull(blockTemplate);
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(clusterConfig);
        Contract.RequiresNonNull(instanceId);
        Contract.RequiresNonNull(progpowHasher);

        BlockTemplate = blockTemplate;
        PrepareBlobTemplate(instanceId);
        PrevHash = prevHash;
        ReservedOffset = BlockTemplate?.ReservedOffset ?? ZanoConstants.BlockTemplateReservedOffset;
        this.progpowHasher = progpowHasher;

        Span<byte> heightBytes = stackalloc byte[8];
        // inject height (big-endian) at the end of the reserved area
        var bytes = BitConverter.GetBytes(BlockTemplate.Height.ToBigEndian());
        bytes.CopyTo(heightBytes[4..]);

        Height = heightBytes.ToHexString(true);
        shareMultiplier = coin.ShareMultiplier;

        blobType = coin.BlobType;
    }

    protected double shareMultiplier;
    protected IProgpowCache progpowHasher;

    private int ReservedOffset;
    private byte[] blobTemplate;
    private byte[] instanceId;
    private int extraNonce;
    private readonly int blobType;

    protected virtual void PrepareBlobTemplate(byte[] instanceId)
    {
        this.instanceId = instanceId;
        blobTemplate = BlockTemplate.Blob.HexToByteArray();
    }

    protected virtual byte[] EncodeBlob(uint workerExtraNonce)
    {
        byte[] blobHash = new byte[ZanoConstants.EncodeBlobSize];
        // hash it
        ZanonoteBindings.GetBlobId(blobTemplate, blobHash);

        return blobHash;
    }

    protected virtual string EncodeTarget(double difficulty, int size = 32)
    {
        var diff = BigInteger.ValueOf((long) (difficulty * 255d));
        var quotient = ZanoConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
        var bytes = quotient.ToByteArray().AsSpan();
        Span<byte> padded = stackalloc byte[ZanoConstants.TargetPaddingLength];

        var padLength = padded.Length - bytes.Length;

        if(padLength > 0)
            bytes.CopyTo(padded.Slice(padLength, bytes.Length));
        else
            bytes.Slice(bytes.Length - padded.Length, padded.Length).CopyTo(padded);

        padded = padded[..size];

        return padded.ToHexString(true);
    }

    #region API-Surface

    public string PrevHash { get; }
    public GetBlockTemplateResponse BlockTemplate { get; }
    public string Height { get; protected set; }

    public virtual ZanoWorkerJob PrepareWorkerJob(double difficulty)
    {
        var workerExtraNonce = (uint) Interlocked.Increment(ref extraNonce);

        if(extraNonce < 0)
            extraNonce = 0;

        var workerJob = new ZanoWorkerJob(EncodeBlob(workerExtraNonce).ToHexString(true), difficulty);
        workerJob.Height = Height;
        workerJob.ExtraNonce = workerExtraNonce;
        workerJob.SeedHash = BlockTemplate.SeedHash.HexToByteArray().ToHexString(true);
        workerJob.Target = EncodeTarget(workerJob.Difficulty);

        return workerJob;
    }

    public virtual (Share Share, string BlobHex) ProcessShare(ILogger logger, string nonce, uint workerExtraNonce, StratumConnection worker)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));
        Contract.Requires<ArgumentException>(workerExtraNonce != 0);

        var context = worker.ContextAs<ZanoWorkerContext>();

        // validate nonce
        if(!ZanoConstants.RegexValidNonce.IsMatch(nonce))
            throw new StratumException(StratumError.MinusOne, "malformed nonce");

        if(!ulong.TryParse(nonce, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fullNonce))
            throw new StratumException(StratumError.MinusOne, "bad nonce " + nonce);

        var blobBytes = ZanonoteBindings.ConvertBlock(blobTemplate, blobTemplate.Length, fullNonce);

        // compute
        if(!progpowHasher.Compute(logger, (int) BlockTemplate.Height, EncodeBlob(workerExtraNonce), fullNonce, out var _, out var resultBytes))
            throw new StratumException(StratumError.MinusOne, "bad hash");

        resultBytes.ReverseInPlace();
        //throw new StratumException(StratumError.LowDifficultyShare, $"nonce: {nonce} ||| Height: {(int) BlockTemplate.Height} ||| blobBytes: {blobBytes.ToHexString()} ||| resultBytes: {resultBytes.ToHexString()} ||| headerValue: {resultBytes.AsSpan().ToBigInteger()} ||| shareDiff: {(double) new BigRational(ZanoConstants.Diff1b, resultBytes.AsSpan().ToBigInteger()) * shareMultiplier} [isBlockCandidate: {(((double) new BigRational(ZanoConstants.Diff1b, resultBytes.AsSpan().ToBigInteger()) * shareMultiplier) >= BlockTemplate.Difficulty)} - isStratumCandidate: {(((double) new BigRational(ZanoConstants.Diff1b, resultBytes.AsSpan().ToBigInteger()) * shareMultiplier) >= context.Difficulty)} - stratum: {context.Difficulty} - blockTemplate: {BlockTemplate.Difficulty}]");

        // check difficulty
        var headerValue = resultBytes.AsSpan().ToBigInteger();
        var shareDiff = (double) new BigRational(ZanoConstants.Diff1b, headerValue) * shareMultiplier;
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;
        var isBlockCandidate = shareDiff >= BlockTemplate.Difficulty;

        // test if share meets at least workers current difficulty
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

        var result = new Share
        {
            BlockHeight = BlockTemplate.Height,
            Difficulty = stratumDifficulty / shareMultiplier,
        };

        if(isBlockCandidate)
        {
            byte[] blockHash = new byte[32];
            // compute blob hash
            ZanonoteBindings.GetBlockId(blobBytes, blockHash);

            // Fill in block-relevant fields
            result.IsBlockCandidate = true;
            result.BlockHash = blockHash.ToHexString();

            return (result, blobBytes.ToHexString());
        }

        return (result, null);
    }

    #endregion // API-Surface
}
