namespace Astral.Network.Connections;

public partial class ServerConnection : NetaConnection
{
    public ServerConnection()
    {
    }




    //internal async Task PostHandshakeSetupAsync(CancellationToken CancellationToken = default)
    //{
    //	var Reader = await ReceiveHandshakeReaderAsync(NetDriver.ConnectTimeout, CancellationToken).ConfigureAwait(false);
    //
    //	var Con = Reader.SerializeObject();
    //	var Chan = Reader.SerializeObject();
    //}
}