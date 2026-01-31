namespace Miningcore.Blockchain.Xelis.Configuration;

public class XelisDaemonEndpointConfigExtra
{
    /// <summary>
    /// Optional port for streaming WebSocket data
    /// </summary>
    public int? PortWs { get; set; }

    /// <summary>
    /// Optional http-path for streaming WebSocket data
    /// </summary>
    public string HttpPathWs { get; set; }

    /// <summary>
    /// Optional: Use SSL to for daemon websocket streaming
    /// </summary>
    public bool SslWs { get; set; }
}
