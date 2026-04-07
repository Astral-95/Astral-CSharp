using Astral.Network.Drivers;
using Astral.Network.Enums;
using Astral.Network.Serialization;
using Astral.Network.Servers;

namespace Astral.Network.Connections;

public partial class ClientConnection : NetaConnection
{
#pragma warning disable CS8618
    protected ClientConnection() { throw new InvalidOperationException("Creating connection this way is not allowed."); }
    protected ClientConnection(int Marker) { if (Marker != int.MaxValue) throw new InvalidOperationException("Creating connection this way is not allowed."); }

    public ClientConnection(NetaServer Server)
    {
        ConnectionSetFlags(NetaConnectionFlags.Server);
        this.Server = Server;
    }


    class ClientConnection_HandleHandshake_Server { }
    internal protected virtual async Task<bool> HandleHandshake_Server(CancellationToken CancellationToken = default)
    {
        var Reader = await ReceiveHandshakeReaderAsync(NetaDriver.ConnectTimeout, CancellationToken).ConfigureAwait(false);

        string TextReceived = "";
        Reader.Serialize(ref TextReceived);

        if (TextReceived != "HelloServer")
        {
            throw new InvalidOperationException("Client sent an invalid packet during handshake. \n" +
                $"Expected: [HelloServer] Received: [{TextReceived}]");
        }

        PooledNetByteWriter Writer = RentWriter();

        string OutText = "Understandable";
        Writer.Serialize(OutText);
        SendHandshakeWriter<ClientConnection_HandleHandshake_Server>(Writer);
        Writer.Return();

        return true;
    }

    class ClientConnection_PostHandshakeSetup { }
    internal void PostHandshakeSetup()
    {
        PooledNetByteWriter Writer = RentWriter(128);

        PackageMap.SerializeObject(this, Writer);
        PackageMap.SerializeObject(Channel, Writer);
        PackageMap.ExportMappings(Writer);

        SendHandshakeWriter<ClientConnection_PostHandshakeSetup>(Writer);
        Writer.Return();
    }
}