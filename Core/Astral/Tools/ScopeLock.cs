using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Astral.Tools;

public sealed class ScopeLock : IDisposable
{
    readonly object Lock;

    public ScopeLock(object LockObject)
    {
        Lock = LockObject;
        System.Threading.Monitor.Enter(Lock);
    }

    public void Dispose() => System.Threading.Monitor.Exit(Lock);
}