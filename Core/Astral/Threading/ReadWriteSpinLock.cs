using System;
using System.Collections.Generic;
using System.Text;

namespace Astral.Threading;

public class ReadWriteSpinLock
{
    // 0 = Free
    // -1 = Writer active/pending
    // >0 = Reader count
    private int State;

    // --- WRITER (The Engine Thread) ---
    public void EnterWrite()
    {
        var sw = new SpinWait();
        while (true)
        {
            // 1. Try to claim the "Writer" intent (set to -1)
            if (State == 0 && Interlocked.CompareExchange(ref State, -1, 0) == 0)
            {
                return; // Got it!
            }

            // 2. If we set it to -1 but readers are still there, 
            // or if someone else is writing, spin.
            sw.SpinOnce();
        }
    }

    public void ExitWrite()
    {
        Volatile.Write(ref State, 0);
    }


    public void EnterRead()
    {
        var sw = new SpinWait();
        while (true)
        {
            int current = State;

            // Only allow entry if no writer is active/pending (_state >= 0)
            if (current >= 0 && Interlocked.CompareExchange(ref State, current + 1, current) == current)
            {
                return;
            }

            sw.SpinOnce();
        }
    }

    public void ExitRead()
    {
        Interlocked.Decrement(ref State);
    }
}