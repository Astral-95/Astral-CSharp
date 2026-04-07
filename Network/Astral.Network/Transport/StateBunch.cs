using Astral.Network.Serialization;

namespace Astral.Network.Transport;

public class StateBunch
{
    public int StateId { get; internal set; } = 0;
    public NetByteWriter Writer { get; set; }

    public bool Reliable { get; private set; } = false;
    public bool Ordered { get; private set; } = false;

    internal StateBunch()
    {
        Writer = new NetByteWriter();
    }

    public void Reset()
    {
        Writer.SetPos(0);
    }
}