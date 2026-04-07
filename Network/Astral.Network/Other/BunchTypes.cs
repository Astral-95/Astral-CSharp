using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Astral.Network.Enums;

[Flags]
public enum EBunchFlags : byte
{
    None = 0,
    Fragment = 1 << 0,
    Reliable = 1 << 1,
    Ordered = 1 << 2,
}