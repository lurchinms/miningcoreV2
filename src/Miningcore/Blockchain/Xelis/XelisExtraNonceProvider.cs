namespace Miningcore.Blockchain.Xelis;

public class XelisExtraNonceProvider : ExtraNonceProviderBase
{
    public XelisExtraNonceProvider(string poolId, int size, byte? clusterInstanceId) : base(poolId, size, clusterInstanceId)
    {
    }
}
